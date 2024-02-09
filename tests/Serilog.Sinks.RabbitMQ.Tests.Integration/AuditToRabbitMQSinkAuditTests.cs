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

namespace Serilog.Sinks.RabbitMQ.Tests.Integration;

/// <summary>
///   Tests for using <see cref="RabbitMQSink" /> as audit sink.
/// </summary>
[Collection("Sequential")]
public sealed class AuditToRabbitMQSinkAuditTests : IClassFixture<RabbitMQFixture>
{
    private readonly RabbitMQFixture _rabbitMQFixture;

    public AuditToRabbitMQSinkAuditTests(RabbitMQFixture rabbitMQFixture)
    {
        _rabbitMQFixture = rabbitMQFixture;
    }

    /// <summary>
    ///   Consumer should receive a message after calling Publish.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>.
    [Fact]
    public async Task Error_LogWithExceptionAndProperties_ConsumerReceivesMessage()
    {
        await _rabbitMQFixture.InitializeAsync();

        const string messageTemplate = "Audit entry with {value}";

        var logger = new LoggerConfiguration()
            .AuditTo
            .RabbitMQ(
                username: RabbitMQFixture.UserName,
                password: RabbitMQFixture.Password,
                hostnames: [RabbitMQFixture.SslCertHostName],
                port: 5672,
                vHost: "/",
                deliveryMode: RabbitMQDeliveryMode.Durable,
                exchange: RabbitMQFixture.SerilogAuditSinkExchange,
                exchangeType: RabbitMQFixture.SerilogAuditSinkExchangeType,
                formatter: new JsonFormatter(),
                autoCreateExchange: true)
            .MinimumLevel.Verbose()
            .CreateLogger();

        logger.Information(messageTemplate, 1.0);

        using var channel = await _rabbitMQFixture.GetConsumingModelAsync();

        var consumer = new EventingBasicConsumer(channel);
        var eventRaised = await Assert.RaisesAsync<BasicDeliverEventArgs>(
            h => consumer.Received += h,
            h => consumer.Received -= h, () =>
            {
                channel.BasicConsume(RabbitMQFixture.SerilogAuditSinkQueueName, autoAck: true, consumer);
                logger.Information(messageTemplate, 1.0);

                // Wait for consumer to receive the message.
                return Task.Delay(50);
            });

        var receivedMessage = JObject.Parse(Encoding.UTF8.GetString(eventRaised.Arguments.Body.ToArray()));
        Assert.Equal("Information", receivedMessage["Level"]);
        Assert.Equal(messageTemplate, receivedMessage["MessageTemplate"]);
        Assert.NotNull(receivedMessage["Properties"]);
        Assert.Equal(1.0, receivedMessage["Properties"]["value"]);

        channel.Close();
        logger.Dispose();
    }
}
