using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
#if NET_FX
using System.Configuration;
#endif

namespace Serilog.Sinks.RabbitMQ.Tests {
    [DoNotParallelize]
    [TestClass]
    public class ConfigurationExtensionsFixture {
#if NET_FX
        [TestCategory("WriteTo")]
        [TestMethod]
        public void WriteWithUriByName() {
            string NameOrUri = "NamedConnection";

            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.WriteTo.RabbitMQ(
                amqpUri: NameOrUri,
                exchange: RabbitMQFixture.ExchangeName)
                .CreateLogger();

            // should not throw
        }

        [TestCategory("WriteTo")]
        [TestMethod]
        public void WriteAppSettings() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.ReadFrom.AppSettings(settingPrefix: "W" )
                .CreateLogger();

            // should not throw
        }

        [TestCategory("WriteTo")]
        [TestMethod]
        public void WriteAppSettingsMultipleHosts() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.ReadFrom.AppSettings(settingPrefix: "H")
                .CreateLogger();

            // should not throw
        }

        [TestCategory("AuditTo")]
        [TestMethod]
        public void AuditWithUriByName() {
            string NameOrUri = "NamedConnection";

            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.AuditTo.RabbitMQ(
                amqpUri: NameOrUri,
                exchange: RabbitMQFixture.ExchangeName)
                .CreateLogger();

            // should not throw
        }

        [TestCategory("AuditTo")]
        [TestMethod]
        public void AuditAppSettings() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.ReadFrom.AppSettings(settingPrefix: "A")
                .CreateLogger();

            // should not throw
        }

#endif
        [TestCategory("WriteTo")]
        [TestMethod]
        public void WriteForMethodCompatibility() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.WriteTo.RabbitMQ(
                new Sinks.RabbitMQ.RabbitMQConfiguration() {
                    Hostname = RabbitMQFixture.HostName,
                    Username = RabbitMQFixture.UserName,
                    Password = RabbitMQFixture.Password,
                }, null).CreateLogger();

            // should not throw
        }

        [TestCategory("WriteTo")]
        [TestMethod]
        public void WriteWithUri() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.WriteTo.RabbitMQ(
                amqpUri: RabbitMQFixture.AmqpUri,
                exchange: RabbitMQFixture.ExchangeName)
                .CreateLogger();

            // should not throw
        }

        [TestCategory("WriteTo")]
        [TestMethod]
        public void WriteWithUriAutocreate() {
            RabbitMQFixture.DropRabbitMQExchange();

            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.WriteTo.RabbitMQ(
                amqpUri: RabbitMQFixture.AmqpUri,
                exchange: RabbitMQFixture.ExchangeName,
                exchangeType: RabbitMQFixture.ExchangeType, 
                autoCreateExchange: true)
                .CreateLogger();

            // should not throw
            Assert.IsTrue(RabbitMQFixture.RabbitMQExchangeExists());
        }

        [TestCategory("WriteTo")]
        [TestMethod]
        public void WriteWithHostUserPwd() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.WriteTo.RabbitMQ(
                hostname: RabbitMQFixture.HostName,
                username: RabbitMQFixture.UserName,
                password: RabbitMQFixture.Password,
                exchange: RabbitMQFixture.ExchangeName)
                .CreateLogger();

            // should not throw
        }

        [TestCategory("AuditTo")]
        [TestMethod]
        public void AuditWithUri() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.AuditTo.RabbitMQ(
                amqpUri: RabbitMQFixture.AmqpUri,
                exchange: RabbitMQFixture.ExchangeName)
                .CreateLogger();

            // should not throw
        }

        [TestCategory("AuditTo")]
        [TestMethod]
        public void AuditWithHostUserPwd() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.AuditTo.RabbitMQ(
                hostname: RabbitMQFixture.HostName,
                username: RabbitMQFixture.UserName,
                password: RabbitMQFixture.Password,
                exchange: RabbitMQFixture.ExchangeName)
                .CreateLogger();

            // should not throw
        }

        [TestCleanup]
        public void Cleanup() {
            Log.CloseAndFlush();

            RabbitMQFixture.DropRabbitMQExchange();
        }
    }
}
