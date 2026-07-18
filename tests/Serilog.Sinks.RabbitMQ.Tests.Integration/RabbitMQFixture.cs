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

using System.Net.Security;
using System.Security.Authentication;

namespace Serilog.Sinks.RabbitMQ.Tests.Integration;

public class RabbitMQFixture : IAsyncDisposable
{
    public const string UserName = "serilog";
    public const string Password = "serilog";
    public const string SerilogAuditSinkExchange = "serilog-sink-audit-exchange";
    public const string SerilogAuditSinkExchangeType = "fanout";
    public const string SerilogAuditSinkQueueName = "serilog-sink-audit-queue";
    public const string SerilogSinkExchange = "serilog-sink-exchange";
    public const string SerilogSinkExchangeType = "fanout";
    public const string SerilogSinkQueueName = "serilog-sink-queue";

    public static readonly string SslCertHostName = InDocker ? "serilog.sinks.rabbitmq.ssl-cert" : "localhost";
    public static readonly string SslPlainHostName = InDocker ? "serilog.sinks.rabbitmq.ssl-plain" : "localhost";

    private readonly RabbitMQClient _rabbitMQClient;
    private readonly ConnectionFactory _connectionFactory;
    private IConnection? _consumingConnection;

    public static bool InDocker => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

    public RabbitMQFixture()
    {
        var rabbitMQClientConfiguration = GetRabbitMQClientConfiguration();
        _rabbitMQClient = new RabbitMQClient(rabbitMQClientConfiguration);

        _connectionFactory = new ConnectionFactory
        {
            HostName = SslCertHostName,
            UserName = UserName,
            Password = Password,
            Port = 5672,
        };
    }

    public static RabbitMQClientConfiguration GetRabbitMQClientConfiguration() =>
        new()
        {
            Port = 5672,
            DeliveryMode = RabbitMQDeliveryMode.Durable,
            Exchange = SerilogSinkExchange,
            Username = UserName,
            Password = Password,
            ExchangeType = SerilogSinkExchangeType,
            Hostnames = [SslCertHostName],
            ClientProvidedName = nameof(RabbitMQFixture),
        };

    public static RabbitMQClientConfiguration GetRabbitMQSslClientConfiguration() =>
        new()
        {
            Port = 5671,
            DeliveryMode = RabbitMQDeliveryMode.Durable,
            Exchange = SerilogSinkExchange,
            ExchangeType = SerilogSinkExchangeType,
            Hostnames = [SslCertHostName],
            SslOption = new SslOption
            {
                Enabled = true,
                ServerName = SslCertHostName,
                AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch |
                                         SslPolicyErrors.RemoteCertificateChainErrors,
                CertPath = "./resources/client-cert.pfx",
                CertPassphrase = "RabbitMQClient",
                Version = SslProtocols.Tls13,
            },
        };

    public async Task InitializeAsync(string? exchangeName = null, CancellationToken cancellationToken = default)
    {
        // Initialize the exchanges and queues.
        await using var channel = await GetConsumingChannelAsync(cancellationToken);

        await channel.ExchangeDeclareAsync(SerilogSinkExchange, SerilogSinkExchangeType, true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(SerilogSinkQueueName, true, false, false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(SerilogSinkQueueName, SerilogSinkExchange, string.Empty, cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(SerilogAuditSinkExchange, SerilogAuditSinkExchangeType, true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(SerilogAuditSinkQueueName, true, false, false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(SerilogAuditSinkQueueName, SerilogAuditSinkExchange, string.Empty, cancellationToken: cancellationToken);

        if (!string.IsNullOrEmpty(exchangeName))
        {
            await channel.ExchangeDeclareAsync(exchangeName!, SerilogSinkExchangeType, true, cancellationToken: cancellationToken);
        }

        await channel.CloseAsync(cancellationToken);

        await Task.Delay(500, cancellationToken);
    }

    public async ValueTask DisposeAsync() => await CleanupAsync(TestContext.Current.CancellationToken);

    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        // Always cleanup the exchanges and queues.
        _consumingConnection ??= await _connectionFactory.CreateConnectionAsync(cancellationToken);

        var channel = await _consumingConnection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.QueueDeleteAsync(SerilogSinkQueueName, cancellationToken: cancellationToken);
        await channel.ExchangeDeleteAsync(SerilogSinkExchange, cancellationToken: cancellationToken);

        await channel.QueueDeleteAsync(SerilogAuditSinkQueueName, cancellationToken: cancellationToken);
        await channel.ExchangeDeleteAsync(SerilogAuditSinkExchange, cancellationToken: cancellationToken);

        await channel.CloseAsync(cancellationToken);
        channel.Dispose();

        if (_consumingConnection is not null)
        {
            await _consumingConnection.CloseAsync(cancellationToken);
        }

        _consumingConnection?.Dispose();

        await _rabbitMQClient.DisposeAsync();
    }

    public Task PublishAsync(string message) => _rabbitMQClient.PublishAsync(Encoding.UTF8.GetBytes(message), new BasicProperties());

    // The IChannel is not disposed automatically, so the calling member is responsible for disposing it.
    public async Task<IChannel> GetConsumingChannelAsync(CancellationToken cancellationToken = default)
    {
        int counter = 0;
        while (true)
        {
            try
            {
                _consumingConnection ??= await _connectionFactory.CreateConnectionAsync(cancellationToken);
                return await _consumingConnection.CreateChannelAsync(cancellationToken: cancellationToken);
            }
            catch (BrokerUnreachableException)
            {
                if (counter++ > 10)
                {
                    throw new Exception("Failed to connect to RabbitMQ.");
                }

                await Task.Delay(1000, cancellationToken);
            }
        }
    }
}
