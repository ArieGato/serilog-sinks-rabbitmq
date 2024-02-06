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

public class RabbitMQFixture : IDisposable
{
    public static bool InDocker => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
    public static readonly string HostName = InDocker ? "serilog.sinks.rabbitmq.server" : "localhost";
    public const string UserName = "serilog";
    public const string Password = "serilog";
    public const string SerilogAuditSinkExchange = "serilog-sink-audit-exchange";
    public const string SerilogAuditSinkExchangeType = "fanout";
    public const string SerilogAuditSinkQueueName = "serilog-sink-audit-queue";
    public const string SerilogSinkExchange = "serilog-sink-exchange";
    public const string SerilogSinkExchangeType = "fanout";
    public const string SerilogSinkQueueName = "serilog-sink-queue";
    public static readonly string AmqpUri = $"amqp://{UserName}:{Password}@{HostName}";

    private readonly RabbitMQClient _rabbitMQClient;

    private readonly ConnectionFactory _connectionFactory;
    private IConnection _consumingConnection;

    public RabbitMQFixture()
    {
        var rabbitMQClientConfiguration = GetRabbitMQClientConfiguration();
        _rabbitMQClient = new RabbitMQClient(rabbitMQClientConfiguration);

        _connectionFactory = new ConnectionFactory { HostName = HostName, UserName = UserName, Password = Password };
    }

    public static RabbitMQClientConfiguration GetRabbitMQClientConfiguration()
    {
        return new RabbitMQClientConfiguration
        {
            Port = 5672,
            DeliveryMode = RabbitMQDeliveryMode.Durable,
            Exchange = SerilogSinkExchange,
            Username = UserName,
            Password = Password,
            ExchangeType = "fanout",
            Hostnames = { HostName },
        };
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

    public void Publish(string message)
    {
        _rabbitMQClient.Publish(message);
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
