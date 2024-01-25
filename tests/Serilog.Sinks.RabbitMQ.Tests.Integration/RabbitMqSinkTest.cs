namespace Serilog.Sinks.RabbitMQ.Tests.Integration
{
    /// <summary>
    ///   Tests for <see cref="RabbitMQSink" />.
    /// </summary>
    [Collection("Sequential")]
    public sealed class RabbitMqSinkTest : IDisposable
    {
        private const string QueueName = "serilog-sink-queue";
        private const string HostName = "rabbitmq.local";
        private const string UserName = "serilog";
        private const string Password = "serilog";

        private readonly Logger logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ((clientConfiguration, sinkConfiguration) =>
            {
                clientConfiguration.Port = 5672;
                clientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
                clientConfiguration.Exchange = "serilog-sink-exchange";
                clientConfiguration.Username = UserName;
                clientConfiguration.Password = Password;
                clientConfiguration.ExchangeType = "fanout";
                clientConfiguration.Hostnames.Add(HostName);
                sinkConfiguration.TextFormatter = new JsonFormatter();
            })
            .MinimumLevel.Verbose()
            .CreateLogger();

        private IConnection connection;
        private IModel channel;

        /// <summary>
        ///   Consumer should receive a message after calling Publish.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>.
        [Fact]
        public async Task Error_LogWithExcptionAndProperties_ConsumerReceivesMessage()
        {
            await this.InitializeAsync();
            var messageTemplate = "Denominator cannot be zero in {numerator}/{denominator}";

            var consumer = new EventingBasicConsumer(this.channel);
            var eventRaised = await Assert.RaisesAsync<BasicDeliverEventArgs>(
                h => consumer.Received += h,
                h => consumer.Received -= h,
                async () =>
                {
                    this.channel.BasicConsume(QueueName, autoAck: true, consumer);
                    this.logger.Error(new DivideByZeroException(), messageTemplate, 1.0, 0.0);

                    // Wait for consumer to receive the message.
                    await Task.Delay(100);
                });

            var receivedMessage = JObject.Parse(Encoding.UTF8.GetString(eventRaised.Arguments.Body.ToArray()));
            Assert.Equal("Error", receivedMessage["Level"]);
            Assert.Equal(messageTemplate, receivedMessage["MessageTemplate"]);
            Assert.NotNull(receivedMessage["Properties"]);
            Assert.Equal(1.0, receivedMessage["Properties"]["numerator"]);
            Assert.Equal(0.0, receivedMessage["Properties"]["denominator"]);
            Assert.Equal("System.DivideByZeroException: Attempted to divide by zero.", receivedMessage["Exception"]);
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
            if (channel != null)
            {
                return;
            }

            var factory = new ConnectionFactory { HostName = HostName, UserName = UserName, Password = Password };

            // Wait for RabbitMQ docker container to start and retry connecting to it.
            for (var i = 0; i < 10; ++i)
            {
                try
                {
                    connection = factory.CreateConnection();
                    channel = connection.CreateModel();
                    break;
                }
                catch (BrokerUnreachableException)
                {
                    await Task.Delay(1000);
                }
            }
        }
    }
}
