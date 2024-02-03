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
using Serilog.Events;

namespace Serilog.Sinks.RabbitMQ.Tests.Integration
{
    [Collection("Sequential")]
    public class ConfigurationExtensionsFixture : IClassFixture<RabbitMQFixture>
    {
        [Fact]
        public void WriteWithUri()
        {
            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.WriteTo.RabbitMQ(
                    amqpUri: RabbitMQFixture.AmqpUri,
                    exchange: RabbitMQFixture.SerilogSinkExchange,
                    exchangeType: RabbitMQFixture.SerilogSinkExchangeType,
                    deliveryMode: RabbitMQDeliveryMode.Durable)
                .CreateLogger();

            // should not throw
            logger.Dispose();
        }

        [Fact]
        public async Task WriteWithUriAutoCreate()
        {
            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.WriteTo.RabbitMQ(
                    amqpUri: RabbitMQFixture.AmqpUri,
                    exchange: RabbitMQFixture.SerilogSinkExchange,
                    exchangeType: RabbitMQFixture.SerilogSinkExchangeType,
                    deliveryMode: RabbitMQDeliveryMode.Durable,
                    autoCreateExchange: true)
                .CreateLogger();

            // Actually log something to trigger the exchange creation
            logger.Information("Some text");

            await Task.Delay(1000); // wait batch execution

            // should not throw
            logger.Dispose();
        }

        [Fact]
        public void WriteWithHostUserPwd()
        {
            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.WriteTo.RabbitMQ(
                    hostname: RabbitMQFixture.HostName,
                    username: RabbitMQFixture.UserName,
                    password: RabbitMQFixture.Password,
                    exchange: RabbitMQFixture.SerilogSinkExchange,
                    exchangeType: RabbitMQFixture.SerilogSinkExchangeType,
                    deliveryMode: RabbitMQDeliveryMode.Durable)
                .CreateLogger();

            // should not throw
            logger.Dispose();
        }

        [Fact]
        public void AuditWithUri()
        {
            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.AuditTo.RabbitMQ(
                    amqpUri: RabbitMQFixture.AmqpUri,
                    exchange: RabbitMQFixture.SerilogAuditSinkExchange,
                    exchangeType: RabbitMQFixture.SerilogAuditSinkExchangeType,
                    deliveryMode: RabbitMQDeliveryMode.Durable)
                .CreateLogger();

            // should not throw
            logger.Dispose();
        }

        [Fact]
        public void AuditWithHostUserPwd()
        {
            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.AuditTo.RabbitMQ(
                    hostname: RabbitMQFixture.HostName,
                    username: RabbitMQFixture.UserName,
                    password: RabbitMQFixture.Password,
                    exchange: RabbitMQFixture.SerilogAuditSinkExchange,
                    exchangeType: RabbitMQFixture.SerilogAuditSinkExchangeType,
                    deliveryMode: RabbitMQDeliveryMode.Durable)
                .CreateLogger();

            // should not throw
            logger.Dispose();
        }

        [Fact]
        public async Task WriteTo_ShouldNotThrow()
        {
            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.WriteTo.RabbitMQ(
                (rabbitMQClientConfiguration, rabbitMQSinkConfiguration) =>
                {
                    rabbitMQClientConfiguration.Port = 5672;
                    rabbitMQClientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
                    rabbitMQClientConfiguration.Exchange = RabbitMQFixture.SerilogSinkExchange;
                    rabbitMQClientConfiguration.Username = RabbitMQFixture.UserName;
                    rabbitMQClientConfiguration.Password = RabbitMQFixture.Password;
                    rabbitMQClientConfiguration.ExchangeType = RabbitMQFixture.SerilogSinkExchangeType;
                    rabbitMQClientConfiguration.Hostnames.Add(RabbitMQFixture.HostName);
                    rabbitMQClientConfiguration.MaxChannels = 32;
                    rabbitMQClientConfiguration.AutoCreateExchange = true;
                    rabbitMQClientConfiguration.Heartbeat = 21;
                    rabbitMQClientConfiguration.SslOption = new SslOption
                    {
                        AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch,
                        Enabled = false,
                        ServerName = "localhost",
                        CertPassphrase = "secret",
                        CertPath = "path",
                        Version = SslProtocols.Tls12,
                        CheckCertificateRevocation = true
                    };
                    rabbitMQClientConfiguration.RouteKey = "";
                    rabbitMQClientConfiguration.VHost = "/";
                    rabbitMQClientConfiguration.AmqpUri = new Uri(RabbitMQFixture.AmqpUri);

                    rabbitMQSinkConfiguration.TextFormatter = new JsonFormatter();
                    rabbitMQSinkConfiguration.BatchPostingLimit = 50;
                    rabbitMQSinkConfiguration.Period = TimeSpan.FromSeconds(5);
                    rabbitMQSinkConfiguration.RestrictedToMinimumLevel = LogEventLevel.Information;
                })
                .CreateLogger();

            // Actually log something to trigger the exchange creation
            logger.Information("Some text");

            await Task.Delay(1000); // wait batch execution

            // should not throw
            logger.Dispose();
        }
    }
}