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

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Serilog.Sinks.RabbitMQ.Tests.Integration;

/// <summary>
/// Tests for using <see cref="RabbitMQSink" /> as WriteTo sink.
/// </summary>
[Collection("Sequential")]
public sealed class WriteToRabbitMQSinkTest : IClassFixture<RabbitMQFixture>
{
    private readonly RabbitMQFixture _rabbitMQFixture;

    public WriteToRabbitMQSinkTest(RabbitMQFixture rabbitMQFixture)
    {
        _rabbitMQFixture = rabbitMQFixture;
    }

    /// <summary>
    /// Consumer should receive a message after calling Publish.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>.
    [Fact]
    public async Task Error_LogWithExceptionAndProperties_ConsumerReceivesMessage()
    {
        await _rabbitMQFixture.InitializeAsync();

        using var logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) =>
            {
                clientConfiguration.Port = 5672;
                clientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
                clientConfiguration.Exchange = RabbitMQFixture.SerilogSinkExchange;
                clientConfiguration.Username = RabbitMQFixture.UserName;
                clientConfiguration.Password = RabbitMQFixture.Password;
                clientConfiguration.ExchangeType = RabbitMQFixture.SerilogSinkExchangeType;
                clientConfiguration.Hostnames = [RabbitMQFixture.SslCertHostName];
                sinkConfiguration.TextFormatter = new JsonFormatter();
            })
            .MinimumLevel.Verbose()
            .CreateLogger();

        const string messageTemplate = "Denominator cannot be zero in {numerator}/{denominator}";

        using var channel = await _rabbitMQFixture.GetConsumingModelAsync();
        var consumer = new EventingBasicConsumer(channel);
        var eventRaised = await Assert.RaisesAsync<BasicDeliverEventArgs>(
            h => consumer.Received += h,
            h => consumer.Received -= h,
            () =>
            {
                channel.BasicConsume(RabbitMQFixture.SerilogSinkQueueName, autoAck: true, consumer);
                logger.Error(new DivideByZeroException(), messageTemplate, 1.0, 0.0);

                // Wait for consumer to receive the message.
                return Task.Delay(1000);
            });

        string json = Encoding.UTF8.GetString(eventRaised.Arguments.Body.ToArray());

        try
        {
            var receivedMessage = JObject.Parse(json);

            receivedMessage["Level"].ShouldBe("Error");
            receivedMessage["MessageTemplate"].ShouldBe(messageTemplate);
            receivedMessage["Properties"].ShouldNotBeNull();
            ((double)receivedMessage["Properties"]!["numerator"]!).ShouldBe(1.0);
            ((double)receivedMessage["Properties"]!["denominator"]!).ShouldBe(0.0);
            receivedMessage["Exception"].ShouldBe("System.DivideByZeroException: Attempted to divide by zero.");
        }
        catch (Exception e)
        {
            Assert.Fail(e.Message + " " + json);
        }

        channel.Close();
    }

    /// <summary>
    /// Consumer should receive a message after logging debug message.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>.
    [Fact]
    public async Task Debug_ThroughReadConfiguration_ConsumerReceivesMessage()
    {
        await _rabbitMQFixture.InitializeAsync();

        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.without.levelswitch.json", false, true)
            .Build();

        using var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        const string messageTemplate = "This is a debug log message";

        using var channel = await _rabbitMQFixture.GetConsumingModelAsync();
        var consumer = new EventingBasicConsumer(channel);
        var eventRaised = await Assert.RaisesAsync<BasicDeliverEventArgs>(
            h => consumer.Received += h,
            h => consumer.Received -= h,
            () =>
            {
                channel.BasicConsume(RabbitMQFixture.SerilogSinkQueueName, autoAck: true, consumer);
                logger.Debug(messageTemplate);

                // Wait for consumer to receive the message.
                return Task.Delay(1000);
            });

        string json = Encoding.UTF8.GetString(eventRaised.Arguments.Body.ToArray());

        try
        {
            var receivedMessage = JObject.Parse(json);

            receivedMessage["Level"].ShouldBe("Debug");
            receivedMessage["MessageTemplate"].ShouldBe(messageTemplate);
        }
        catch (Exception e)
        {
            Assert.Fail(e.Message + " " + json);
        }

        channel.Close();
    }

    [Fact]
    public async Task Log_WhenInParallel_AllLogEventsArePublished()
    {
        const string logParallelMessageExchange = "log-parallel-message-exchange";
        const string logParallelMessageQueue = "log-parallel-message-queue";

        using var model = await _rabbitMQFixture.GetConsumingModelAsync();

        model.ExchangeDeclare(logParallelMessageExchange, RabbitMQFixture.SerilogSinkExchangeType, true);
        model.QueueDeclare(logParallelMessageQueue, true, false, false);
        model.QueueBind(logParallelMessageQueue, logParallelMessageExchange, string.Empty);

        var config = new RabbitMQClientConfiguration
        {
            Port = 5672,
            DeliveryMode = RabbitMQDeliveryMode.Durable,
            Exchange = logParallelMessageExchange,
            ExchangeType = RabbitMQFixture.SerilogSinkExchangeType,
            AutoCreateExchange = true,
            Username = RabbitMQFixture.UserName,
            Password = RabbitMQFixture.Password,
            Hostnames = [RabbitMQFixture.SslCertHostName],
        };
        using var rabbitMQClient = new RabbitMQClient(config);

        using var logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) =>
            {
                clientConfiguration.Port = 5672;
                clientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
                clientConfiguration.Exchange = logParallelMessageExchange;
                clientConfiguration.Username = RabbitMQFixture.UserName;
                clientConfiguration.Password = RabbitMQFixture.Password;
                clientConfiguration.ExchangeType = RabbitMQFixture.SerilogSinkExchangeType;
                clientConfiguration.Hostnames = [RabbitMQFixture.SslCertHostName];
                sinkConfiguration.TextFormatter = new JsonFormatter();
            })
            .MinimumLevel.Verbose()
            .CreateLogger();

        const string messageTemplate = "Denominator cannot be zero in {numerator}/{denominator}";

        var watch = Stopwatch.StartNew();

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };
        Parallel.For(0, 10, parallelOptions, (_, _) =>
        {
            for (int i = 0; i < 1000; i++)
            {
                logger.Error(new DivideByZeroException(), messageTemplate, 1.0, 0.0);
            }
        });

        while (model.MessageCount(logParallelMessageQueue) < 10000)
        {
            if (watch.ElapsedMilliseconds > 10000)
            {
                Assert.Fail("Timeout waiting for messages to be published. Maybe messages are lost");
            }

            await Task.Delay(200);
        }

        watch.Stop();

        model.Close();
    }
}
