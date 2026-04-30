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
using System.Reflection;
using System.Threading.Channels;

using Serilog.Sinks.RabbitMQ.Tests.TestHelpers;

namespace Serilog.Sinks.RabbitMQ.Tests.RabbitMQ;

[Collection("SelfLog")]
public class RabbitMQChannelPoolTests
{
    private static IConnection BuildConnectionWithChannelFactory(Func<IChannel> channelFactory)
    {
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(channelFactory()));
        return connection;
    }

    private static IRabbitMQConnectionFactory BuildConnectionFactory(IConnection connection)
    {
        var connectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        connectionFactory.GetConnectionAsync().Returns(connection);
        return connectionFactory;
    }

    private static IChannel CreateOpenChannel()
    {
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        return channel;
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (deadline.Elapsed > limit)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Constructor_StartsBackgroundWarmUp_FillsToChannelCount()
    {
        // Arrange
        int created = 0;
        var connection = BuildConnectionWithChannelFactory(() =>
        {
            Interlocked.Increment(ref created);
            return CreateOpenChannel();
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 4 };

        // Act
        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await WaitForAsync(() => Volatile.Read(ref created) == 4);

        // Assert
        Volatile.Read(ref created).ShouldBe(4);
    }

    [Fact]
    public async Task GetAsync_ReturnsWarmedChannel_WithoutAdditionalCreation()
    {
        // Arrange
        int created = 0;
        var connection = BuildConnectionWithChannelFactory(() =>
        {
            Interlocked.Increment(ref created);
            return CreateOpenChannel();
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 2 };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await WaitForAsync(() => Volatile.Read(ref created) == 2);

        // Act
        var channel = await pool.GetAsync();

        // Assert
        channel.ShouldNotBeNull();
        Volatile.Read(ref created).ShouldBe(2);
    }

    [Fact]
    public async Task GetAsync_BlocksUntilChannelIsReturned()
    {
        // Arrange
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        var first = await pool.GetAsync();

        // Act
        var pending = pool.GetAsync().AsTask();
        await Task.Delay(50);
        pending.IsCompleted.ShouldBeFalse();

        await pool.ReturnAsync(first);
        var second = await pending;

        // Assert
        second.ShouldBe(first);
    }

    [Fact]
    public async Task ReturnAsync_WhenChannelIsClosed_DisposesAndTriggersReplacement()
    {
        // Arrange
        int created = 0;
        var connection = BuildConnectionWithChannelFactory(() =>
        {
            Interlocked.Increment(ref created);
            return CreateOpenChannel();
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        _ = await pool.GetAsync();

        var brokenChannel = Substitute.For<IRabbitMQChannel>();
        brokenChannel.IsOpen.Returns(false);

        // Act
        await pool.ReturnAsync(brokenChannel);

        // Assert — dead channel is disposed asynchronously by the replacement task.
        await WaitForAsync(() => brokenChannel.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == nameof(IAsyncDisposable.DisposeAsync)));
        await WaitForAsync(() => Volatile.Read(ref created) == 2);
        await brokenChannel.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ReturnAsync_WhenBrokenChannelDisposeThrows_StillRefillsPoolAndLogs()
    {
        // Arrange — the broken channel's DisposeAsync throws. The fire-and-forget
        // replacement task must catch the exception (so it is not unobserved),
        // log it to SelfLog, and still run WarmUpAsync to refill the pool.
        // [Collection("SelfLog")] serialises writers so the content assertion
        // below is deterministic (issue #282).
        using var selfLog = new SelfLogScope(out var selfLogBuilder);

        int created = 0;
        var connection = BuildConnectionWithChannelFactory(() =>
        {
            Interlocked.Increment(ref created);
            return CreateOpenChannel();
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        _ = await pool.GetAsync();

        var brokenChannel = Substitute.For<IRabbitMQChannel>();
        brokenChannel.IsOpen.Returns(false);
        brokenChannel.When(x => x.DisposeAsync()).Do(_ => throw new InvalidOperationException("dispose-boom"));

        // Act
        await pool.ReturnAsync(brokenChannel);

        // Assert — replacement still happens and SelfLog records the swallowed error.
        await WaitForAsync(() => Volatile.Read(ref created) == 2);
        await brokenChannel.Received(1).DisposeAsync();
        selfLogBuilder.ToString().ShouldContain("dispose-boom");
    }

    [Fact]
    public async Task GetAsync_RespectsCancellationToken()
    {
        // Arrange
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        _ = await pool.GetAsync();

        using var cts = new CancellationTokenSource();

        // Act
        var pending = pool.GetAsync(cts.Token).AsTask();
        cts.Cancel();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(pending);
    }

    [Fact]
    public async Task DisposeAsync_DisposesRetainedChannels()
    {
        // Arrange
        var disposedChannels = new List<IChannel>();
        var connection = BuildConnectionWithChannelFactory(() =>
        {
            var channel = CreateOpenChannel();
            channel.When(c => c.Dispose()).Do(_ => disposedChannels.Add(channel));
            return channel;
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 3 };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await WaitForAsync(() => connection.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IConnection.CreateChannelAsync)) == 3);

        // Act
        await pool.DisposeAsync();

        // Assert — RabbitMQChannel.DisposeAsync closes then Dispose()s the underlying IChannel.
        disposedChannels.Count.ShouldBe(3);
    }

    [Fact]
    public async Task DisposeAsync_SwallowsExceptions_WhenRetainedChannelDisposeThrows()
    {
        // Arrange — warm the pool, then smuggle in a throwing channel via ReturnAsync.
        // Pool.DisposeAsync must absorb per-channel failures so one bad channel does not
        // leak the pool's SemaphoreSlim or CancellationTokenSource.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await WaitForAsync(() => connection.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IConnection.CreateChannelAsync)) == 1);

        // Rent the warmed-up channel first so the substituted throwing channel can be
        // returned without overflowing the pool's SemaphoreSlim (capped at ChannelCount).
        _ = await pool.GetAsync();

        var throwingChannel = Substitute.For<IRabbitMQChannel>();
        throwingChannel.IsOpen.Returns(true);
        throwingChannel.When(x => x.DisposeAsync()).Do(_ => throw new InvalidOperationException("boom"));
        await pool.ReturnAsync(throwingChannel);

        // Act + Assert — drains the throwing channel; catch swallows the exception.
        await Should.NotThrowAsync(async () => await pool.DisposeAsync());
        await throwingChannel.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Warmup_DeclaresExchangeOnce_EvenAcrossReplacements()
    {
        // Arrange
        var createdChannels = new List<IChannel>();
        var connection = BuildConnectionWithChannelFactory(() =>
        {
            var channel = CreateOpenChannel();
            lock (createdChannels)
            {
                createdChannels.Add(channel);
            }

            return channel;
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 2,
            Exchange = "x",
            ExchangeType = "topic",
            AutoCreateExchange = true,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await WaitForAsync(() =>
        {
            lock (createdChannels)
            {
                return createdChannels.Count == 2;
            }
        });

        _ = await pool.GetAsync();

        var brokenChannel = Substitute.For<IRabbitMQChannel>();
        brokenChannel.IsOpen.Returns(false);

        // Act
        await pool.ReturnAsync(brokenChannel);
        await WaitForAsync(() =>
        {
            lock (createdChannels)
            {
                return createdChannels.Count == 3;
            }
        });

        // Assert: ExchangeDeclareAsync called exactly once across all created channels.
        IChannel[] snapshot;
        lock (createdChannels)
        {
            snapshot = createdChannels.ToArray();
        }

        var declareCalls = snapshot.Sum(c => c.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(IChannel.ExchangeDeclareAsync)));

        declareCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Warmup_DeclaresExchangeOnce_UnderConcurrentWarmup()
    {
        // Deterministic reproducer for the exchange-declare race on _exchangeDeclared.
        // The first channel's ExchangeDeclareAsync blocks on a gate; while blocked, a
        // parallel Task.Run(WarmUpAsync(1)) is spawned via ReturnAsync(brokenChannel).
        // On master both warmups see _exchangeDeclared == false and each declare the
        // exchange — declareCalls == 2, test fails. After the fix, the second path
        // serialises on the guard and sees the flag set, so declareCalls == 1.
        // TaskCompletionSource non-generic is .NET 5+; use bool variant for net48.
        var gate = new TaskCompletionSource<bool>();
        var createdChannels = new List<IChannel>();

        var connection = BuildConnectionWithChannelFactory(() =>
        {
            var channel = CreateOpenChannel();
            channel.ExchangeDeclareAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<IDictionary<string, object?>?>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(gate.Task);

            lock (createdChannels)
            {
                createdChannels.Add(channel);
            }

            return channel;
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            Exchange = "x",
            ExchangeType = "topic",
            AutoCreateExchange = true,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Wait until the first channel has entered ExchangeDeclareAsync (now blocked on the gate).
        await WaitForAsync(() =>
        {
            lock (createdChannels)
            {
                return createdChannels.Any(c => c.ReceivedCalls()
                    .Any(call => call.GetMethodInfo().Name == nameof(IChannel.ExchangeDeclareAsync)));
            }
        });

        // Spawn a parallel warmup. ReturnAsync of a broken channel triggers
        // Task.Run(WarmUpAsync(1)); while the initial warmup is blocked inside
        // ExchangeDeclareAsync, _exchangeDeclared is still false, so the buggy
        // code enters the declare branch a second time.
        var brokenChannel = Substitute.For<IRabbitMQChannel>();
        brokenChannel.IsOpen.Returns(false);
        await pool.ReturnAsync(brokenChannel);

        // Wait until the second channel has been created so we know the parallel
        // WarmUpAsync has reached CreateChannelAsync's declare check.
        await WaitForAsync(() =>
        {
            lock (createdChannels)
            {
                return createdChannels.Count >= 2;
            }
        });

        // Give the second warmup time to pass CreateChannelAsync and enter the declare
        // check. On master (buggy code) it must reach ExchangeDeclareAsync before we
        // release the gate, otherwise the flag will already be set by the first warmup
        // and the race will not fire (false green). 500 ms is generous for slow CI
        // workers; this happens on every run so the cost is bounded.
        await Task.Delay(500);

        gate.SetResult(true);

        IChannel[] snapshot;
        lock (createdChannels)
        {
            snapshot = createdChannels.ToArray();
        }

        var declareCalls = snapshot.Sum(c => c.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(IChannel.ExchangeDeclareAsync)));

        declareCalls.ShouldBe(1);
    }

    [Fact]
    public async Task CreateChannelAsync_WhenDeclareFails_ClosesUnderlyingChannel()
    {
        // Deterministic reproducer for the channel leak on declare failure (issue #280).
        // When ExchangeDeclareAsync throws during warm-up, the just-created IChannel
        // must be closed rather than leaked to the broker. On master the channel is
        // never closed — the outer WarmUpSingleAsync catch simply retries with a fresh
        // channel and the old one becomes an orphan. After the fix, CreateChannelAsync
        // wraps cleanup in a try/catch that disposes the channel before rethrowing.
        var createdChannels = new List<IChannel>();

        var connection = BuildConnectionWithChannelFactory(() =>
        {
            var channel = CreateOpenChannel();
            channel.ExchangeDeclareAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<IDictionary<string, object?>?>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("declare-fail")));

            lock (createdChannels)
            {
                createdChannels.Add(channel);
            }

            return channel;
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            Exchange = "x",
            ExchangeType = "topic",
            AutoCreateExchange = true,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Wait until at least one channel has had ExchangeDeclareAsync invoked (and
        // thrown). Any such channel must subsequently have CloseAsync called on it.
        IChannel? firstFailed = null;
        await WaitForAsync(() =>
        {
            lock (createdChannels)
            {
                firstFailed = createdChannels.FirstOrDefault(c => c.ReceivedCalls()
                    .Any(call => call.GetMethodInfo().Name == nameof(IChannel.ExchangeDeclareAsync)));
                return firstFailed is not null;
            }
        });

        firstFailed.ShouldNotBeNull();
        var failedChannel = firstFailed;

        await WaitForAsync(() => failedChannel.ReceivedCalls()
            .Any(call => call.GetMethodInfo().Name == nameof(IChannel.CloseAsync)));

        await failedChannel.Received().CloseAsync();
    }

    [Fact]
    public async Task CreateChannelAsync_WhenDeclareFailsAndDisposeAlsoThrows_SwallowsDisposeAndRetries()
    {
        // Covers the nested catch in CreateChannelAsync that protects the original
        // declare exception from being masked by a throwing DisposeAsync. Setup:
        // ExchangeDeclareAsync throws the "real" error, and _channel.Dispose() also
        // throws during cleanup. Without the nested try/catch, the Dispose failure
        // would replace the declare failure as the observed exception. With it, the
        // dispose error is swallowed and the warmup loop retries cleanly — proven by
        // a second channel being created after the 500 ms retry delay.
        var createdChannels = new List<IChannel>();

        var connection = BuildConnectionWithChannelFactory(() =>
        {
            var channel = CreateOpenChannel();
            channel.ExchangeDeclareAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<IDictionary<string, object?>?>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("declare-fail")));
            channel.When(c => c.Dispose())
                .Do(_ => throw new InvalidOperationException("dispose-fail"));

            lock (createdChannels)
            {
                createdChannels.Add(channel);
            }

            return channel;
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            Exchange = "x",
            ExchangeType = "topic",
            AutoCreateExchange = true,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Wait until the first channel has had Dispose() invoked. Dispose is called
        // from RabbitMQChannel.DisposeAsync and (per the mock setup) throws, so the
        // swallow path in CreateChannelAsync's nested catch MUST have executed for
        // this to observe as completed. Deterministic — no dependency on the 500 ms
        // retry delay, so it stays stable even when tests run in parallel classes.
        IChannel? first = null;
        await WaitForAsync(() =>
        {
            lock (createdChannels)
            {
                first = createdChannels.FirstOrDefault();
                return first?.ReceivedCalls()
                    .Any(call => call.GetMethodInfo().Name == nameof(IDisposable.Dispose)) == true;
            }
        });

        first.ShouldNotBeNull();
        first.Received().Dispose();
    }

    [Fact]
    public async Task CreateChannelAsync_WhenDeclareCancelled_ClosesUnderlyingChannel()
    {
        // Companion to CreateChannelAsync_WhenDeclareFails_ClosesUnderlyingChannel,
        // covering the cancellation path. Gates ExchangeDeclareAsync with a TCS, then
        // disposes the pool so the warm-up's cancellation token fires during declare.
        // The catch in CreateChannelAsync is bare precisely to cover OperationCanceled
        // too; this test proves it.
        var declareGate = new TaskCompletionSource<bool>();
        var createdChannels = new List<IChannel>();

        var connection = BuildConnectionWithChannelFactory(() =>
        {
            var channel = CreateOpenChannel();
            channel.ExchangeDeclareAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<IDictionary<string, object?>?>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(declareGate.Task);

            lock (createdChannels)
            {
                createdChannels.Add(channel);
            }

            return channel;
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            Exchange = "x",
            ExchangeType = "topic",
            AutoCreateExchange = true,
        };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Wait until warm-up has entered ExchangeDeclareAsync (blocked on the gate).
        IChannel? target = null;
        await WaitForAsync(() =>
        {
            lock (createdChannels)
            {
                target = createdChannels.FirstOrDefault(c => c.ReceivedCalls()
                    .Any(call => call.GetMethodInfo().Name == nameof(IChannel.ExchangeDeclareAsync)));
                return target is not null;
            }
        });

        target.ShouldNotBeNull();
        var blockedChannel = target;

        // Dispose the pool: _shutdownCts cancels the warm-up's token. The declare call
        // becomes cancelled (TrySetCanceled propagates via the task), the catch fires,
        // and the channel must be closed.
        declareGate.TrySetCanceled();
        await pool.DisposeAsync();

        await WaitForAsync(() => blockedChannel.ReceivedCalls()
            .Any(call => call.GetMethodInfo().Name == nameof(IChannel.CloseAsync)));

        await blockedChannel.Received().CloseAsync();
    }

    [Fact]
    public async Task GetAsync_AfterDispose_ThrowsInvalidOperationException()
    {
        // Covers the new ChannelClosedException-to-InvalidOperationException mapping
        // in GetAsync, which replaces the old OperationCanceledException signalling
        // for pool disposal.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await WaitForAsync(() => connection.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == nameof(IConnection.CreateChannelAsync)));

        await pool.DisposeAsync();

        await Should.ThrowAsync<InvalidOperationException>(async () => await pool.GetAsync());
    }

    [Fact]
    public async Task ReturnAsync_WhenPoolIsFull_DisposesSurplusChannel()
    {
        // Covers the new surplus-channel branch in ReturnAsync: Writer.TryWrite returns
        // false when the bounded Channel<T> is at capacity and no GetAsync has drained
        // it. The channel should be disposed via DisposeSurplusChannelAsync rather than
        // leaking.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Drain+return the warmed channel to guarantee the pool is at capacity before
        // we test the surplus path. Just waiting on ReceivedCalls races with the write.
        var warmed = await pool.GetAsync();
        await pool.ReturnAsync(warmed);

        var surplusChannel = Substitute.For<IRabbitMQChannel>();
        surplusChannel.IsOpen.Returns(true);

        await pool.ReturnAsync(surplusChannel);

        await surplusChannel.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ReturnAsync_WhenSurplusChannelDisposeThrows_SwallowsException()
    {
        // Covers the catch arm inside DisposeSurplusChannelAsync. A surplus channel
        // whose DisposeAsync throws must not propagate the exception to the caller
        // (RabbitMQClient.PublishAsync's finally); the error is best-effort logged
        // to SelfLog.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        var warmed = await pool.GetAsync();
        await pool.ReturnAsync(warmed);

        var surplusChannel = Substitute.For<IRabbitMQChannel>();
        surplusChannel.IsOpen.Returns(true);
        surplusChannel.When(c => c.DisposeAsync())
            .Do(_ => throw new InvalidOperationException("surplus-dispose-fail"));

        await Should.NotThrowAsync(async () => await pool.ReturnAsync(surplusChannel));
        await surplusChannel.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task WarmUp_DisposesOrphanChannel_WhenPoolClosesBeforeTryWrite()
    {
        // Covers the orphan-dispose branch in WarmUpSingleAsync: CreateChannelAsync
        // completes AFTER the pool has been disposed, so Writer.TryWrite returns false
        // and the newly-created channel must be disposed rather than leaked.
        var gate = new TaskCompletionSource<bool>();
        var channelCreationEntered = new TaskCompletionSource<bool>();
        var disposedChannels = new List<IChannel>();

        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                channelCreationEntered.TrySetResult(true);
                await gate.Task;
                var ch = CreateOpenChannel();
                ch.When(c => c.Dispose()).Do(_ => disposedChannels.Add(ch));
                return ch;
            });

        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Wait until WarmUp has entered CreateChannelAsync (now blocked on the gate).
        await channelCreationEntered.Task;

        // Dispose the pool while the channel is still being "created". This calls
        // Writer.TryComplete(), so the eventual TryWrite in WarmUpSingleAsync fails.
        await pool.DisposeAsync();

        // Release the gate: CreateChannelAsync now returns, WarmUpSingleAsync attempts
        // TryWrite, fails, and should dispose the orphan channel.
        gate.SetResult(true);

        await WaitForAsync(() => disposedChannels.Count >= 1);
        disposedChannels.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ReturnAsync_AfterDispose_DisposesChannel()
    {
        // Arrange
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await pool.DisposeAsync();

        var channel = Substitute.For<IRabbitMQChannel>();

        // Act
        await pool.ReturnAsync(channel);

        // Assert
        await channel.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        // Arrange
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Act + Assert: second DisposeAsync is a no-op and must not throw.
        await pool.DisposeAsync();
        await Should.NotThrowAsync(async () => await pool.DisposeAsync());
    }

    [Fact]
    public async Task WarmUp_RetriesAfterTransientFailure()
    {
        // Arrange
        int attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                return attempt == 1
                    ? throw new InvalidOperationException("transient")
                    : Task.FromResult(CreateOpenChannel());
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        // Act
        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        var channel = await pool.GetAsync();

        // Assert
        channel.ShouldNotBeNull();
        Volatile.Read(ref attempts).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task DisposeAsync_WhileCreateChannelIsAwaiting_StopsWarmUp()
    {
        // Arrange — CreateChannelAsync hangs on the cancellation token. When the pool
        // is disposed the token cancels and Task.Delay throws OperationCanceledException
        // from inside the warm-up's outer try, exercising the cancellation-observing arm
        // of the catch for OperationCanceledException.
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(async call =>
            {
                var ct = call.ArgAt<CancellationToken>(1);
                await Task.Delay(Timeout.Infinite, ct);
                return CreateOpenChannel();
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await Task.Delay(50);

        // Act + Assert
        await Should.NotThrowAsync(async () => await pool.DisposeAsync());
    }

    [Fact]
    public async Task DisposeAsync_WhileWarmUpIsRetrying_ExitsCleanly()
    {
        // Arrange — every CreateChannelAsync attempt fails so warm-up loops on the
        // retry delay; Dispose should cancel the loop without hanging.
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ => throw new InvalidOperationException("permanent"));
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await Task.Delay(50);

        // Act + Assert
        await Should.NotThrowAsync(async () => await pool.DisposeAsync());
    }

    [Fact]
    public async Task CreateChannelAsync_SwallowsObjectDisposedException_OnRelease_WhenDisposeRacesDeclare()
    {
        // Item 4 from #286: if DisposeAsync disposes _exchangeDeclareLock while a warm-up
        // is still holding it (inside ExchangeDeclareAsync), the `finally { Release(); }`
        // in CreateChannelAsync throws ObjectDisposedException into the catch-all handler
        // and SelfLog gets a noisy (non-fatal) entry. The narrow fix swallows the
        // ObjectDisposedException specifically, so the ODE neither leaks out nor hits
        // the broader warm-up catch. Asserts: DisposeAsync completes, and SelfLog
        // contains no ObjectDisposedException trace from the warm-up path.
        using var selfLog = new SelfLogScope(out var selfLogBuilder);

        var declareGate = new TaskCompletionSource<bool>();
        var disposed = new TaskCompletionSource<bool>();
        var channel = CreateOpenChannel();
        channel.When(c => c.Dispose()).Do(_ => disposed.TrySetResult(true));
        channel.ExchangeDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(declareGate.Task);

        var connection = BuildConnectionWithChannelFactory(() => channel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            Exchange = "x",
            ExchangeType = "topic",
            AutoCreateExchange = true,
        };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Wait until warm-up is inside ExchangeDeclareAsync, holding _exchangeDeclareLock.
        await WaitForAsync(() => channel.ReceivedCalls()
            .Any(call => call.GetMethodInfo().Name == nameof(IChannel.ExchangeDeclareAsync)));

        // Dispose the pool while the lock is held. DisposeAsync does not wait for the
        // warm-up; it disposes _exchangeDeclareLock synchronously as part of shutdown.
        await pool.DisposeAsync();

        // Release the declare. The `finally` in CreateChannelAsync now hits a disposed
        // semaphore; the targeted catch swallows the ObjectDisposedException. The pool
        // writer is completed at this point so WarmUpSingleAsync's TryWrite fails and
        // the orphan channel is disposed — we synchronise on that dispose to know the
        // warm-up has run through its finally block.
        declareGate.SetResult(true);

        // Task.WaitAsync(TimeSpan) is .NET 6+; use Task.WhenAny for net48 compatibility.
        var completed = await Task.WhenAny(disposed.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.ShouldBeSameAs(disposed.Task, "warm-up did not reach the orphan-dispose path within the timeout");
        await disposed.Task;

        selfLogBuilder.ToString().ShouldNotContain(nameof(ObjectDisposedException));
    }

    // Issue #302: bounded warm-up + circuit breaker tests below.
    private static readonly FieldInfo BrokenUntilTicksField =
        typeof(RabbitMQChannelPool).GetField("_brokenUntilTicks", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("RabbitMQChannelPool._brokenUntilTicks field not found.");

    private static void ExpireCooldown(RabbitMQChannelPool pool) =>
        BrokenUntilTicksField.SetValue(pool, 0L);

    [Theory]
    [InlineData(1, 500)]
    [InlineData(2, 1000)]
    [InlineData(3, 2000)]
    [InlineData(4, 4000)]
    [InlineData(5, 8000)]
    [InlineData(6, 16000)]
    [InlineData(7, 30000)]
    [InlineData(8, 30000)]
    [InlineData(100, 30000)]
    public void GetBackoffDelay_ReturnsExpectedSchedule(int failures, int expectedMilliseconds)
    {
        RabbitMQChannelPool.GetBackoffDelay(failures)
            .ShouldBe(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Fact]
    public async Task WarmUp_StopsAfterMaxRetries_AndTransitionsToBroken()
    {
        // Arrange — every CreateChannelAsync attempt throws. With WarmUpMaxRetries=3 the
        // warm-up loop should perform 3 attempts (because _consecutiveFailures resets only
        // on success) and then transition the pool to Broken.
        int attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("permanent");
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = 3,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Broken);

        Volatile.Read(ref attempts).ShouldBe(3);
    }

    [Fact]
    public async Task GetAsync_ThrowsExhausted_WhenPoolIsBroken()
    {
        int attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("permanent");
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = 2,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Broken);

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await pool.GetAsync());
        ex.Message.ShouldContain("exhausted");
        ex.Message.ShouldContain("2");
    }

    [Fact]
    public async Task GetAsync_TriggersProbe_AfterCooldown_AndRecoversOnSuccess()
    {
        // Arrange — first 3 CreateChannelAsync calls fail (exhausting WarmUpMaxRetries=3
        // and tripping Broken), from the 4th onwards the factory succeeds. Expiring the
        // cooldown via reflection simulates the 60 s wall-clock wait without slowing the
        // test; the probe attempt then succeeds and the pool transitions back to
        // Warming → Open via the refill.
        int attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                if (attempt <= 3)
                {
                    throw new InvalidOperationException("transient-broker-outage");
                }

                return Task.FromResult(CreateOpenChannel());
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = 3,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Broken);

        // Act — expire cooldown so the next GetAsync tries to probe.
        ExpireCooldown(pool);

        // The probe succeeds, the caller falls through to ReadAsync, and the refill task
        // eventually takes state back to Open.
        var channel = await pool.GetAsync();
        channel.ShouldNotBeNull();

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Open);
    }

    [Fact]
    public async Task GetAsync_ProbeFailure_RestoresBrokenStateWithFreshCooldown()
    {
        // Arrange — factory always fails. After exhaustion and cooldown expiry, the probe
        // attempt must also fail, the pool must return to Broken with a NEW cooldown, and
        // subsequent GetAsync calls must throw exhausted without re-probing.
        int attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("permanent");
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = 2,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Broken);
        int attemptsAfterExhaustion = Volatile.Read(ref attempts);

        ExpireCooldown(pool);

        // First post-cooldown call triggers the probe. Probe fails → back to Broken.
        await Should.ThrowAsync<InvalidOperationException>(async () => await pool.GetAsync());
        pool.CurrentState.ShouldBe(RabbitMQChannelPool.PoolState.Broken);

        // Probe used exactly one attempt on top of the pre-cooldown budget.
        Volatile.Read(ref attempts).ShouldBe(attemptsAfterExhaustion + 1);

        // Second post-cooldown call finds a FRESH cooldown and throws without probing.
        await Should.ThrowAsync<InvalidOperationException>(async () => await pool.GetAsync());
        Volatile.Read(ref attempts).ShouldBe(attemptsAfterExhaustion + 1);
    }

    [Fact]
    public async Task GetAsync_ConcurrentCallsDuringProbe_AllSeeExhaustion()
    {
        // Arrange — first N calls fail to trip Broken; from N+1 onwards CreateChannelAsync
        // blocks on a gate so the probe can be observed mid-flight. We then fire 4 concurrent
        // GetAsync calls: one wins the CAS and becomes the probe, the others must all see
        // Probing and throw exhausted immediately. After the gate releases, the probe
        // succeeds and the winner's GetAsync returns a channel.
        int attempts = 0;
        var probeGate = new TaskCompletionSource<bool>();
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(async _ =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                if (attempt <= 2)
                {
                    throw new InvalidOperationException("permanent");
                }

                // Probe call: block until the test releases the gate.
                await probeGate.Task.ConfigureAwait(false);
                return CreateOpenChannel();
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = 2,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Broken);
        ExpireCooldown(pool);

        // Kick off four concurrent GetAsync calls; one becomes the probe.
        var callers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try
            {
                return await pool.GetAsync();
            }
            catch (InvalidOperationException)
            {
                return null!;
            }
        })).ToArray();

        // Wait until the probe has entered CreateChannelAsync.
        await WaitForAsync(() => Volatile.Read(ref attempts) >= 3);
        pool.CurrentState.ShouldBe(RabbitMQChannelPool.PoolState.Probing);

        // Let any concurrent callers finish their Broken/Probing checks.
        await WaitForAsync(() => callers.Count(c => c.IsCompleted) >= 3);

        // Now release the probe so the winning caller can complete.
        probeGate.SetResult(true);

        var results = await Task.WhenAll(callers);

        // Exactly one caller got a channel; the rest threw (returned null in our wrapper).
        results.Count(r => r is not null).ShouldBe(1);
        results.Count(r => r is null).ShouldBe(3);
    }

    [Fact]
    public async Task GetAsync_ProbeInFlight_WhenPoolDisposed_DisposesOrphanChannelAndSurfacesDisposal()
    {
        // Covers HandleBrokenStateAsync's "TryWrite failed" branch: probe is inside
        // CreateChannelAsync when the pool is disposed, so _channels.Writer is completed
        // by the time the probe returns. The returned channel is orphaned and must be
        // disposed; GetAsync's follow-on ReadAsync then surfaces the disposal.
        int attempts = 0;
        var probeGate = new TaskCompletionSource<bool>();
        var probeChannel = Substitute.For<IChannel>();
        probeChannel.IsOpen.Returns(true);

        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(async _ =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                if (attempt <= 2)
                {
                    throw new InvalidOperationException("permanent");
                }

                await probeGate.Task.ConfigureAwait(false);
                return probeChannel;
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = 2,
        };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Broken);
        ExpireCooldown(pool);

        var probeCall = pool.GetAsync().AsTask();

        // Wait until the probe has entered CreateChannelAsync (attempt 3).
        await WaitForAsync(() => Volatile.Read(ref attempts) >= 3);

        // Dispose the pool while the probe is blocked; the writer is completed so TryWrite
        // fails when the probe eventually returns.
        await pool.DisposeAsync();

        // Release the probe — CreateChannelAsync returns, TryWrite fails, the orphan
        // channel is disposed, HandleBrokenStateAsync returns without transitioning.
        probeGate.SetResult(true);

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await probeCall);
        ex.Message.ShouldContain("disposed");
        probeChannel.Received(1).Dispose();
    }

    [Fact]
    public async Task GetAsync_ProbeInFlight_WhenCallerTokenCancelled_RestoresBrokenAndRethrows()
    {
        // Covers the caller-cancellation path in HandleBrokenStateAsync: the probe is
        // awaiting connection.CreateChannelAsync with the caller's token. Cancelling the
        // token causes the probe to throw OperationCanceledException, which the handler
        // must route through RecordProbeFailure (resetting the cooldown) and rethrow.
        int attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(async call =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                if (attempt <= 2)
                {
                    throw new InvalidOperationException("permanent");
                }

                // Probe call: block on the caller's cancellation token so we can cancel
                // the probe deterministically.
                var ct = call.ArgAt<CancellationToken>(1);
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return CreateOpenChannel();
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = 2,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Broken);
        ExpireCooldown(pool);

        using var cts = new CancellationTokenSource();
        var probeCall = pool.GetAsync(cts.Token).AsTask();

        await WaitForAsync(() => Volatile.Read(ref attempts) >= 3);
        pool.CurrentState.ShouldBe(RabbitMQChannelPool.PoolState.Probing);

        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () => await probeCall);

        // Probe caller's OCE must also flip the pool back to Broken with a fresh cooldown,
        // so a follow-on caller sees exhaustion rather than falling through to ReadAsync.
        pool.CurrentState.ShouldBe(RabbitMQChannelPool.PoolState.Broken);
    }

    [Fact]
    public async Task HandleBrokenState_WhenCasLoses_ThrowsExhaustion()
    {
        // Covers the CAS-lost race in HandleBrokenStateAsync (line ~162): a caller reads
        // state=Broken and enters the handler, but between the read and the
        // CompareExchange(Broken → Probing) another caller wins the probe role. The loser
        // must throw the exhaustion exception without running a second probe. Simulating
        // the timing race deterministically in a multi-thread test is not reliable, so we
        // use a narrow internal test hook that pre-sets state=Probing and invokes the
        // handler with observedState=Broken — the CAS then correctly fails.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1, WarmUpMaxRetries = 2 };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Drive the pool into Probing (simulating the "other caller already won the CAS").
        pool.TestingSetState(RabbitMQChannelPool.PoolState.Probing);

        // Invoke the handler as if we had observed Broken a moment earlier — CAS loses.
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await pool.TestingInvokeHandleBrokenStateAsync(
                RabbitMQChannelPool.PoolState.Broken,
                default));
        ex.Message.ShouldContain("exhausted");

        // State must be unchanged — we were not the probe owner, so we did not clear it.
        pool.CurrentState.ShouldBe(RabbitMQChannelPool.PoolState.Probing);
    }

    [Fact]
    public async Task GetAsync_InFlightWaiter_WakesOnTransitionToBroken_AndRoutesToFallback()
    {
        // The key queue-bloat mitigation: a GetAsync parked on ReadAsync during the
        // cumulative warm-up backoff window (controlled by WarmUpMaxRetries and the
        // backoff schedule) would otherwise stay hung for the entire outage,
        // blocking BatchingSink's flush loop. When warm-up exhausts and transitions
        // to Broken, the unhealthy signal fires and the in-flight waiter wakes,
        // observes Broken, and throws the exhaustion exception so the batch
        // rethrows to BatchingSink's failure listener / Fallback chain.
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ => throw new InvalidOperationException("permanent"));
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = 2,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Park a caller on ReadAsync BEFORE the pool has had time to reach Broken.
        // Pool is Warming; pool is empty; caller blocks.
        var waiter = pool.GetAsync().AsTask();
        pool.CurrentState.ShouldNotBe(RabbitMQChannelPool.PoolState.Broken);

        // Wait for warm-up to exhaust — this fires SignalUnhealthy after flipping state.
        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Broken);

        // The hung waiter should wake with the exhaustion exception (NOT OCE), proving
        // the signal fired and the catch handler routed the cancellation to the batching
        // layer's fallback path.
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await waiter);
        ex.Message.ShouldContain("exhausted");
    }

    [Fact]
    public async Task WarmUp_WithNullMaxRetries_KeepsRetrying_WithoutTransitioningToBroken()
    {
        // `WarmUpMaxRetries = null` is the opt-in for unlimited retries (pre-9.0 behaviour);
        // covers the short-circuit arm of `_maxRetries is int maxRetries && failures >= maxRetries`
        // where the pattern match fails. Warm-up must keep retrying with the exponential
        // backoff rather than transitioning to Broken — no matter how many failures stack up.
        var attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("permanent");
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = null,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        // Wait until warm-up has retried enough times that the default MaxRetries of 10
        // would have tripped the breaker — confirms the null opt-out disables exhaustion
        // entirely. Two attempts suffice given the backoff (~500 ms of wall-clock delay
        // after the first failure).
        await WaitForAsync(() => Volatile.Read(ref attempts) >= 2);
        pool.CurrentState.ShouldBe(RabbitMQChannelPool.PoolState.Warming);
    }

    [Fact]
    public async Task RefillExhaustion_DuringProbing_DoesNotPrematurelySignalUnhealthy()
    {
        // If a refill WarmUpAsync (spawned by ReturnAsync) is still looping when
        // HandleBrokenStateAsync has flipped state to Probing, and the refill itself
        // reaches _maxRetries, it must NOT signal unhealthy — a concurrent probe is
        // mid-flight and prematurely waking waiters (who would then observe Probing
        // and throw exhausted) bypasses the probe's recovery.
        //
        // The guard in WarmUpSingleAsync only calls SignalUnhealthy when one of the two
        // Warming/Open→Broken CAS calls succeeds; if state was Probing, both fail and
        // the signal is suppressed. This test uses TestingSetState to force the Probing
        // state deterministically, then spawns a refill via ReturnAsync that will hit
        // _maxRetries, and asserts the unhealthy CTS is never cancelled.
        var attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1)
                {
                    // Initial warm-up succeeds so the pool reaches Open.
                    return Task.FromResult(CreateOpenChannel());
                }

                throw new InvalidOperationException("transient");
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = 2,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Open);

        // Simulate HandleBrokenStateAsync already owning the probe.
        pool.TestingSetState(RabbitMQChannelPool.PoolState.Probing);

        var ctsField = typeof(RabbitMQChannelPool).GetField("_unhealthySignalCts", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_unhealthySignalCts field not found.");
        var signalCts = ctsField.GetValue(pool) as CancellationTokenSource
            ?? throw new InvalidOperationException("_unhealthySignalCts value was null.");
        signalCts.IsCancellationRequested.ShouldBeFalse();

        // Return a broken channel — spawns a refill WarmUpAsync that will keep failing.
        var brokenChannel = Substitute.For<IRabbitMQChannel>();
        brokenChannel.IsOpen.Returns(false);
        await pool.ReturnAsync(brokenChannel);

        // Wait until the refill has failed _maxRetries times plus the initial success.
        await WaitForAsync(() => Volatile.Read(ref attempts) >= 1 + configuration.WarmUpMaxRetries!.Value);

        // Give the exhaustion block a beat to run its CAS pair + guarded SignalUnhealthy.
        await Task.Delay(50);

        // State must still be Probing (both CAS no-op'd) and the signal must NOT have fired.
        pool.CurrentState.ShouldBe(RabbitMQChannelPool.PoolState.Probing);
        signalCts.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task ReturnAsync_Refill_WhenPoolBroken_AbortsWithoutMoreAttempts()
    {
        // Once the pool has transitioned to Broken, refills triggered by ReturnAsync must
        // not keep hammering the broker — WarmUpSingleAsync's entry-check short-circuits
        // so the circuit-breaker cooldown actually holds. Without the check, a cluster of
        // simultaneously-broken channels would each spawn a refill task that ignored the
        // breaker and piled up SelfLog spam.
        int attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("permanent");
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 1,
            WarmUpMaxRetries = 2,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Broken);
        int attemptsAfterExhaustion = Volatile.Read(ref attempts);

        // Simulate a broken channel return while the pool is Broken.
        var brokenChannel = Substitute.For<IRabbitMQChannel>();
        brokenChannel.IsOpen.Returns(false);
        await pool.ReturnAsync(brokenChannel);

        // Give the refill task time to have run if it were going to.
        await Task.Delay(100);

        Volatile.Read(ref attempts).ShouldBe(attemptsAfterExhaustion);
        pool.CurrentState.ShouldBe(RabbitMQChannelPool.PoolState.Broken);
    }

    [Fact]
    public async Task SignalUnhealthy_SwallowsObjectDisposedException_WhenCtsAlreadyDisposed()
    {
        // Covers SignalUnhealthy's ObjectDisposedException catch. When DisposeAsync
        // races with a warm-up exhaustion that is about to call SignalUnhealthy, the
        // CTS has already been disposed and Cancel() throws ODE. The catch swallows
        // so the ODE doesn't propagate into WarmUpSingleAsync's broad handler and
        // light up SelfLog.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };
        var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await pool.DisposeAsync(); // disposes _unhealthySignalCts among other things

        // Reach into the (now-disposed) pool and invoke SignalUnhealthy directly.
        // Production reaches this via the exhaustion → signal path; reflection is
        // how we deterministically hit the ODE branch without a multi-thread race.
        var method = typeof(RabbitMQChannelPool).GetMethod("SignalUnhealthy", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SignalUnhealthy not found.");

        Should.NotThrow(() => method.Invoke(pool, null));
    }

    [Fact]
    public async Task GetAsync_WhenChannelsWriterCompletedWithoutDisposedFlag_ThrowsInvalidOperation()
    {
        // Covers the ChannelClosedException catch path in GetAsync. The normal
        // disposal path short-circuits via the fast check at the top, so this
        // catch only fires in the narrow window where DisposeAsync is mid-flight
        // and has completed the writer without yet flipping the disposed flag.
        // Reflection reproduces that window by completing the writer in place.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };
        var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Open);

        // Drain the pool so GetAsync would normally park on ReadAsync.
        await pool.GetAsync();

        // Complete the writer in place, without setting _disposed. GetAsync's fast
        // path sees _disposed == 0 and falls through to ReadAsync, which throws
        // ChannelClosedException on the completed writer.
        var channelsField = typeof(RabbitMQChannelPool).GetField("_channels", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_channels field not found.");
        var channels = channelsField.GetValue(pool) as Channel<IRabbitMQChannel>
            ?? throw new InvalidOperationException("_channels value was null.");
        channels.Writer.TryComplete();

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await pool.GetAsync());
        ex.Message.ShouldContain("disposed");

        // Pool field state is intentionally inconsistent post-test — no DisposeAsync
        // to avoid spurious errors; SemaphoreSlim leak is acceptable in a test.
    }

    [Fact]
    public async Task GetAsync_WhenDisposedFlagSetDuringReadAsync_MapsCancellationToDisposal()
    {
        // Covers the OperationCanceledException catch whose when-filter reads the
        // disposed flag. Like the writer-completed case above, this only fires in
        // the narrow DisposeAsync race where the flag flips after the fast-path
        // check but before the catch re-reads it. Reflection reproduces the exact
        // state without needing a multi-thread race.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };
        var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Open);
        await pool.GetAsync(); // drain

        using var cts = new CancellationTokenSource();
        var parked = pool.GetAsync(cts.Token).AsTask();
        await Task.Delay(50); // let parked call reach ReadAsync

        // Flip _disposed without running the real DisposeAsync. The when-filter in
        // GetAsync's catch re-reads this field when the OCE fires, and must see it
        // set.
        var disposedField = typeof(RabbitMQChannelPool).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_disposed field not found.");
        disposedField.SetValue(pool, 1);

        cts.Cancel();

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await parked);
        ex.Message.ShouldContain("disposed");

        // Skip real DisposeAsync — _disposed is already set, Interlocked.Exchange
        // would return non-zero and no-op. Test intentionally leaks the semaphore.
    }

    [Fact]
    public async Task GetAsync_WhenUnhealthySignalFiresButStateNotBroken_RethrowsOperationCanceled()
    {
        // Covers the `throw;` fallthrough in GetAsync's unhealthy-signal catch.
        // SignalUnhealthy is only called from state-transition paths in production,
        // so reaching this code requires _unhealthySignalCts to fire without a
        // corresponding state transition — an atypical case used here to prove the
        // catch doesn't mask a genuine cancellation as exhaustion.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };
        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Open);
        await pool.GetAsync(); // drain so GetAsync parks on ReadAsync

        var parked = pool.GetAsync().AsTask();
        await Task.Delay(50);

        // Cancel the unhealthy CTS WITHOUT transitioning state.
        var ctsField = typeof(RabbitMQChannelPool).GetField("_unhealthySignalCts", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_unhealthySignalCts field not found.");
        var cts = ctsField.GetValue(pool) as CancellationTokenSource
            ?? throw new InvalidOperationException("_unhealthySignalCts value was null.");
        cts.Cancel();

        // Catch fires (linked cancelled, caller token not), state is Open, falls
        // through to `throw;`. Caller sees OCE, not an exhaustion exception.
        await Should.ThrowAsync<OperationCanceledException>(async () => await parked);
        pool.CurrentState.ShouldBe(RabbitMQChannelPool.PoolState.Open);
    }

    [Fact]
    public async Task GetAsync_WithPreCancelledToken_AfterDispose_ThrowsInvalidOperationException()
    {
        // Item 2 from #286: shutdown-cancellation must win deterministically over the
        // caller's own token cancellation. Without the OperationCanceledException-to-
        // InvalidOperationException mapping in GetAsync, a pre-cancelled user token
        // would cause ReadAsync to throw OCE before the ChannelClosedException surfaces,
        // and callers would see a cancellation exception instead of the disposal signal.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };

        var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await pool.DisposeAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await pool.GetAsync(cts.Token));
        ex.Message.ShouldContain("disposed");
    }

    [Fact]
    public async Task GetAsync_WhenUnhealthySignalCtsRotatedAndDisposed_DoesNotLeakObjectDisposedException()
    {
        // The probe-success path in HandleBrokenStateAsync rotates _unhealthySignalCts
        // and disposes the previous instance:
        //   Interlocked.Exchange(ref _unhealthySignalCts, new CTS()).Dispose();
        // A concurrent GetAsync that already loaded the old field reference will then
        // call .Token on a disposed CTS, throwing ObjectDisposedException — which
        // bypasses the structured catch blocks in GetAsync and surfaces to the publish
        // path as an unstructured failure.
        //
        // Deterministically reproducing the inter-instruction race needs two threads;
        // disposing the current CTS in place via reflection produces the exact same
        // observable state (a disposed CTS still referenced by the field). Once fixed
        // (snapshot-then-guard, or Volatile.Read + try/catch on Token), GetAsync
        // should map this to the structured exhaustion / disposal path rather than
        // leaking ODE.
        var connection = BuildConnectionWithChannelFactory(CreateOpenChannel);
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 1 };
        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Open);
        await pool.GetAsync(); // drain so a follow-on GetAsync would park on ReadAsync

        var ctsField = typeof(RabbitMQChannelPool).GetField("_unhealthySignalCts", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_unhealthySignalCts field not found.");
        var cts = ctsField.GetValue(pool) as CancellationTokenSource
            ?? throw new InvalidOperationException("_unhealthySignalCts value was null.");
        cts.Dispose();

        // After the fix, this should map to a structured failure type
        // (InvalidOperationException for "disposed" or "exhausted") or succeed —
        // but never leak ObjectDisposedException to the caller.
        ObjectDisposedException? leaked = null;
        try
        {
            using var callerCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await pool.GetAsync(callerCts.Token);
        }
        catch (ObjectDisposedException ode)
        {
            leaked = ode;
        }
        catch
        {
            // Any other exception type is acceptable — we only care that ODE
            // doesn't surface unstructured to publish-path callers.
        }

        leaked.ShouldBeNull();
    }

    [Fact]
    public async Task ProbeRecovery_WithChannelCountGreaterThanOne_RefillReachesOpenState()
    {
        // Existing recovery test uses ChannelCount=1, which masks the bug. With
        // ChannelCount=2 the probe writes 1 channel into the bounded queue, then refill
        // spawns WarmUpAsync(_size=2): write #1 succeeds (pool full at 2/2), write #2's
        // TryWrite returns false, the orphan is disposed, the cohort is treated as
        // failed, and markOpenOnCompletion never CAS'es Warming → Open.
        //
        // We invoke HandleBrokenStateAsync via the test hook (instead of going through
        // GetAsync) so the probe channel stays in the pool while refill runs — in the
        // GetAsync path, ReadAsync drains the probe channel synchronously before the
        // spawned refill task starts, hiding the bug behind incidental ordering.
        //
        // After the fix (refill should request `_size - 1`, or warm-up should fill
        // until full), state must reach Open.
        int attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(CreateOpenChannel());
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 2,
            WarmUpMaxRetries = 2,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(() => pool.CurrentState == RabbitMQChannelPool.PoolState.Open);

        // Drain the pool so the probe writes into an empty bounded queue — this
        // mirrors the production post-outage state where the pool is exhausted
        // before the breaker trips. Without draining, the probe's TryWrite fails
        // on a full queue and the success path is never reached.
        await pool.GetAsync();
        await pool.GetAsync();

        // Force the pool into Broken with cooldown elapsed, mirroring the post-outage
        // state that HandleBrokenStateAsync's success path acts on.
        pool.TestingSetState(RabbitMQChannelPool.PoolState.Broken);
        ExpireCooldown(pool);

        // Run the probe directly. It writes 1 channel and spawns refill in the
        // background; we don't drain afterwards, so refill faces a pool that
        // already has 1/_size occupied — the exact race the bug exposes.
        await pool.TestingInvokeHandleBrokenStateAsync(RabbitMQChannelPool.PoolState.Broken, default);

        // With the bug present this WaitForAsync times out: refill orphans the (_size)th
        // channel, returns false from WarmUpSingleAsync, and exits before reaching the
        // Warming → Open CAS. State stays Warming.
        await WaitForAsync(
            () => pool.CurrentState == RabbitMQChannelPool.PoolState.Open,
            TimeSpan.FromSeconds(3));
        pool.CurrentState.ShouldBe(RabbitMQChannelPool.PoolState.Open);
    }

    [Fact(Skip = "P0 #3: _consecutiveFailures is reset on every individual successful channel add, so a flaky broker that lets one channel through per cohort never trips the breaker even past WarmUpMaxRetries cumulative failures. Confirmed failing — breaker never trips. Whether to fix vs. document is a design call.")]
    public async Task WarmUp_WithFlakyBrokerOneSuccessPerCohort_TripsBrokenAfterMaxRetriesCumulativeFailures()
    {
        // Pattern: every odd attempt fails, every even attempt succeeds. With
        // ChannelCount=4 and WarmUpMaxRetries=3, cohorts run:
        //   cohort 0: fail, success (failures=1, reset to 0)
        //   cohort 1: fail, success (failures=1, reset to 0)
        //   cohort 2: fail, success (failures=1, reset to 0)
        //   cohort 3: fail, success (failures=1, reset to 0)
        // Total: 4 failures, none "consecutive" by the current definition — pool
        // reaches Open. A user who set WarmUpMaxRetries=3 reasonably expects the
        // breaker to trip after 3 cumulative warm-up failures.
        //
        // After the fix (track failures per cohort or only reset on full warm-up
        // completion), state should transition to Broken before reaching Open.
        int attempts = 0;
        var connection = Substitute.For<IConnection>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<IChannel>>(_ =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                if (attempt % 2 == 1)
                {
                    throw new InvalidOperationException("flaky-broker");
                }

                return Task.FromResult(CreateOpenChannel());
            });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration
        {
            ChannelCount = 4,
            WarmUpMaxRetries = 3,
        };

        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);

        await WaitForAsync(
            () => pool.CurrentState == RabbitMQChannelPool.PoolState.Broken,
            TimeSpan.FromSeconds(5));
    }
}
