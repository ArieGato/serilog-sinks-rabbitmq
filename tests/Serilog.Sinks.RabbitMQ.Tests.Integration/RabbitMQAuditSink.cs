namespace Serilog.Sinks.RabbitMQ.Tests.Integration
{
    /// <summary>
    ///   Tests for <see cref="RabbitMqAuditSink" />.
    /// </summary>
    [Collection("Sequential")]
    public sealed class RabbitMqAuditSink : IClassFixture<RabbitMQFixture>
    {
        private readonly RabbitMQFixture _rabbitMQFixture;

        public RabbitMqAuditSink(RabbitMQFixture rabbitMQFixture)
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
                    amqpUri: $"amqp://{RabbitMQFixture.UserName}:{RabbitMQFixture.Password}@{RabbitMQFixture.HostName}",
                    deliveryMode: RabbitMQDeliveryMode.Durable,
                    exchange: RabbitMQFixture.SerilogAuditSinkExchange,
                    exchangeType: RabbitMQFixture.SerilogAuditSinkExchangeType,
                    formatter: new JsonFormatter(),
                    autoCreateExchange: true)
                .MinimumLevel.Verbose()
                .CreateLogger();

            logger.Information(messageTemplate, 1.0);

            var channel = await _rabbitMQFixture.GetConsumingModelAsync();

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

            logger.Dispose();
        }
    }
}
