// Copyright 2015 Serilog Contributors
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

using System;
using RabbitMQ.Client;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// RabbitMqClient - this class is the engine that lets you send messages to RabbitMq
    /// </summary>
    public class RabbitMQClient : IDisposable
    {
        // configuration member
        private readonly RabbitMQConfiguration _config;
        private readonly PublicationAddress _publicationAddress;

        // endpoint members
        private IConnectionFactory _connectionFactory;
        private IConnection _connection;
        private IModel _model;
        private IBasicProperties _properties;

        /// <summary>
        /// Constructor for RabbitMqClient
        /// </summary>
        /// <param name="configuration">mandatory</param>
        public RabbitMQClient(RabbitMQConfiguration configuration, bool autoCreateExchange)
        {
            // load configuration
            _config = configuration;
            _publicationAddress = new PublicationAddress(_config.ExchangeType, _config.Exchange, _config.RouteKey);

            // initialize 
            InitializeEndpoint();
            if (autoCreateExchange)
            {
                _model.ExchangeDeclare(_config.Exchange, _config.ExchangeType, _config.DeliveryMode == RabbitMQDeliveryMode.Durable);
            }
        }

        /// <summary>
        /// Private method, that must be run for the client to work.
        /// <remarks>See constructor</remarks>
        /// </summary>
        private void InitializeEndpoint()
        {
            // prepare endpoint
            _connectionFactory = GetConnectionFactory();
            _connection = _connectionFactory.CreateConnection();
            _model = _connection.CreateModel();

            _properties = _model.CreateBasicProperties();
            _properties.DeliveryMode = (byte)_config.DeliveryMode; //persistance
        }

        /// <summary>
        /// Configures a new ConnectionFactory, and returns it
        /// </summary>
        /// <returns></returns>
        private IConnectionFactory GetConnectionFactory()
        {
            // prepare connection factory
            ConnectionFactory connectionFactory = new ConnectionFactory();
            if (_config.AmqpUri != null) connectionFactory.Uri = _config.AmqpUri;

            // setup auto recovery
            connectionFactory.AutomaticRecoveryEnabled = true;
            connectionFactory.NetworkRecoveryInterval = TimeSpan.FromSeconds(2);

            // setup heartbeat if needed
            if (_config.Heartbeat > 0)
                connectionFactory.RequestedHeartbeat = _config.Heartbeat;

            // only set, if has value, otherwise leave default
            if (!string.IsNullOrEmpty(_config.Hostname)) connectionFactory.HostName = _config.Hostname;
            if (!string.IsNullOrEmpty(_config.Username)) connectionFactory.UserName = _config.Username;
            if (!string.IsNullOrEmpty(_config.Password)) connectionFactory.Password = _config.Password;
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
        public void Publish(string message)
        {
            // push message to exchange
            _model.BasicPublish(_publicationAddress, _properties, System.Text.Encoding.UTF8.GetBytes(message));
        }

        public void Dispose()
        {
            _model.Dispose();
            _connection.Dispose();
        }
    }
}