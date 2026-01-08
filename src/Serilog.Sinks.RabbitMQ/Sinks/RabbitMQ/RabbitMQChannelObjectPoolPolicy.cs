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

namespace Serilog.Sinks.RabbitMQ;

internal sealed class RabbitMQChannelObjectPoolPolicy : IPooledObjectPolicy<IRabbitMQChannel>
{
    private readonly RabbitMQClientConfiguration _config;
    private readonly IRabbitMQConnectionFactory _rabbitMQConnectionFactory;

    private volatile bool _exchangeCreated;

    public RabbitMQChannelObjectPoolPolicy(
        RabbitMQClientConfiguration rabbitMQConfiguration,
        IRabbitMQConnectionFactory rabbitMQConnectionFactory)
    {
        _config = rabbitMQConfiguration;
        _rabbitMQConnectionFactory = rabbitMQConnectionFactory;
    }

    public IRabbitMQChannel Create() => AsyncHelpers.RunSync(CreateAsync);

    private async Task<IRabbitMQChannel> CreateAsync()
    {
        var connection = await _rabbitMQConnectionFactory.GetConnectionAsync().ConfigureAwait(false);
        var channel = await connection.CreateChannelAsync().ConfigureAwait(false);

        await CreateExchangeAsync(channel).ConfigureAwait(false);

        return new RabbitMQChannel(channel);
    }

    public bool Return(IRabbitMQChannel obj) => obj.IsOpen;

    private async Task CreateExchangeAsync(IChannel channel)
    {
        if (!_exchangeCreated && _config.AutoCreateExchange)
        {
            await channel.ExchangeDeclareAsync(_config.Exchange, _config.ExchangeType, _config.DeliveryMode == RabbitMQDeliveryMode.Durable)
                .ConfigureAwait(false);
            _exchangeCreated = true;
        }
    }
}
