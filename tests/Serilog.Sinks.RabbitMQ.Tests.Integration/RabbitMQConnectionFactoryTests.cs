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

namespace Serilog.Sinks.RabbitMQ.Tests.Integration
{
    public class RabbitMQConnectionFactoryTests
    {
        [Fact]
        public void GetConnection_ShouldReturnOpenConnection()
        {
            var sut = new RabbitMQConnectionFactory(RabbitMQFixture.GetRabbitMQClientConfiguration(),
                new CancellationTokenSource());

            sut.GetConnection().IsOpen.Should().BeTrue();

            sut.Close();

            sut.Dispose();
        }

        [Fact]
        public void GetConnection_ShouldReturnNull_WhenBrokerCannotBeReached()
        {
            var rabbitMQClientConfiguration = RabbitMQFixture.GetRabbitMQClientConfiguration();
            rabbitMQClientConfiguration.Port = 5673;

            using var sut = new RabbitMQConnectionFactory(rabbitMQClientConfiguration, new CancellationTokenSource());

            var act = () => sut.GetConnection();
            act.Should().ThrowExactly<BrokerUnreachableException>();
        }
    }
}