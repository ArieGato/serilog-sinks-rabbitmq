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

using RabbitMQ.Client;
using Serilog.Debugging;

namespace Serilog.Sinks.RabbitMQ;

internal class RabbitMQConnectionFactory : IRabbitMQConnectionFactory
{
    private readonly RabbitMQClientConfiguration _config;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private IConnection _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQConnectionFactory"/> class.
    /// </summary>
    /// <param name="rabbitMQConfiguration"></param>
    /// <param name="cancellationTokenSource"></param>
    public RabbitMQConnectionFactory(
        RabbitMQClientConfiguration rabbitMQConfiguration,
        CancellationTokenSource cancellationTokenSource)
    {
        _config = rabbitMQConfiguration;
        _cancellationTokenSource = cancellationTokenSource;
        _connectionFactory = GetConnectionFactory();
    }

    /// <summary>
    /// Returns the connection. Creates a new connection if none exists.
    /// </summary>
    /// <returns></returns>
    public IConnection GetConnection()
    {
        if (_connection != null)
        {
            return _connection;
        }

        _connectionLock.Wait(_cancellationTokenSource.Token);

        try
        {
            _connection ??= _config.Hostnames.Count == 0
                ? _connectionFactory.CreateConnection()
                : _connectionFactory.CreateConnection(GetAmqpTcpEndpoints());
        }
        finally
        {
            _connectionLock.Release();
        }

        return _connection;
    }

    private List<AmqpTcpEndpoint> GetAmqpTcpEndpoints()
    {
        return _config.Hostnames.Select(hostname =>
        {
            var amqpTcpEndpoint = AmqpTcpEndpoint.Parse(hostname);
            if (_connectionFactory.Port > 0)
                amqpTcpEndpoint.Port = _connectionFactory.Port;
            amqpTcpEndpoint.Ssl.Enabled = _connectionFactory.Ssl.Enabled;
            amqpTcpEndpoint.Ssl.Version = _connectionFactory.Ssl.Version;
            amqpTcpEndpoint.Ssl.AcceptablePolicyErrors = _connectionFactory.Ssl.AcceptablePolicyErrors;
            amqpTcpEndpoint.Ssl.CheckCertificateRevocation = _connectionFactory.Ssl.CheckCertificateRevocation;
            amqpTcpEndpoint.Ssl.ServerName = !string.IsNullOrEmpty(_connectionFactory.Ssl.ServerName)
                    ? _connectionFactory.Ssl.ServerName
                    : amqpTcpEndpoint.HostName;
            return amqpTcpEndpoint;
        }).ToList();
    }

    private ConnectionFactory GetConnectionFactory()
    {
        // prepare connection factory
        var connectionFactory = new ConnectionFactory();

        if (_config.AmqpUri != null)
        {
            connectionFactory.Uri = _config.AmqpUri;
        }

        // setup auto recovery
        connectionFactory.AutomaticRecoveryEnabled = true;
        connectionFactory.NetworkRecoveryInterval = TimeSpan.FromSeconds(2);

        if (_config.SslOption != null)
            connectionFactory.Ssl = _config.SslOption;

        // setup heartbeat if needed
        if (_config.Heartbeat > 0)
        {
            connectionFactory.RequestedHeartbeat = TimeSpan.FromMilliseconds(_config.Heartbeat);
        }

        // only set values when set in configuration, otherwise leave default
        if (!string.IsNullOrEmpty(_config.Username))
        {
            connectionFactory.UserName = _config.Username;
        }

        if (!string.IsNullOrEmpty(_config.Password))
        {
            connectionFactory.Password = _config.Password;
        }

        if (_config.Port > 0)
        {
            connectionFactory.Port = _config.Port;
        }

        if (!string.IsNullOrEmpty(_config.VHost))
        {
            connectionFactory.VirtualHost = _config.VHost;
        }

        return connectionFactory;
    }

    /// <summary>
    /// Close the connection and all channels to RabbitMq
    /// </summary>
    /// <exception cref="AggregateException"></exception>
    public void Close()
    {
        _connectionLock.Wait(10);
        _connection?.Close();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _connectionLock?.Dispose();
            _connection?.Dispose();
        }
        catch (Exception exception)
        {
            SelfLog.WriteLine(exception.Message);
        }
    }
}
