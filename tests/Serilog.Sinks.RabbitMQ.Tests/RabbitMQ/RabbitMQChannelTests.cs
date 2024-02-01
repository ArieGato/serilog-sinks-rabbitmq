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

using RabbitMQ.Client;

namespace Serilog.Sinks.RabbitMQ.Tests.RabbitMQ
{
    public class RabbitMQChannelTests
    {
        [Fact]
        public void Dispose_ShouldNotThrowException_WhenIModelCloseThrowsException()
        {
            // Arrange
            var model = Substitute.For<IModel>();
            model.When(x => x.Close())
                .Do(_ => throw new Exception("some-message"));

            var sut = new RabbitMQChannel(model);

            // Act
            sut.Dispose();

            // Assert
            model.Received(1).Close();
            model.Received(1).Dispose();
        }

        [Fact]
        public void IsOpen_ShouldReturnTrue_WhenModelIsOpen()
        {
            // Arrange
            var model = Substitute.For<IModel>();
            model.IsOpen.Returns(true);

            var sut = new RabbitMQChannel(model);

            // Act
            var isOpen = sut.IsOpen;

            // Assert
            isOpen.Should().BeTrue();
        }

        [Fact]
        public void BasicPublish_ShouldCallModelBasicPublish_WithCorrectParameters()
        {
            // Arrange
            var model = Substitute.For<IModel>();
            var basicProperties = Substitute.For<IBasicProperties>();
            model.CreateBasicProperties().Returns(basicProperties);

            var address = new PublicationAddress("exchangeType", "exchangeName", "routingKey");
            var body = new ReadOnlyMemory<byte>([1, 2, 3]);

            var sut = new RabbitMQChannel(model);

            // Act
            sut.BasicPublish(address, body);

            // Assert
            model.Received(1).BasicPublish(address, Arg.Is(basicProperties), body);
        }
    }
}