﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
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

            Log.CloseAndFlush();
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

            Log.CloseAndFlush();
        }

#endif

        [TestCategory("WriteTo")]
        [TestMethod]
        public void WriteWithUri() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.WriteTo.RabbitMQ(
                amqpUri: RabbitMQFixture.AmqpUri,
                exchange: RabbitMQFixture.ExchangeName)
                .CreateLogger();

            // should not throw

            Log.CloseAndFlush();
        }

        [TestCategory("WriteTo")]
        [TestMethod]
        public void WriteWithUriAutocreate() {
            RabbitMQFixture.DropRabbitMQExchange();

            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.WriteTo.RabbitMQ(
                amqpUri: RabbitMQFixture.AmqpUri,
                exchange: RabbitMQFixture.ExchangeName,
                autoCreateExchange: true)
                .CreateLogger();

            // should not throw
            Assert.IsTrue(RabbitMQFixture.RabbitMQExchangeExists());

            Log.CloseAndFlush();
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

            Log.CloseAndFlush();
        }

        [TestCategory("AuditTo")]
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

            Log.CloseAndFlush();
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

            Log.CloseAndFlush();
        }

        [TestCleanup]
        public void Cleanup() {
            RabbitMQFixture.DropRabbitMQExchange();
        }
    }
}
