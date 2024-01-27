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

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// RabbitMqClient - this class is the engine that lets you send messages to RabbitMq
    /// </summary>
    internal class RabbitMQClient : IDisposable
    {
        // synchronization locks
        private const int MaxChannelCount = 64;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private readonly SemaphoreSlim[] _modelLocks = new SemaphoreSlim[MaxChannelCount];
        private readonly CancellationTokenSource _closeTokenSource = new();
        private readonly CancellationToken _closeToken;
        private int _currentModelIndex = -1;

        // configuration member
        private readonly RabbitMQClientConfiguration _config;
        private readonly PublicationAddress _publicationAddress;

        // endpoint members
        private readonly ConnectionFactory _connectionFactory;
        private readonly IModel[] _models = new IModel[MaxChannelCount];
        private readonly IBasicProperties[] _properties = new IBasicProperties[MaxChannelCount];
        private volatile IConnection _connection;
        private bool _exchangeCreated;

        /// <summary>
        /// Constructor for RabbitMqClient
        /// </summary>
        /// <param name="configuration">mandatory</param>
        public RabbitMQClient(RabbitMQClientConfiguration configuration)
        {
            _closeToken = _closeTokenSource.Token;

            // RabbitMQ channels are not thread-safe.
            // https://www.rabbitmq.com/dotnet-api-guide.html#model-sharing
            // Create a pool of channels and give each call to Publish one channel.
            for (var i = 0; i < MaxChannelCount; i++)
            {
                _modelLocks[i] = new SemaphoreSlim(1, 1);
            }

            // load configuration
            _config = configuration;
            _publicationAddress = new PublicationAddress(_config.ExchangeType, _config.Exchange, _config.RouteKey);

            // initialize
            _connectionFactory = GetConnectionFactory();
        }

        /// <summary>
        /// Publishes a message to RabbitMq Exchange
        /// </summary>
        /// <param name="message"></param>
        public async Task PublishAsync(string message)
        {
            var currentModelIndex = Interlocked.Increment(ref _currentModelIndex);

            // Interlocked.Increment can overflow and return a negative currentModelIndex.
            // Ensure that currentModelIndex is always in the range of [0, MaxChannelCount) by using this formula.
            // https://stackoverflow.com/a/14997413/263003
            currentModelIndex = (currentModelIndex % MaxChannelCount + MaxChannelCount) % MaxChannelCount;
            var modelLock = _modelLocks[currentModelIndex];
            await modelLock.WaitAsync(_closeToken);

            try
            {
                var model = _models[currentModelIndex];
                var properties = _properties[currentModelIndex];

                // if model is closed, dispose it and create a new one
                if (model is { IsOpen: false })
                {
                    model.Dispose();
                    model = null;
                }

                if (model == null)
                {
                    var connection = await GetConnectionAsync();
                    model = connection.CreateModel();
                    _models[currentModelIndex] = model;

                    CreateExchange(model);

                    properties = model.CreateBasicProperties();
                    properties.DeliveryMode = (byte)_config.DeliveryMode; // persistence
                    _properties[currentModelIndex] = properties;
                }

                // push message to exchange
                model.BasicPublish(_publicationAddress, properties, System.Text.Encoding.UTF8.GetBytes(message));
            }
            finally
            {
                modelLock.Release();
            }
        }

        /// <summary>
        /// Close the connection and all channels to RabbitMq
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        public void Close()
        {
            var exceptions = new List<Exception>();

            try
            {
                _closeTokenSource.Cancel();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }

            // Disposing channel and connection objects is not enough, they must be explicitly closed with the API methods.
            // https://www.rabbitmq.com/dotnet-api-guide.html#disconnecting
            for (var i = 0; i < _models.Length; i++)
            {
                try
                {
                    _modelLocks[i].Wait(10);
                    _models[i]?.Close();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            try
            {
                _connectionLock.Wait(10);
                _connection?.Close();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException($"Exceptions occurred while closing {nameof(RabbitMQClient)}", exceptions);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _closeTokenSource.Dispose();

            _connectionLock.Dispose();
            foreach (var modelLock in _modelLocks)
            {
                modelLock.Dispose();
            }

            foreach (var model in _models)
            {
                model?.Dispose();
            }

            _connection?.Dispose();
        }

        private async Task<IConnection> GetConnectionAsync()
        {
            if (_connection == null)
            {
                await _connectionLock.WaitAsync(_closeToken);
                try
                {
                    if (_connection == null)
                    {
                        _connection = _config.Hostnames.Count == 0
                            ? _connectionFactory.CreateConnection()
                            : _connectionFactory.CreateConnection(_config.Hostnames.Select(h =>
                            {
                                var amqpTcpEndpoint = AmqpTcpEndpoint.Parse(h);
                                if (_connectionFactory.Port > 0) amqpTcpEndpoint.Port = _connectionFactory.Port;
                                amqpTcpEndpoint.Ssl.Enabled = _connectionFactory.Ssl.Enabled;
                                amqpTcpEndpoint.Ssl.Version = _connectionFactory.Ssl.Version;
                                amqpTcpEndpoint.Ssl.AcceptablePolicyErrors =
                                    _connectionFactory.Ssl.AcceptablePolicyErrors;
                                amqpTcpEndpoint.Ssl.CheckCertificateRevocation =
                                    _connectionFactory.Ssl.CheckCertificateRevocation;
                                amqpTcpEndpoint.Ssl.ServerName =
                                    !string.IsNullOrEmpty(_connectionFactory.Ssl.ServerName)
                                        ? _connectionFactory.Ssl.ServerName
                                        : amqpTcpEndpoint.HostName;
                                return amqpTcpEndpoint;
                            }).ToList());
                    }
                }
                finally
                {
                    _connectionLock.Release();
                }
            }

            return _connection;
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

            if (_config.SslOption != null) connectionFactory.Ssl = _config.SslOption;

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

        private void CreateExchange(IModel model)
        {
            if (!_exchangeCreated && _config.AutoCreateExchange)
            {
                model.ExchangeDeclare(_config.Exchange, _config.ExchangeType,
                    _config.DeliveryMode == RabbitMQDeliveryMode.Durable);
                _exchangeCreated = true;
            }
        }
    }
}