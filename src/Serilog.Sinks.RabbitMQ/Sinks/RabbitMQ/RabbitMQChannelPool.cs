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
    private static readonly TimeSpan WarmUpRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly RabbitMQClientConfiguration _config;
    private readonly IRabbitMQConnectionFactory _connectionFactory;
    private readonly int _size;
    private readonly Channel<IRabbitMQChannel> _channels;
    private readonly SemaphoreSlim _exchangeDeclareLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private volatile bool _exchangeDeclared;
    private int _disposed;

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
        _size = configuration.ChannelCount > 0 ? configuration.ChannelCount : RabbitMQClient.DEFAULT_CHANNEL_COUNT;
        _channels = Channel.CreateBounded<IRabbitMQChannel>(_size);

        _ = Task.Run(() => WarmUpAsync(_size, _shutdownCts.Token));
    }

    /// <inheritdoc />
    public async ValueTask<IRabbitMQChannel> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _channels.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw new InvalidOperationException("Channel pool has been disposed.");
        }
    }

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

            await WarmUpAsync(1, _shutdownCts.Token).ConfigureAwait(false);
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
    }

    private async Task WarmUpAsync(int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            if (!await WarmUpSingleAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Warm-up retries on any transient broker error so the pool can recover from network or broker hiccups without taking the sink down.")]
    private async Task<bool> WarmUpSingleAsync(CancellationToken cancellationToken)
    {
        // while (true): all exits happen through return paths inside the body (success,
        // orphan, or cancellation from one of the awaited operations). Looping with an
        // explicit cancellation-check as the condition added an unreachable-in-practice
        // branch that skewed coverage for no added safety.
        while (true)
        {
            try
            {
                var channel = await CreateChannelAsync(cancellationToken).ConfigureAwait(false);
                if (!_channels.Writer.TryWrite(channel))
                {
                    // Pool closed while we were creating the channel; dispose the orphan.
                    await channel.DisposeAsync().ConfigureAwait(false);
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Failed to warm up RabbitMQ channel: {0}", ex);
                try
                {
                    await Task.Delay(WarmUpRetryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }
    }

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
                    _exchangeDeclareLock.Release();
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
