namespace Serilog.Sinks.RabbitMQ.Tests.Integration
{
    using System;
    using System.Text;
    using System.Threading.Tasks;
    using global::RabbitMQ.Client;
    using global::RabbitMQ.Client.Events;
    using global::RabbitMQ.Client.Exceptions;
    using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;
    using Xunit;

    /// <summary>
    ///   Tests for <see cref="RabbitMQClient" />.
    /// </summary>
    [Collection("Sequential")]
    public sealed class RabbitMqClientTest : IDisposable
    {
        private const string QueueName = "serilog-sink-queue";
        private const string HostName = "rabbitmq";

        private readonly RabbitMQClient client = new RabbitMQClient(new RabbitMQClientConfiguration
        {
            Port = 5672,
            DeliveryMode = RabbitMQDeliveryMode.Durable,
            Exchange = "serilog-sink-exchange",
            Username = "guest",
            Password = "guest",
            ExchangeType = "fanout",
            Hostnames = { HostName },
        });

        private IConnection connection;
        private IModel channel;

        /// <summary>
        ///   Consumer should receive a message after calling Publish.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>.
        [Fact]
        public async Task Publish_SingleMessage_ConsumerReceivesMessage()
        {
            await this.InitializeAsync();
            var message = Guid.NewGuid().ToString();

            var consumer = new EventingBasicConsumer(this.channel);
            var eventRaised = await Assert.RaisesAsync<BasicDeliverEventArgs>(
                h => consumer.Received += h,
                h => consumer.Received -= h,
                async () =>
                {
                    this.channel.BasicConsume(QueueName, autoAck: true, consumer);
                    await this.client.PublishAsync(message);

                    // Wait for consumer to receive the message.
                    await Task.Delay(50);
                });

            var receivedMessage = Encoding.UTF8.GetString(eventRaised.Arguments.Body);
            Assert.Equal(message, receivedMessage);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.client?.Dispose();
            this.channel?.Dispose();
            this.connection?.Dispose();
        }

        private async Task InitializeAsync()
        {
            if (this.connection == null)
            {
                var factory = new ConnectionFactory { HostName = HostName };

                // Wait for RabbitMQ docker container to start and retry connecting to it.
                for (int i = 0; i < 10; ++i)
                {
                    try
                    {
                        this.connection = factory.CreateConnection();
                        this.channel = this.connection.CreateModel();
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
}
