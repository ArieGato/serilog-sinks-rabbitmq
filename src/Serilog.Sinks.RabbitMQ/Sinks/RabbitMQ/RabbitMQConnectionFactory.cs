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

/// <summary>
/// Default <see cref="IRabbitMQConnectionFactory"/> implementation that lazily creates
/// and caches a single <see cref="IConnection"/>.
/// </summary>
internal sealed class RabbitMQConnectionFactory : IRabbitMQConnectionFactory
{
    private readonly RabbitMQClientConfiguration _config;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private IConnection? _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQConnectionFactory"/> class.
    /// </summary>
    /// <param name="rabbitMQConfiguration">The RabbitMQ client configuration.</param>
    /// <param name="cancellationTokenSource">Token source used to cancel pending connection attempts on shutdown.</param>
    public RabbitMQConnectionFactory(
        RabbitMQClientConfiguration rabbitMQConfiguration,
        CancellationTokenSource cancellationTokenSource)
    {
        _config = rabbitMQConfiguration;
        _cancellationTokenSource = cancellationTokenSource;
        _connectionFactory = GetConnectionFactory();
    }

    /// <inheritdoc />
    public async Task<IConnection> GetConnectionAsync()
    {
        if (_connection != null)
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

        try
        {
            _connection ??= await _connectionFactory.CreateConnectionAsync(GetAmqpTcpEndpoints()).ConfigureAwait(false);
        }
        finally
        {
            _ = _connectionLock.Release();
        }

        return _connection;
    }

    /// <summary>
    /// Builds the <see cref="AmqpTcpEndpoint"/> list used by the underlying
    /// RabbitMQ client. Exposed internally so tests can exercise per-endpoint
    /// SSL configuration without a running broker.
    /// </summary>
    /// <returns>One endpoint per configured hostname.</returns>
    internal IEnumerable<AmqpTcpEndpoint> GetAmqpTcpEndpoints() =>
        _config.Hostnames.Select(hostname =>
        {
            var amqpTcpEndpoint = AmqpTcpEndpoint.Parse(hostname);
            if (_connectionFactory.Port > 0)
            {
                amqpTcpEndpoint.Port = _connectionFactory.Port;
            }

            if (_connectionFactory.Ssl.Enabled)
            {
                // Shallow-clone the SslOption per endpoint so:
                //   1. Every endpoint carries its own SNI ServerName matching its own
                //      hostname (prior code mutated a single shared SslOption, leaking
                //      the first hostname into every subsequent endpoint — issue #289).
                //   2. The caller-supplied SslOption is never mutated; downstream state
                //      the RabbitMQ client may attach to the option is per-connection.
                amqpTcpEndpoint.Ssl = CloneForEndpoint(_connectionFactory.Ssl);
                if (string.IsNullOrEmpty(amqpTcpEndpoint.Ssl.ServerName))
                {
                    amqpTcpEndpoint.Ssl.ServerName = amqpTcpEndpoint.HostName;
                }
            }

            return amqpTcpEndpoint;
        }).ToList();

    /// <summary>
    /// Shallow-clone of <see cref="SslOption"/> so each endpoint gets an independent
    /// instance that can safely carry its own SNI <see cref="SslOption.ServerName"/>.
    /// Delegates and certificate collections stay by reference — they are treated as
    /// immutable configuration inputs by callers and by <c>RabbitMQ.Client</c>.
    /// </summary>
    /// <param name="source">The caller-provided option; never mutated.</param>
    /// <returns>A new <see cref="SslOption"/> with the same values.</returns>
    private static SslOption CloneForEndpoint(SslOption source) =>
        new()
        {
            AcceptablePolicyErrors = source.AcceptablePolicyErrors,
            CertPassphrase = source.CertPassphrase,
            CertPath = source.CertPath,
            Certs = source.Certs,
            CertificateSelectionCallback = source.CertificateSelectionCallback,
            CertificateValidationCallback = source.CertificateValidationCallback,
            CheckCertificateRevocation = source.CheckCertificateRevocation,
            Enabled = source.Enabled,
            ServerName = source.ServerName,
            Version = source.Version,
        };

    /// <summary>
    /// Builds the underlying <see cref="ConnectionFactory"/> from the configuration.
    /// Exposed internally so tests can exercise each wiring branch (auth mechanism
    /// selection, SSL, ClientProvidedName, Heartbeat, VHost, Port, Hostnames) without
    /// a running broker. Each call constructs a fresh instance; callers outside the
    /// constructor should treat the returned value as immutable configuration.
    /// </summary>
    /// <returns>A newly-constructed <see cref="ConnectionFactory"/>.</returns>
    internal ConnectionFactory GetConnectionFactory()
    {
        // prepare connection factory
        var connectionFactory = new ConnectionFactory
        {
            // setup auto recovery
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(2),
        };

        if (_config.ClientProvidedName != null)
        {
            connectionFactory.ClientProvidedName = _config.ClientProvidedName;
        }

        if (_config.SslOption != null)
        {
            connectionFactory.Ssl = _config.SslOption;

            // Assume External authentication mechanism when a certificate path is provided
            if (!string.IsNullOrEmpty(_config.SslOption.CertPath))
            {
                connectionFactory.AuthMechanisms = [new ExternalMechanismFactory()];
            }
        }

        // setup heartbeat if needed
        if (_config.Heartbeat > 0)
        {
            connectionFactory.RequestedHeartbeat = TimeSpan.FromMilliseconds(_config.Heartbeat);
        }

        connectionFactory.UserName = _config.Username;
        connectionFactory.Password = _config.Password;

        if (_config.Port > 0)
        {
            connectionFactory.Port = _config.Port;
        }

        if (!string.IsNullOrEmpty(_config.VHost))
        {
            connectionFactory.VirtualHost = _config.VHost;
        }

        if (_config.Hostnames.Count == 1)
        {
            connectionFactory.HostName = _config.Hostnames[0];
        }

        return connectionFactory;
    }

    /// <inheritdoc />
    public async Task CloseAsync()
    {
        await _connectionLock.WaitAsync(10).ConfigureAwait(false);
        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_connection is not null)
            {
                await _connection.CloseAsync().ConfigureAwait(false);
                _connection.Dispose();
            }
        }
        catch (Exception exception)
        {
            SelfLog.WriteLine(exception.Message);
        }
        finally
        {
            _connectionLock.Dispose();
        }
    }
}
