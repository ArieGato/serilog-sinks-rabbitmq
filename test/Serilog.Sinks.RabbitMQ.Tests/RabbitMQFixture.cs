using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RabbitMQ.Client;

namespace Serilog.Sinks.RabbitMQ.Tests {
    [DoNotParallelize]
    [TestClass]
    public class RabbitMQFixture  {
        public static string ExchangeName => "LogExchange";
        public static string ExchangeType => "direct";
        public static string HostName => "localhost";
        public static string UserName => "guest";
        public static string Password => "guest";
        public static string AmqpUri => "amqp://" + HostName;

        [TestMethod]
        public void Write() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.WriteTo.RabbitMQ(
                hostname: RabbitMQFixture.HostName,
                username: RabbitMQFixture.UserName,
                password: RabbitMQFixture.Password,
                exchange: RabbitMQFixture.ExchangeName,
                batchPostingLimit : 1,
                period : TimeSpan.FromMilliseconds(10))
                .CreateLogger();

            Log.Information("Some text");

            Thread.Sleep(100);

            Log.CloseAndFlush();
        }

        [TestMethod]
        public void Audit() {
            var loggerConfiguration = new LoggerConfiguration();
            Log.Logger = loggerConfiguration.AuditTo.RabbitMQ(
                hostname: RabbitMQFixture.HostName,
                username: RabbitMQFixture.UserName,
                password: RabbitMQFixture.Password,
                exchange: RabbitMQFixture.ExchangeName)
                .CreateLogger();

            Log.Information("Some text");

            Log.CloseAndFlush();
        }

        [TestCleanup]
        public void Cleanup() {
            RabbitMQFixture.DropRabbitMQExchange();
        }

        public static void CreateRabbitMQExchange() {
            var factory = new ConnectionFactory() { Uri = AmqpUri };
            using (var connection = factory.CreateConnection()) 
            using (var model = connection.CreateModel()) {
                model.ExchangeDeclare(ExchangeName, ExchangeType);
            }
        }

        public static void DropRabbitMQExchange() {
            var factory = new ConnectionFactory() { Uri = AmqpUri };
            using (var connection = factory.CreateConnection()) 
            using (var model = connection.CreateModel()) {
                model.ExchangeDelete(ExchangeName, false);
            }
        }

        public static void GetCountRabbitMQExchange() {
            var factory = new ConnectionFactory() { Uri = AmqpUri };
            using (var connection = factory.CreateConnection()) 
            using (var model = connection.CreateModel()) {
                //model.ExchangeDeclarePassive(ExchangeName);
            }
        }

        public static bool RabbitMQExchangeExists() {
            var factory = new ConnectionFactory() { Uri = AmqpUri };
            using (var connection = factory.CreateConnection()) 
            using (var model = connection.CreateModel()) {
                try {
                    // Try create exxhange with same name and differnet type
                    model.ExchangeDeclare(ExchangeName, "topic", arguments: new Dictionary<string, object>() { { "a", "b" } });
                    return false;
                }
                catch {
                    return true;
                }
            }
        }
    }
}
