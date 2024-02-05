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

using Microsoft.Extensions.ObjectPool;

namespace Serilog.Sinks.RabbitMQ.Tests.RabbitMQ;

public class RabbitMQClientTests
{
    [Fact]
    public void Publish_ShouldCreateAndReturnChannelToPool()
    {
        // Arrange
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration()
        {
            Exchange = "some-exchange",
            ExchangeType = "some-exchange-type",
            RouteKey = "some-route-key"
        };
        var rabbitMQConnectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        var rabbitMQChannelObjectPoolPolicy = Substitute.For<IPooledObjectPolicy<IRabbitMQChannel>>();

        var rabbitMQChannel = Substitute.For<IRabbitMQChannel>();
        rabbitMQChannelObjectPoolPolicy.Create().Returns(rabbitMQChannel);
        rabbitMQChannelObjectPoolPolicy.Return(Arg.Is(rabbitMQChannel)).Returns(true);

        var sut = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, rabbitMQChannelObjectPoolPolicy);

        // Act
        sut.Publish("some-message");

        // Assert
        rabbitMQChannelObjectPoolPolicy.Received(1).Create();
        rabbitMQChannelObjectPoolPolicy.Received(1).Return(Arg.Is(rabbitMQChannel));

        rabbitMQChannel.Received(1).BasicPublish(Arg.Any<PublicationAddress>(), Arg.Any<ReadOnlyMemory<byte>>());
    }

    [Fact]
    public void Close_ShouldCreateAndReturnChannelToPool()
    {
        // Arrange
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration()
        {
            Exchange = "some-exchange",
            ExchangeType = "some-exchange-type",
            RouteKey = "some-route-key"
        };
        var rabbitMQConnectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        var rabbitMQChannelObjectPoolPolicy = Substitute.For<IPooledObjectPolicy<IRabbitMQChannel>>();

        var sut = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, rabbitMQChannelObjectPoolPolicy);

        // Act
        sut.Close();

        // Assert
        rabbitMQConnectionFactory.Received(1).Close();
    }

    [Fact]
    public void Close_ShouldThrowAggregateException_WhenExceptionsOccur()
    {
        // Arrange
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration()
        {
            Exchange = "some-exchange",
            ExchangeType = "some-exchange-type",
            RouteKey = "some-route-key"
        };
        var rabbitMQConnectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        rabbitMQConnectionFactory.When(x => x.Close()).Do(_ => throw new InvalidOperationException("some-exception"));

        var rabbitMQChannelObjectPoolPolicy = Substitute.For<IPooledObjectPolicy<IRabbitMQChannel>>();

        var sut = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, rabbitMQChannelObjectPoolPolicy);

        // Need to dispose the client, so the close will throw two exceptions
        sut.Dispose();

        // Act
        var act = () => sut.Close();

        // Assert
        act.Should().Throw<AggregateException>()
            .WithMessage($"Exceptions occurred while closing {nameof(RabbitMQClient)}*")
            .And.InnerExceptions.Should().HaveCount(2);
    }

    [Fact]
    public void Dispose_ShouldDisposeConnectionAndChannel()
    {
        // Arrange
        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration()
        {
            Exchange = "some-exchange",
            ExchangeType = "some-exchange-type",
            RouteKey = "some-route-key"
        };
        var rabbitMQConnectionFactory = Substitute.For<IRabbitMQConnectionFactory>();
        var rabbitMQChannelObjectPoolPolicy = Substitute.For<IPooledObjectPolicy<IRabbitMQChannel>>();

        var rabbitMQChannel = Substitute.For<IRabbitMQChannel>();
        rabbitMQChannelObjectPoolPolicy.Create().Returns(rabbitMQChannel);
        rabbitMQChannelObjectPoolPolicy.Return(Arg.Is(rabbitMQChannel)).Returns(true);

        var sut = new RabbitMQClient(rabbitMQClientConfiguration, rabbitMQConnectionFactory, rabbitMQChannelObjectPoolPolicy);
        
        // Need to publish a message first to create the channel in the Pool
        sut.Publish("some-message");

        // Act
        sut.Dispose();

        // Assert
        rabbitMQChannel.Received(1).Dispose();
        rabbitMQConnectionFactory.Received(1).Dispose();
    }
}