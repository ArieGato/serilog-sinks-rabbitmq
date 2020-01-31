// Copyright 2015 Serilog Contributors
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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// RabbitMqClient - this class is the engine that lets you send messages to RabbitMq
    /// </summary>
    public class RabbitMQClient : IDisposable
    {
        // synchronization locks
        private const int MaxChannelCount = 64;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim[] _modelLocks = new SemaphoreSlim[MaxChannelCount];
        private readonly CancellationTokenSource _closeTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _closeToken;
        private int _currentModelIndex = -1;

        // configuration member
        private readonly RabbitMQClientConfiguration _config;

        // endpoint members
        private readonly IConnectionFactory _connectionFactory;
        private readonly IModel[] _models = new IModel[MaxChannelCount];
        private readonly IBasicProperties[] _properties = new IBasicProperties[MaxChannelCount];
        private volatile IConnection _connection;

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

            // initialize
            _connectionFactory = GetConnectionFactory();
        }

        /// <summary>
        /// Configures a new ConnectionFactory, and returns it
        /// </summary>
        /// <returns></returns>
        private IConnectionFactory GetConnectionFactory()
        {
            // prepare connection factory
            var connectionFactory = new ConnectionFactory
            {
                UserName = _config.Username,
                Password = _config.Password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(2),
                UseBackgroundThreadsForIO = _config.UseBackgroundThreadsForIO
            };

            if (_config.SslOption != null)
            {
                connectionFactory.Ssl.Version = _config.SslOption.Version;
                connectionFactory.Ssl.CertPath = _config.SslOption.CertPath;
                connectionFactory.Ssl.ServerName = _config.SslOption.ServerName;
                connectionFactory.Ssl.Enabled = _config.SslOption.Enabled;
                connectionFactory.Ssl.AcceptablePolicyErrors = _config.SslOption.AcceptablePolicyErrors;
            }
            // setup heartbeat if needed
            if (_config.Heartbeat > 0)
                connectionFactory.RequestedHeartbeat = _config.Heartbeat;

            // only set, if has value, otherwise leave default
            if (_config.Port > 0) connectionFactory.Port = _config.Port;
            if (!string.IsNullOrEmpty(_config.VHost)) connectionFactory.VirtualHost = _config.VHost;
            if (_config.Protocol != null) connectionFactory.Protocol = _config.Protocol;

            // return factory
            return connectionFactory;
        }

        /// <summary>
        /// Publishes a message to RabbitMq Exchange
        /// </summary>
        /// <param name="message"></param>
        public Task PublishAsync(string message)
        {
            return PublishAsync(message, routingKey: null);
        }

        /// <summary>
        /// Publishes a message to RabbitMq Exchange
        /// </summary>
        /// <param name="message"></param>
        /// <param name="routingKey">The routing key to use when publishing the message.</param>
        public async Task PublishAsync(string message, string routingKey)
        {
            routingKey = routingKey ?? _config.RouteKey;

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
                if (model == null)
                {
                    var connection = await GetConnectionAsync();
                    model = connection.CreateModel();
                    _models[currentModelIndex] = model;

                    properties = model.CreateBasicProperties();
                    properties.DeliveryMode = (byte)_config.DeliveryMode; // persistence
                    _properties[currentModelIndex] = properties;
                }

                var publicationAddress = new PublicationAddress(_config.ExchangeType, _config.Exchange, routingKey);

                // push message to exchange
                model.BasicPublish(publicationAddress, properties, System.Text.Encoding.UTF8.GetBytes(message));
            }
            finally
            {
                modelLock.Release();
            }
        }

        public void Close()
        {
            IList<Exception> exceptions = new List<Exception>();
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
                throw new AggregateException(exceptions);
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
                            : _connectionFactory.CreateConnection(_config.Hostnames);
                    }
                }
                finally
                {
                    _connectionLock.Release();
                }
            }

            return _connection;
        }
    }
}
