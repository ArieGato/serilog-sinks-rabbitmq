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
using RabbitMQ.Client.Exceptions;
using Serilog.Debugging;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// RabbitMqClient - this class is the engine that lets you send messages to RabbitMq
    /// </summary>
    public class RabbitMQClient
    {
        // configuration member
        private readonly RabbitMQConfiguration _config;
        private readonly PublicationAddress _publicationAddress;

        // endpoint members
        private Func<ModelWithProperties> _modelWithPropertiesFactory;
        private ModelWithProperties _modelWithProperties;

        /// <summary>
        /// Constructor for RabbitMqClient
        /// </summary>
        /// <param name="configuration">mandatory</param>
        public RabbitMQClient(RabbitMQConfiguration configuration)
        {
            // load configuration
            _config = configuration;
            _publicationAddress = new PublicationAddress(_config.ExchangeType, _config.Exchange, _config.RouteKey);

            // Lazy 
            _modelWithPropertiesFactory = () =>
            {
                var model = GetConnectionFactory()
                    .CreateConnection()
                    .CreateModel();

                var basicProperties = model.CreateBasicProperties();
                basicProperties.DeliveryMode = (byte)_config.DeliveryMode; //persistance
                return new ModelWithProperties(model, basicProperties);
            };
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
                HostName = _config.Hostname,
                UserName = _config.Username,
                Password = _config.Password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(2)
            };

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
        public void Publish(string message)
        {
            var modelAndProperties = LazilyInitializeModel();
            if (modelAndProperties == null)
            {
                SelfLog.WriteLine("Could not connect to RabbitMQ broker");
                return;
            }
            modelAndProperties.Model.BasicPublish(_publicationAddress,
                modelAndProperties.Properties, System.Text.Encoding.UTF8.GetBytes(message));
        }

        private ModelWithProperties LazilyInitializeModel()
        {
            if (_modelWithProperties == null)
            {
                try
                {
                    _modelWithProperties = _modelWithPropertiesFactory();
                }
                catch (BrokerUnreachableException e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            return _modelWithProperties;
        }

        internal class ModelWithProperties
        {
            public IModel Model { get { return _model; } }
            public IBasicProperties Properties { get { return _properties; } }

            private readonly IModel _model;
            private readonly IBasicProperties _properties;

            public ModelWithProperties(IModel model, IBasicProperties properties)
            {
                _model = model;
                _properties = properties;
            }
        }
    }
}