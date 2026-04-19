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

namespace Serilog.Sinks.RabbitMQ.Tests.RabbitMQ;

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
    public async Task ReturnAsync_WhenBrokenChannelDisposeThrows_StillRefillsPool()
    {
        // Arrange — the broken channel's DisposeAsync throws. The fire-and-forget
        // replacement task must catch the exception (so it is not unobserved) and
        // still run WarmUpAsync to refill the pool. Without the catch, the await
        // would short-circuit the lambda and WarmUpAsync would never execute —
        // WaitForAsync(created == 2) below would then hit its 2s timeout.
        //
        // The catch also logs to SelfLog, but asserting on SelfLog here is flaky:
        // SelfLog is a global static and parallel test classes race for the writer.
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

        // Assert — the pool refills even though the broken channel's dispose threw.
        await WaitForAsync(() => Volatile.Read(ref created) == 2);
        await brokenChannel.Received(1).DisposeAsync();
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

        // Give the second warmup time to enter the declare branch if the race fires.
        await Task.Delay(150);

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
        await WaitForAsync(() => connection.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == nameof(IConnection.CreateChannelAsync)));

        // Queue is full (capacity 1, warmed with 1 channel). Returning another open
        // channel cannot be re-pooled — TryWrite fails and we take the dispose path.
        var surplusChannel = Substitute.For<IRabbitMQChannel>();
        surplusChannel.IsOpen.Returns(true);

        await pool.ReturnAsync(surplusChannel);

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
    public async Task Constructor_DefaultsToSixtyFourChannels_WhenChannelCountIsZero()
    {
        // Arrange
        int created = 0;
        var connection = BuildConnectionWithChannelFactory(() =>
        {
            Interlocked.Increment(ref created);
            return CreateOpenChannel();
        });
        var connectionFactory = BuildConnectionFactory(connection);
        var configuration = new RabbitMQClientConfiguration { ChannelCount = 0 };

        // Act
        await using var pool = new RabbitMQChannelPool(configuration, connectionFactory);
        await WaitForAsync(() => Volatile.Read(ref created) == 64);

        // Assert
        Volatile.Read(ref created).ShouldBe(64);
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
}
