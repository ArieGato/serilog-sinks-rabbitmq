namespace Serilog.Sinks.RabbitMQ.Tests.Integration
{
    /// <summary>
    ///   Tests for <see cref="RabbitMqAuditSink" />.
    /// </summary>
    [Collection("Sequential")]
    public sealed class RabbitMqAuditSink : IDisposable
    {
        private const string QueueName = "serilog-sink-audit-queue";
        private const string HostName = "rabbitmq.local";
        private const string UserName = "serilog";
        private const string Password = "serilog";

        private readonly Logger logger = new LoggerConfiguration()
            .AuditTo
            .RabbitMQ($"amqp://{UserName}:{Password}@{HostName}", deliveryMode: RabbitMQDeliveryMode.Durable, exchange: "serilog-sink-audit-exchange", exchangeType: "fanout", formatter: new JsonFormatter(), autoCreateExchange: true)
            .MinimumLevel.Verbose()
            .CreateLogger();

        private IConnection connection;
        private IModel channel;

        /// <summary>
        ///   Consumer should receive a message after calling Publish.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>.
        [Fact]
        public async Task Error_LogWithExceptionAndProperties_ConsumerReceivesMessage()
        {
            await this.InitializeAsync();
            var messageTemplate = "Audit entry with {value}";

            this.logger.Information(messageTemplate, 1.0);

            var consumer = new EventingBasicConsumer(this.channel);
            var eventRaised = await Assert.RaisesAsync<BasicDeliverEventArgs>(
                h => consumer.Received += h,
                h => consumer.Received -= h,
                async () =>
                {
                    this.channel.BasicConsume(QueueName, autoAck: true, consumer);
                    this.logger.Information(messageTemplate, 1.0);

                    // Wait for consumer to receive the message.
                    await Task.Delay(50);
                });

            var receivedMessage = JObject.Parse(Encoding.UTF8.GetString(eventRaised.Arguments.Body.ToArray()));
            Assert.Equal("Information", receivedMessage["Level"]);
            Assert.Equal(messageTemplate, receivedMessage["MessageTemplate"]);
            Assert.NotNull(receivedMessage["Properties"]);
            Assert.Equal(1.0, receivedMessage["Properties"]["value"]);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.logger?.Dispose();
            this.channel?.Dispose();
            this.connection?.Dispose();
        }

        private async Task InitializeAsync()
        {
            if (connection != null)
            {
                return;
            }

            var factory = new ConnectionFactory { HostName = HostName, UserName = UserName, Password = Password };

            // Wait for RabbitMQ docker container to start and retry connecting to it.
            for (int i = 0; i < 10; ++i)
            {
                try
                {
                    this.connection = factory.CreateConnection();
                    this.channel = this.connection.CreateModel();
                    return;
                }
                catch (BrokerUnreachableException)
                {
                    await Task.Delay(1000);
                }
            }

            throw new InvalidOperationException("Failed to initialize");
        }
    }
}
