// Copyright 2015-2022 Serilog Contributors
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

namespace Serilog.Sinks.RabbitMQ.Tests.RabbitMQ;

public class RabbitMQClientTests
{
    [Fact]
    public async Task Publish_ShouldGetAndReturnChannelToPool()
    {
        // Arrange
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration()
        {
            Exchange = "some-exchange",
            ExchangeType = "some-exchange-type",
            RoutingKey = "some-route-key",
        };
        var rabbitMQConnectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        var channelPool = Substitute.For<IRabbitMQChannelPool>();

        var rabbitMQChannel = Substitute.For<IRabbitMQChannel>();
        channelPool.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<IRabbitMQChannel>(rabbitMQChannel));

        var sut = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, channelPool);

        // Act
        await sut.PublishAsync(Encoding.UTF8.GetBytes("some-message"), new BasicProperties());

        // Assert
        await channelPool.Received(1).GetAsync(Arg.Any<CancellationToken>());
        await channelPool.Received(1).ReturnAsync(Arg.Is(rabbitMQChannel));

        await rabbitMQChannel.Received(1).BasicPublishAsync(Arg.Any<PublicationAddress>(), Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>());
    }

    [Fact]
    public async Task PublishAsync_WhenGetAsyncThrows_DoesNotReturnChannel()
    {
        // Arrange — GetAsync throws before the local `channel` is assigned, so the
        // `finally` block must take the `channel == null` path and skip ReturnAsync.
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration()
        {
            Exchange = "some-exchange",
            ExchangeType = "some-exchange-type",
            RoutingKey = "some-route-key",
        };
        var rabbitMQConnectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        var channelPool = Substitute.For<IRabbitMQChannelPool>();
        channelPool.GetAsync(Arg.Any<CancellationToken>())
            .Returns<ValueTask<IRabbitMQChannel>>(_ =>
                new ValueTask<IRabbitMQChannel>(Task.FromException<IRabbitMQChannel>(new InvalidOperationException("no channels"))));

        var sut = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, channelPool);

        // Act + Assert
        await Should.ThrowAsync<InvalidOperationException>(() =>
            sut.PublishAsync(Encoding.UTF8.GetBytes("some-message"), new BasicProperties()));
        await channelPool.DidNotReceive().ReturnAsync(Arg.Any<IRabbitMQChannel>());
    }

    [Fact]
    public async Task CloseAsync_ShouldCloseConnection()
    {
        // Arrange
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration()
        {
            Exchange = "some-exchange",
            ExchangeType = "some-exchange-type",
            RoutingKey = "some-route-key",
        };
        var rabbitMQConnectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        var channelPool = Substitute.For<IRabbitMQChannelPool>();

        var sut = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, channelPool);

        // Act
        await sut.CloseAsync();

        // Assert
        await rabbitMQConnectionFactory.Received(1).CloseAsync();
    }

    [Fact]
    public async Task CloseAsync_ShouldThrowAggregateException_WhenExceptionsOccur()
    {
        // Arrange — after DisposeAsync, the internal cancellation-token source is disposed,
        // so a subsequent CloseAsync call observes exceptions from both Cancel() and the
        // connection factory's CloseAsync, which should be surfaced as an AggregateException.
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration()
        {
            Exchange = "some-exchange",
            ExchangeType = "some-exchange-type",
            RoutingKey = "some-route-key",
        };
        var rabbitMQConnectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        rabbitMQConnectionFactory.When(x => x.CloseAsync()).Do(_ => throw new InvalidOperationException("some-exception"));

        var channelPool = Substitute.For<IRabbitMQChannelPool>();

        var sut = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, channelPool);

        await sut.DisposeAsync();

        // Act
        var act = async () => await sut.CloseAsync();

        // Assert
        var ex = await Should.ThrowAsync<AggregateException>(act);
        ex.Message.ShouldStartWith($"Exceptions occurred while closing {nameof(RabbitMQClient)}");
        ex.InnerExceptions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task DisposeAsync_ShouldDisposePoolAndConnectionFactory()
    {
        // Arrange
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration()
        {
            Exchange = "some-exchange",
            ExchangeType = "some-exchange-type",
            RoutingKey = "some-route-key",
        };
        var rabbitMQConnectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        var channelPool = Substitute.For<IRabbitMQChannelPool>();

        var sut = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, channelPool);

        // Act
        await sut.DisposeAsync();

        // Assert
        await channelPool.Received(1).DisposeAsync();
        rabbitMQConnectionFactory.Received(1).Dispose();
    }
}
