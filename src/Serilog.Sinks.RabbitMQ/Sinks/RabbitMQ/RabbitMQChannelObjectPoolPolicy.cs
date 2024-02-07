// Copyright 2015-2024 Serilog Contributors
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

using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;

namespace Serilog.Sinks.RabbitMQ
{
    /// <inheritdoc />
    internal class RabbitMQChannelObjectPoolPolicy : IPooledObjectPolicy<IRabbitMQChannel>
    {
        private readonly RabbitMQClientConfiguration _config;
        private readonly IRabbitMQConnectionFactory _rabbitMQConnectionFactory;

        private bool _exchangeCreated;

        /// <summary>
        /// Initializes a new instance of the <see cref="RabbitMQChannelObjectPoolPolicy"/> class.
        /// </summary>
        /// <param name="rabbitMQConfiguration"></param>
        /// <param name="rabbitMQConnectionFactory"></param>
        public RabbitMQChannelObjectPoolPolicy(
            RabbitMQClientConfiguration rabbitMQConfiguration,
            IRabbitMQConnectionFactory rabbitMQConnectionFactory)
        {
            _config = rabbitMQConfiguration;
            _rabbitMQConnectionFactory = rabbitMQConnectionFactory;
        }

        /// <inheritdoc />
        public IRabbitMQChannel Create()
        {
            var connection = _rabbitMQConnectionFactory.GetConnection();

            var model = connection.CreateModel();

            CreateExchange(model);

            return new RabbitMQChannel(model);
        }

        /// <inheritdoc />
        public bool Return(IRabbitMQChannel obj)
        {
            return obj.IsOpen;
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
