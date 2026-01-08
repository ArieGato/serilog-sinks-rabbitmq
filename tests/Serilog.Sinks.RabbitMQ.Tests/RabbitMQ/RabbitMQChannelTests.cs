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

public class RabbitMQChannelTests
{
    [Fact]
    public async Task Dispose_ShouldNotThrowException_WhenIChannelCloseThrowsException()
    {
        // Arrange
        var channel = Substitute.For<IChannel>();
        channel.When(x => x.CloseAsync())
            .Do(_ => throw new Exception("some-message"));

        var sut = new RabbitMQChannel(channel);

        // Act
        sut.Dispose();

        // Assert
        await channel.Received(1).CloseAsync();
        channel.Received(1).Dispose();
    }

    [Fact]
    public void IsOpen_ShouldReturnTrue_WhenChannelIsOpen()
    {
        // Arrange
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);

        var sut = new RabbitMQChannel(channel);

        // Act
        bool isOpen = sut.IsOpen;

        // Assert
        isOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task BasicPublish_ShouldCallChannelBasicPublish_WithCorrectParameters()
    {
        // Arrange
        var channel = Substitute.For<IChannel>();
        var basicProperties = new BasicProperties();

        var address = new PublicationAddress("exchangeType", "exchangeName", "routingKey");
        var body = new ReadOnlyMemory<byte>([1, 2, 3]);

        var sut = new RabbitMQChannel(channel);

        // Act
        await sut.BasicPublishAsync(address, new BasicProperties(), body);

        // Assert
        var actual = Arg.Is<BasicProperties>(p => p.AppId == basicProperties.AppId);
        await channel.Received(1).BasicPublishAsync(address, actual, body);
    }
}
