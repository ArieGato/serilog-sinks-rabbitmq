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
}
