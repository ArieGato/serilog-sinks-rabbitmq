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
    [Collection("Sequential")]
    public class ConfigurationExtensionsFixture : IClassFixture<RabbitMQFixture>
    {
        private readonly RabbitMQFixture _rabbitMQFixture;

        public ConfigurationExtensionsFixture(RabbitMQFixture rabbitMQFixture)
        {
            _rabbitMQFixture = rabbitMQFixture;
        }

#if NET_FX
        [Fact]
        public void WriteWithUriByName()
        {
            const string nameOrUri = "NamedConnection";

            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.WriteTo.RabbitMQ(
                    amqpUri: nameOrUri,
                    exchange: RabbitMQFixture.SerilogSinkExchange,
                    exchangeType: RabbitMQFixture.SerilogSinkExchangeType,
                    deliveryMode: RabbitMQDeliveryMode.Durable)
                .CreateLogger();

            // should not throw
            logger.Dispose();
        }

        [Fact]
        public void WriteAppSettingsWithReference()
        {
            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.ReadFrom.AppSettings(settingPrefix: "R")
                .CreateLogger();

            // should not throw
            logger.Dispose();
        }

        [Fact]
        public async Task WriteAppSettings()
        {
            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.ReadFrom.AppSettings(settingPrefix: "W")
                .CreateLogger();

            var cleanupModel = await _rabbitMQFixture.GetConsumingModelAsync();
            cleanupModel.ExchangeDelete("serilog-settings-sink-exchange");
            cleanupModel.Dispose();

            // should not throw
            logger.Dispose();
        }

        [Fact]
        public async Task WriteAppSettingsMultipleHosts()
        {
            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.ReadFrom.AppSettings(settingPrefix: "H")
                .CreateLogger();

            var cleanupModel = await _rabbitMQFixture.GetConsumingModelAsync();
            cleanupModel.ExchangeDelete("serilog-settings-sink-exchange");
            cleanupModel.Dispose();

            // should not throw
            logger.Dispose();
        }

        [Fact]
        public void AuditWithUriByName()
        {
            const string nameOrUri = "NamedConnection";

            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.AuditTo.RabbitMQ(
                    amqpUri: nameOrUri,
                    exchange: RabbitMQFixture.SerilogSinkExchange,
                    exchangeType: RabbitMQFixture.SerilogSinkExchangeType,
                    deliveryMode: RabbitMQDeliveryMode.Durable)
                .CreateLogger();

            // should not throw
            logger.Dispose();
        }

        [Fact]
        public async Task AuditAppSettings()
        {
            var loggerConfiguration = new LoggerConfiguration();
            var logger = loggerConfiguration.ReadFrom.AppSettings(settingPrefix: "A")
                .CreateLogger();

            var cleanupModel = await _rabbitMQFixture.GetConsumingModelAsync();
            cleanupModel.ExchangeDelete("serilog-settings-sink-audit-exchange");
            cleanupModel.Dispose();
            
            // should not throw
            logger.Dispose();
        }
#endif

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
    }
}