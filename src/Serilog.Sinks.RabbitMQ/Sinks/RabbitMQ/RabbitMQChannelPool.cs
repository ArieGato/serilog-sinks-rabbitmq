// Copyright 2015-2024 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using RabbitMQ.Client;
using Serilog.Debugging;

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// Fixed-size pool of <see cref="IRabbitMQChannel"/> instances. Channels are pre-opened
/// in the background at construction and broken channels are replaced asynchronously to
/// keep the pool full.
/// </summary>
internal sealed class RabbitMQChannelPool : IRabbitMQChannelPool
{
    /// <summary>
    /// Cooldown between exhaustion and the next probe attempt. Intentionally not
    /// user-configurable — exposed as an internal constant so the circuit-breaker
    /// tests can reason about wall-clock timing without touching private state.
    /// </summary>
    internal const int BROKEN_COOLDOWN_MS = 60_000;

    /// <summary>
    /// Cooldown expressed in <see cref="Stopwatch"/> ticks. <c>Stopwatch</c> is portable
    /// across every target framework (unlike <c>Environment.TickCount64</c>, which is
    /// .NET 5+) and is monotonic — wall-clock adjustments do not retroactively shrink or
    /// extend an in-flight cooldown.
    /// </summary>
    private static readonly long BROKEN_COOLDOWN_STOPWATCH_TICKS =
        Stopwatch.Frequency * BROKEN_COOLDOWN_MS / 1000;

    /// <summary>
    /// Lifecycle states for the channel pool. Exposed internally so the circuit-breaker
    /// tests can observe the state machine without reflecting on the backing int field.
    /// </summary>
    internal enum PoolState
    {
        /// <summary>Initial state: warm-up is filling the pool for the first time.</summary>
        Warming = 0,

        /// <summary>Fully warmed — all channels are in the pool (or checked out).</summary>
        Open = 1,

        /// <summary>Exhausted retries; <c>GetAsync</c> throws until the cooldown elapses.</summary>
        Broken = 2,

        /// <summary>Cooldown expired; a single probe warm-up is in flight.</summary>
        Probing = 3,
    }

    private readonly RabbitMQClientConfiguration _config;
    private readonly IRabbitMQConnectionFactory _connectionFactory;
    private readonly int _size;
    private readonly int _maxRetries;
    private readonly Channel<IRabbitMQChannel> _channels;
    private readonly SemaphoreSlim _exchangeDeclareLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private volatile bool _exchangeDeclared;
    private int _state; // PoolState as int for Interlocked APIs
    private long _brokenUntilTicks; // Stopwatch.GetTimestamp() at which the cooldown expires
    private int _consecutiveFailures;
    private int _disposed;

    /// <summary>
    /// Cancelled when the pool transitions to <see cref="PoolState.Broken"/> so any
    /// <see cref="GetAsync"/> caller currently parked in <c>ReadAsync</c> wakes up and
    /// routes to the batching sink's failure listener instead of staying hung for the
    /// remainder of the outage. Replaced with a fresh instance on probe success so the
    /// next pre-exhaustion window starts clean.
    /// </summary>
    private CancellationTokenSource _unhealthySignalCts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQChannelPool"/> class
    /// and starts background warm-up of the configured number of channels.
    /// </summary>
    /// <param name="configuration">The RabbitMQ client configuration.</param>
    /// <param name="connectionFactory">The connection factory used to obtain the underlying <see cref="IConnection"/>.</param>
    public RabbitMQChannelPool(
        RabbitMQClientConfiguration configuration,
        IRabbitMQConnectionFactory connectionFactory)
    {
        _config = configuration;
        _connectionFactory = connectionFactory;
        _size = configuration.ChannelCount;
        _maxRetries = configuration.WarmUpMaxRetries;
        _channels = Channel.CreateBounded<IRabbitMQChannel>(_size);
        _state = (int)PoolState.Warming;

        _ = Task.Run(() => WarmUpAsync(_size, markOpenOnCompletion: true, _shutdownCts.Token));
    }

    /// <summary>
    /// Current lifecycle state. Exposed for circuit-breaker tests so they can
    /// synchronise on state transitions without reflecting on the backing field.
    /// </summary>
    internal PoolState CurrentState => (PoolState)Volatile.Read(ref _state);

    /// <summary>
    /// Test-only setter for the state field. Used by the CAS-lost probe-race test to
    /// force an atypical state/observedState divergence; not wired up in production.
    /// </summary>
    internal void TestingSetState(PoolState state) => Volatile.Write(ref _state, (int)state);

    /// <inheritdoc />
    public async ValueTask<IRabbitMQChannel> GetAsync(CancellationToken cancellationToken = default)
    {
        // Fast path for a disposed pool: accessing _unhealthySignalCts.Token below
        // would throw ObjectDisposedException after DisposeAsync ran, which is
        // technically an InvalidOperationException but bypasses the structured
        // ChannelClosedException / OCE mapping below. Short-circuit here so the
        // disposal signal is deterministic.
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new InvalidOperationException("Channel pool has been disposed.");
        }

        var state = (PoolState)Volatile.Read(ref _state);

        if (state == PoolState.Broken || state == PoolState.Probing)
        {
            await HandleBrokenStateAsync(state, cancellationToken).ConfigureAwait(false);
        }

        // Link the caller's token with the "pool became unhealthy" signal so a
        // state transition to Broken wakes up any in-flight ReadAsync waiter —
        // otherwise a caller that parked during Warming (before the cumulative
        // warm-up backoff, controlled by WarmUpMaxRetries + GetBackoffDelay,
        // exhausts) stays hung on the empty channel until the probe eventually
        // succeeds, blocking BatchingSink from processing new batches and letting
        // the queue grow indefinitely. Waking the waiter lets it route through
        // the catch below to the exhaustion exception, which in turn routes events
        // to the configured fallback.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _unhealthySignalCts.Token);
        try
        {
            return await _channels.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw new InvalidOperationException("Channel pool has been disposed.");
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _disposed) != 0)
        {
            // Shutdown and the user's token can race inside ReadAsync; whichever fires
            // first observably "wins". Map to the disposal exception when _disposed is
            // set so callers see a deterministic failure mode rather than an
            // OperationCanceledException whose token might belong to either party
            // (issue #286 item 2).
            throw new InvalidOperationException("Channel pool has been disposed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Unhealthy-signal fired. Re-read state — if we've transitioned to Broken
            // or Probing, surface the exhaustion exception so the batch rethrows and
            // BatchingSink routes to the failure listener / Fallback chain.
            var next = (PoolState)Volatile.Read(ref _state);
            if (next == PoolState.Broken || next == PoolState.Probing)
            {
                throw PoolExhaustedException();
            }

            throw;
        }
    }

    /// <summary>
    /// Called from <see cref="GetAsync"/> when the pool state is <see cref="PoolState.Broken"/>
    /// or <see cref="PoolState.Probing"/>. Either throws the exhaustion exception (cooldown
    /// not elapsed, or another caller already owns the probe), or transitions to
    /// <see cref="PoolState.Probing"/>, runs a single warm-up attempt, and — on success —
    /// transitions to <see cref="PoolState.Warming"/> and kicks off full refill before
    /// returning so the caller can await <c>ReadAsync</c>.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Probe is a single-shot warm-up whose outcome determines whether the circuit-breaker stays open; any exception other than caller-cancellation must route the pool back to Broken without leaking. Original failure is summarised via SelfLog.")]
    private async Task HandleBrokenStateAsync(PoolState observedState, CancellationToken cancellationToken)
    {
        if (observedState == PoolState.Probing)
        {
            throw PoolExhaustedException();
        }

        var now = Stopwatch.GetTimestamp();
        var brokenUntil = Volatile.Read(ref _brokenUntilTicks);
        if (now < brokenUntil)
        {
            throw PoolExhaustedException();
        }

        // Cooldown elapsed. Try to claim the probe role; only one caller wins the CAS.
        if (Interlocked.CompareExchange(ref _state, (int)PoolState.Probing, (int)PoolState.Broken) != (int)PoolState.Broken)
        {
            throw PoolExhaustedException();
        }

        try
        {
            var channel = await CreateChannelAsync(cancellationToken).ConfigureAwait(false);
            if (!_channels.Writer.TryWrite(channel))
            {
                // Pool disposed mid-probe — dispose the orphan and fall through. The
                // caller's ReadAsync then observes ChannelClosedException and surfaces
                // the disposal consistently with the non-probe path.
                await channel.DisposeAsync().ConfigureAwait(false);
                return;
            }

            // Probe succeeded — reset counter, install a fresh unhealthy signal (the
            // previous one fired on exhaustion and would instantly poison new waiters),
            // transition to Warming, kick off refill.
            Volatile.Write(ref _consecutiveFailures, 0);
            Interlocked.Exchange(ref _unhealthySignalCts, new CancellationTokenSource()).Dispose();
            Interlocked.Exchange(ref _state, (int)PoolState.Warming);
            _ = Task.Run(() => WarmUpAsync(_size, markOpenOnCompletion: true, _shutdownCts.Token));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled; restore Broken so the next caller can probe again.
            RecordProbeFailure();
            throw;
        }
        catch (Exception ex)
        {
            SelfLog.WriteLine("Probe warm-up failed; pool remains broken: {0}", ex);
            RecordProbeFailure();
            throw PoolExhaustedException();
        }
    }

    private void RecordProbeFailure()
    {
        Volatile.Write(ref _brokenUntilTicks, Stopwatch.GetTimestamp() + BROKEN_COOLDOWN_STOPWATCH_TICKS);
        Interlocked.Exchange(ref _state, (int)PoolState.Broken);
        SignalUnhealthy();
    }

    /// <summary>
    /// Trips the unhealthy signal so any in-flight <see cref="GetAsync"/> waiter wakes
    /// and observes the transition to <see cref="PoolState.Broken"/>. Idempotent —
    /// subsequent calls on a cancelled CTS are no-ops.
    /// </summary>
    private void SignalUnhealthy()
    {
        try
        {
            _unhealthySignalCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Racing with DisposeAsync; the signal no longer matters.
        }
    }

    private InvalidOperationException PoolExhaustedException() =>
        new($"Channel pool exhausted after {_maxRetries} consecutive warm-up failures; broker is unreachable.");

    /// <summary>
    /// Test-only hook for the CAS-lost probe race. Production callers enter
    /// <see cref="HandleBrokenStateAsync"/> via <see cref="GetAsync"/>, which reads the
    /// state field once up-front and passes it as <c>observedState</c>. Forcing the real
    /// state to <see cref="PoolState.Probing"/> after that read — the race path where
    /// another caller won the CAS — requires coordinating two threads that deterministic
    /// tests cannot reliably produce. This hook simulates the loser's observation by
    /// calling the handler with <c>observedState = Broken</c> while the real state is
    /// Probing; the <c>CompareExchange(Broken → Probing)</c> then correctly fails and the
    /// handler throws the exhaustion exception.
    /// </summary>
    internal Task TestingInvokeHandleBrokenStateAsync(PoolState observedState, CancellationToken cancellationToken) =>
        HandleBrokenStateAsync(observedState, cancellationToken);

    /// <inheritdoc />
    public ValueTask ReturnAsync(IRabbitMQChannel channel)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return channel.DisposeAsync();
        }

        if (channel.IsOpen)
        {
            // TryWrite returns false when the writer has been completed (disposal raced
            // with the return) or when the queue is full (caller returned more channels
            // than they rented). Either way, the pool cannot take the channel; dispose
            // it and log so accidental over-returns are visible rather than silent.
            if (!_channels.Writer.TryWrite(channel))
            {
                return DisposeSurplusChannelAsync(channel);
            }

            return default;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await channel.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Failed to dispose broken RabbitMQ channel during return: {0}", ex.Message);
            }

            // Refill is "opportunistic" — don't advance state to Open on completion. Only
            // full initial warm-ups (and post-probe refills) do that.
            await WarmUpAsync(1, markOpenOnCompletion: false, _shutdownCts.Token).ConfigureAwait(false);
        });
        return default;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Surplus-channel disposal is best-effort clean-up for channels we cannot re-pool; any failure is logged to SelfLog and must not propagate into the caller's publish path (RabbitMQClient.PublishAsync's finally).")]
    private static async ValueTask DisposeSurplusChannelAsync(IRabbitMQChannel channel)
    {
        SelfLog.WriteLine("Returned channel could not be re-pooled (pool full or closed); disposing.");
        try
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SelfLog.WriteLine("Failed to dispose surplus RabbitMQ channel: {0}", ex.Message);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdownCts.Cancel();
        _channels.Writer.TryComplete();

        while (_channels.Reader.TryRead(out var channel))
        {
            try
            {
                await channel.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // best effort during shutdown
            }
        }

        _exchangeDeclareLock.Dispose();
        _shutdownCts.Dispose();
        _unhealthySignalCts.Dispose();
    }

    private async Task WarmUpAsync(int count, bool markOpenOnCompletion, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            if (!await WarmUpSingleAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }

        if (markOpenOnCompletion)
        {
            // Full warm-up completed. Only advance Warming → Open; callers in Broken or
            // Probing may have transitioned us while this task was running (e.g. a refill
            // warm-up raced the probe state machine) and those transitions must stand.
            Interlocked.CompareExchange(ref _state, (int)PoolState.Open, (int)PoolState.Warming);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Warm-up retries on any transient broker error so the pool can recover from network or broker hiccups without taking the sink down. Only exhaustion (and shutdown) terminates the loop.")]
    private async Task<bool> WarmUpSingleAsync(CancellationToken cancellationToken)
    {
        // while (true): all exits happen through return paths inside the body (success,
        // orphan, cancellation, or exhaustion). Looping with an explicit cancellation
        // check as the condition added an unreachable-in-practice branch that skewed
        // coverage for no added safety.
        while (true)
        {
            // Don't attempt if another warm-up already transitioned us to Broken — let
            // the circuit breaker's cooldown hold. (Broken state is reset on probe
            // success by HandleBrokenStateAsync.)
            if ((PoolState)Volatile.Read(ref _state) == PoolState.Broken)
            {
                return false;
            }

            try
            {
                var channel = await CreateChannelAsync(cancellationToken).ConfigureAwait(false);
                if (!_channels.Writer.TryWrite(channel))
                {
                    // Pool closed while we were creating the channel; dispose the orphan.
                    await channel.DisposeAsync().ConfigureAwait(false);
                    return false;
                }

                // Any successful channel addition resets the consecutive-failure counter,
                // so a single transient blip cannot combine with later failures to reach
                // WarmUpMaxRetries.
                Volatile.Write(ref _consecutiveFailures, 0);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                var failures = Interlocked.Increment(ref _consecutiveFailures);
                SelfLog.WriteLine("Failed to warm up RabbitMQ channel (attempt {0}): {1}", failures, ex);

                if (_maxRetries > 0 && failures >= _maxRetries)
                {
                    SelfLog.WriteLine(
                        "Channel pool exhausted after {0} consecutive warm-up failures; breaking for {1} ms.",
                        _maxRetries,
                        BROKEN_COOLDOWN_MS);
                    Volatile.Write(ref _brokenUntilTicks, Stopwatch.GetTimestamp() + BROKEN_COOLDOWN_STOPWATCH_TICKS);

                    // Only transition from Warming/Open; the probing path owns its own transitions.
                    _ = Interlocked.CompareExchange(ref _state, (int)PoolState.Broken, (int)PoolState.Warming);
                    _ = Interlocked.CompareExchange(ref _state, (int)PoolState.Broken, (int)PoolState.Open);

                    // Wake any in-flight GetAsync waiters so they see Broken and route to
                    // the failure listener rather than staying hung for the rest of the
                    // outage. Must happen AFTER the state transition so the woken caller
                    // observes Broken, not Warming.
                    SignalUnhealthy();
                    return false;
                }

                try
                {
                    await Task.Delay(GetBackoffDelay(failures), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Exponential backoff schedule used between warm-up attempts. Input is the
    /// consecutive-failure count *after* the failure that triggered the delay, so
    /// attempt 1 → 500 ms, attempt 2 → 1 s, doubling up to a 30 s cap.
    /// </summary>
    internal static TimeSpan GetBackoffDelay(int failures) => failures switch
    {
        <= 1 => TimeSpan.FromMilliseconds(500),
        2 => TimeSpan.FromSeconds(1),
        3 => TimeSpan.FromSeconds(2),
        4 => TimeSpan.FromSeconds(4),
        5 => TimeSpan.FromSeconds(8),
        6 => TimeSpan.FromSeconds(16),
        _ => TimeSpan.FromSeconds(30),
    };

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Cleanup path for a newly-created IChannel: any post-creation failure (declare error, cancellation, etc.) must route through DisposeAsync before the original exception is rethrown, regardless of source.")]
    private async Task<IRabbitMQChannel> CreateChannelAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.GetConnectionAsync().ConfigureAwait(false);
        var underlyingChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var channel = new RabbitMQChannel(underlyingChannel);

        try
        {
            if (_config.AutoCreateExchange && !_exchangeDeclared)
            {
                await _exchangeDeclareLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // Double-check under the lock: another warm-up may have declared the
                    // exchange while we were waiting for the semaphore.
                    if (!_exchangeDeclared)
                    {
                        await underlyingChannel.ExchangeDeclareAsync(
                            _config.Exchange,
                            _config.ExchangeType,
                            _config.DeliveryMode == RabbitMQDeliveryMode.Durable,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        _exchangeDeclared = true;
                    }
                }
                finally
                {
                    // DisposeAsync can dispose _exchangeDeclareLock while we hold it —
                    // Release() then throws ObjectDisposedException into the warm-up's
                    // catch-all, which logs it to SelfLog as noise (issue #286 item 4).
                    // The race is benign (disposal happens after the lock has served its
                    // purpose), so swallow the specific exception type without polluting
                    // SelfLog or the broader catch in WarmUpSingleAsync.
                    try
                    {
                        _exchangeDeclareLock.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Pool was disposed while we held the lock; nothing to release.
                    }
                }
            }

            return channel;
        }
        catch
        {
            // Close the newly-created channel before rethrowing so WarmUpSingleAsync's
            // retry loop does not leak IChannel instances on repeated declare failures.
            // Nested try/catch: a failure inside DisposeAsync must not mask the
            // original exception (e.g. ExchangeDeclareAsync's error).
            try
            {
                await channel.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // best-effort cleanup
            }

            throw;
        }
    }
}
