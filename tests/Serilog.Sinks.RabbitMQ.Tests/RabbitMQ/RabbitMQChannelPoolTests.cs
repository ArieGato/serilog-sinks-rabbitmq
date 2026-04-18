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
