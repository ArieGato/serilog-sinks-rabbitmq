using Serilog.Formatting.Json;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;
using System;
using System.Threading;

namespace Serilog.Sinks.RabbitMQ.Tests.Console
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Wait for RabbitMQ
            Thread.Sleep(10000);

            var config = new RabbitMQClientConfiguration
            {
                Port = 5672,
                DeliveryMode = RabbitMQ.RabbitMQDeliveryMode.Durable,
                Exchange = "serilog-sink-exchange",
                Username = "guest",
                Password = "guest",
                ExchangeType = "fanout"
            };

            config.Hostnames.Add("rabbitmq");

            var logger = new LoggerConfiguration()
                .WriteTo.RabbitMQ(config , new RabbitMQSinkConfiguration(){ TextFormatter = new JsonFormatter() })
                .MinimumLevel.Verbose()
                .CreateLogger();

            logger.Verbose("Verbose log entry");
            logger.Debug("Debug log entry");
            logger.Information("Information log entry");
            logger.Warning("Warning log entry");
            logger.Error("Error log entry");
            logger.Fatal("Fatal log entry");
        }
    }
}
