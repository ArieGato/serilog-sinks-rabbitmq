namespace Serilog.Sinks.RabbitMQ.Tests.Integration
{
    public class RabbitMQFixture : IDisposable
    {
        public const string HostName = "rabbitmq.local";
        public const string UserName = "serilog";
        public const string Password = "serilog";
        public const string SerilogAuditSinkExchange = "serilog-sink-audit-exchange";
        public const string SerilogAuditSinkExchangeType = "fanout";
        public const string SerilogAuditSinkQueueName = "serilog-sink-audit-queue";
        public const string SerilogSinkExchange = "serilog-sink-exchange";
        public const string SerilogSinkExchangeType = "fanout";
        public const string SerilogSinkQueueName = "serilog-sink-queue";
        public const string AmqpUri = $"amqp://{UserName}:{Password}@{HostName}";

        private readonly RabbitMQClient _rabbitMQClient;

        private readonly ConnectionFactory _connectionFactory;
        private IConnection _consumingConnection;

        public RabbitMQFixture()
        {
            _rabbitMQClient = new RabbitMQClient(
                new RabbitMQClientConfiguration
                {
                    Port = 5672,
                    DeliveryMode = RabbitMQDeliveryMode.Durable,
                    Exchange = SerilogSinkExchange,
                    Username = UserName,
                    Password = Password,
                    ExchangeType = "fanout",
                    Hostnames = { HostName },
                });

            _connectionFactory = new ConnectionFactory { HostName = HostName, UserName = UserName, Password = Password };
        }

        public async Task InitializeAsync()
        {
            // Initialize the exchanges and queues.
            var model = await GetConsumingModelAsync();

            model.ExchangeDeclare(SerilogSinkExchange, SerilogSinkExchangeType, true);
            model.QueueDeclare(SerilogSinkQueueName, true, false, false);
            model.QueueBind(SerilogSinkQueueName, SerilogSinkExchange, "");

            model.ExchangeDeclare(SerilogAuditSinkExchange, SerilogAuditSinkExchangeType, true);
            model.QueueDeclare(SerilogAuditSinkQueueName, true, false, false);
            model.QueueBind(SerilogAuditSinkQueueName, SerilogAuditSinkExchange, "");

            model.Close();
            model.Dispose();
        }

        public void Dispose()
        {
            // only cleanup when consuming connection has been used.
            if (_consumingConnection != null)
            {
                var cleanupModel = _consumingConnection.CreateModel();

                cleanupModel.QueueDelete(SerilogSinkQueueName);
                cleanupModel.ExchangeDelete(SerilogSinkExchange);

                cleanupModel.QueueDelete(SerilogAuditSinkQueueName);
                cleanupModel.ExchangeDelete(SerilogAuditSinkExchange);

                cleanupModel.Close();
                cleanupModel.Dispose();

                _consumingConnection?.Close();
                _consumingConnection?.Dispose();
            }

            _rabbitMQClient.Close();
            _rabbitMQClient.Dispose();
        }

        public Task PublishAsync(string message)
        {
            return _rabbitMQClient.PublishAsync(message);
        }

        /// <summary>
        /// The IModel is not Disposed automatically, so the calling member is responsible for Disposing it.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<IModel> GetConsumingModelAsync()
        {
            for (var i = 0; i < 10; ++i)
            {
                try
                {
                    _consumingConnection = _connectionFactory.CreateConnection();

                    return _consumingConnection.CreateModel();
                }
                catch (BrokerUnreachableException)
                {
                    await Task.Delay(1000);
                }
            }

            throw new Exception("Failed to connect to RabbitMQ.");
        }
    }
}