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

namespace Serilog.Sinks.RabbitMQ.Tests.Integration
{
    /// <summary>
    ///   Tests for <see cref="RabbitMQSink" />.
    /// </summary>
    [Collection("Sequential")]
    public sealed class RabbitMqSinkTest : IClassFixture<RabbitMQFixture>
    {
        private readonly RabbitMQFixture _rabbitMQFixture;

        public RabbitMqSinkTest(RabbitMQFixture rabbitMQFixture)
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

            var logger = new LoggerConfiguration()
                .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) =>
                {
                    clientConfiguration.Port = 5672;
                    clientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
                    clientConfiguration.Exchange = RabbitMQFixture.SerilogSinkExchange;
                    clientConfiguration.Username = RabbitMQFixture.UserName;
                    clientConfiguration.Password = RabbitMQFixture.Password;
                    clientConfiguration.ExchangeType = "fanout";
                    clientConfiguration.Hostnames.Add(RabbitMQFixture.HostName);
                    sinkConfiguration.TextFormatter = new JsonFormatter();
                })
                .MinimumLevel.Verbose()
                .CreateLogger();

            const string messageTemplate = "Denominator cannot be zero in {numerator}/{denominator}";

            var channel = await _rabbitMQFixture.GetConsumingModelAsync();
            var consumer = new EventingBasicConsumer(channel);
            var eventRaised = await Assert.RaisesAsync<BasicDeliverEventArgs>(
                h => consumer.Received += h,
                h => consumer.Received -= h, () =>
                {
                    channel.BasicConsume(RabbitMQFixture.SerilogSinkQueueName, autoAck: true, consumer);
                    logger.Error(new DivideByZeroException(), messageTemplate, 1.0, 0.0);

                    // Wait for consumer to receive the message.
                    return Task.Delay(100);
                });

            var receivedMessage = JObject.Parse(Encoding.UTF8.GetString(eventRaised.Arguments.Body.ToArray()));

            Assert.Equal("Error", receivedMessage["Level"]);
            Assert.Equal(messageTemplate, receivedMessage["MessageTemplate"]);
            Assert.NotNull(receivedMessage["Properties"]);
            Assert.Equal(1.0, receivedMessage["Properties"]["numerator"]);
            Assert.Equal(0.0, receivedMessage["Properties"]["denominator"]);
            Assert.Equal("System.DivideByZeroException: Attempted to divide by zero.", receivedMessage["Exception"]);

            logger.Dispose();
        }

        [Fact]
        public async Task Log_WhenInParallel_AllLogEventsArePublished()
        {
            await _rabbitMQFixture.InitializeAsync();

            var logger = new LoggerConfiguration()
                .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) =>
                {
                    clientConfiguration.Port = 5672;
                    clientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
                    clientConfiguration.Exchange = RabbitMQFixture.SerilogSinkExchange;
                    clientConfiguration.Username = RabbitMQFixture.UserName;
                    clientConfiguration.Password = RabbitMQFixture.Password;
                    clientConfiguration.ExchangeType = "fanout";
                    clientConfiguration.Hostnames.Add(RabbitMQFixture.HostName);
                    sinkConfiguration.TextFormatter = new JsonFormatter();
                })
                .MinimumLevel.Verbose()
                .CreateLogger();

            const string messageTemplate = "Denominator cannot be zero in {numerator}/{denominator}";

            var watch = Stopwatch.StartNew();

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };
            Parallel.For(0, 10, parallelOptions, (_, _) =>
            {
                for (var i = 0; i < 1000; i++)
                {
                    logger.Error(new DivideByZeroException(), messageTemplate, 1.0, 0.0);
                }
            });

            var model = await _rabbitMQFixture.GetConsumingModelAsync();
            while (model.MessageCount(RabbitMQFixture.SerilogSinkQueueName) < 10000)
            {
                if (watch.ElapsedMilliseconds > 10000)
                {
                    Assert.Fail("Timeout waiting for messages to be published. Maybe messages are lost");
                }
                await Task.Delay(20);
            }

            watch.Stop();
        }
    }
}