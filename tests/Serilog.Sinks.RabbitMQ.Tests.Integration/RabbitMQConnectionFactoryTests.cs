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

using System.Net.Security;
using System.Security.Authentication;

namespace Serilog.Sinks.RabbitMQ.Tests.Integration;

public class RabbitMQConnectionFactoryTests
{
    [Fact]
    public async Task GetConnection_ShouldReturnOpenConnection()
    {
        var sut = new RabbitMQConnectionFactory(RabbitMQFixture.GetRabbitMQClientConfiguration(), new CancellationTokenSource());

        (await sut.GetConnectionAsync()).IsOpen.ShouldBeTrue();

        await sut.CloseAsync();

        sut.Dispose();
    }

    [Fact]
    public async Task GetConnection_ShouldReturnNull_WhenBrokerCannotBeReached()
    {
        var rabbitMQClientConfiguration = RabbitMQFixture.GetRabbitMQClientConfiguration();
        rabbitMQClientConfiguration.Port = 5673;

        using var sut = new RabbitMQConnectionFactory(rabbitMQClientConfiguration, new CancellationTokenSource());

        var act = () => sut.GetConnectionAsync();
        (await Should.ThrowAsync<BrokerUnreachableException>(act)).GetType().ShouldBe(typeof(BrokerUnreachableException));
    }

    [Fact]
    public async Task GetConnection_ShouldReturnOpenConnection_WhenConfiguredForSslCert()
    {
        // arrange
        var sut = new RabbitMQConnectionFactory(RabbitMQFixture.GetRabbitMQSslClientConfiguration(), new CancellationTokenSource());

        // act
        var connection = await sut.GetConnectionAsync();

        // assert
        connection.IsOpen.ShouldBeTrue();

        await sut.CloseAsync();

        sut.Dispose();
    }

    [Fact]
    public async Task GetConnection_ShouldReturnOpenConnection_WhenConfiguredForSslPlain()
    {
        // arrange
        var configuration = new RabbitMQClientConfiguration
        {
            Port = 6671,
            DeliveryMode = RabbitMQDeliveryMode.Durable,
            Exchange = RabbitMQFixture.SerilogSinkExchange,
            Username = RabbitMQFixture.UserName,
            Password = RabbitMQFixture.Password,
            ExchangeType = RabbitMQFixture.SerilogSinkExchangeType,
            Hostnames = [RabbitMQFixture.SslPlainHostName],
            SslOption = new SslOption
            {
                Enabled = true,
                ServerName = RabbitMQFixture.SslCertHostName,
                AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch |
                                         SslPolicyErrors.RemoteCertificateChainErrors,
                Version = SslProtocols.Tls13,
            },
        };

        var sut = new RabbitMQConnectionFactory(configuration, new CancellationTokenSource());

        // act
        var connection = await sut.GetConnectionAsync();

        // assert
        connection.IsOpen.ShouldBeTrue();

        await sut.CloseAsync();
        sut.Dispose();
    }
}
