using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RabbitMQ.Client;

[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]

namespace Serilog.Sinks.RabbitMQ.Tests {
    [DoNotParallelize]
    [TestClass]
    public class RabbitMQFixture  {
        public static string ExchangeName => "LogExchange";
        public static string ExchangeType => "direct";
        public static string HostName => "rabbitmq.local";
        public static string UserName => "serilog";
        public static string Password => "serilog";
        public static Uri AmqpUri => new($"amqp://{UserName}:{Password}@{HostName}");

        [TestMethod]
        public void WriteAndAudit() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.WriteTo.RabbitMQ(
                hostname: RabbitMQFixture.HostName,
                username: RabbitMQFixture.UserName,
                password: RabbitMQFixture.Password,
                exchange: RabbitMQFixture.ExchangeName,
                batchPostingLimit: 1,
                period: TimeSpan.FromMilliseconds(10))
                .AuditTo.RabbitMQ(
                hostname: RabbitMQFixture.HostName,
                username: RabbitMQFixture.UserName,
                password: RabbitMQFixture.Password,
                exchange: RabbitMQFixture.ExchangeName)
                .CreateLogger();

            Log.Information("Some text");

            Thread.Sleep(100); // wait batch execution
        }

        [TestCleanup]
        public void Cleanup() {
            Log.CloseAndFlush();

            RabbitMQFixture.DropRabbitMQExchange();
        }

        public static void CreateRabbitMQExchange() {
            var factory = new ConnectionFactory() { Uri = AmqpUri };
            using var connection = factory.CreateConnection();
            using var model = connection.CreateModel();
            model.ExchangeDeclare(ExchangeName, ExchangeType);
        }

        public static void DropRabbitMQExchange() {
            var factory = new ConnectionFactory()
            {
                Uri = AmqpUri, UserName = UserName, Password = Password
            };
            using var connection = factory.CreateConnection();
            using var model = connection.CreateModel();
            model.ExchangeDelete(ExchangeName, false);
        }

        public static void GetCountRabbitMQExchange() {
            var factory = new ConnectionFactory()
            {
                Uri = AmqpUri,
                UserName = UserName,
                Password = Password
            };
            using var connection = factory.CreateConnection();
            using var model = connection.CreateModel();

            //model.ExchangeDeclarePassive(ExchangeName);
        }

        public static bool RabbitMQExchangeExists() {
            var factory = new ConnectionFactory()
            {
                Uri = AmqpUri,
                UserName = UserName,
                Password = Password
            };
            using var connection = factory.CreateConnection();
            using var model = connection.CreateModel();

            try {
                // Try create exchange with same name and different type
                model.ExchangeDeclare(ExchangeName, "topic", arguments: new Dictionary<string, object>() { { "a", "b" } });
                return false;
            }
            catch {
                return true;
            }
        }
    }
}
