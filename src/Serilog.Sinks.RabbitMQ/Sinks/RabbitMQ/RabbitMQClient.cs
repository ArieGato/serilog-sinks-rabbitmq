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

using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// This class is the engine that lets you send messages to RabbitMQ.
/// </summary>
internal sealed class RabbitMQClient : IRabbitMQClient
{
    private readonly ObjectPool<IRabbitMQChannel> _modelObjectPool;

    /// <summary>
    /// Default value for the maximum number of channels.
    /// </summary>
    internal const int DEFAULT_MAX_CHANNEL_COUNT = 64;

    private readonly CancellationTokenSource _closeTokenSource = new();

    private readonly PublicationAddress _publicationAddress;
    private readonly IRabbitMQConnectionFactory _rabbitMQConnectionFactory;

    public RabbitMQClient(RabbitMQClientConfiguration configuration)
    {
        _rabbitMQConnectionFactory = new RabbitMQConnectionFactory(configuration, _closeTokenSource);

        var pooledObjectPolicy = new RabbitMQChannelObjectPoolPolicy(configuration, _rabbitMQConnectionFactory);
        var defaultObjectPoolProvider = new DefaultObjectPoolProvider
        {
            MaximumRetained = configuration.MaxChannels > 0 ? configuration.MaxChannels : DEFAULT_MAX_CHANNEL_COUNT,
        };
        _modelObjectPool = defaultObjectPoolProvider.Create(pooledObjectPolicy);

        _publicationAddress = new PublicationAddress(configuration.ExchangeType, configuration.Exchange, configuration.RouteKey);
    }

    /// <summary>
    /// Internal constructor for testing.
    /// </summary>
    /// <param name="configuration">The RabbitMQ configuration.</param>
    /// <param name="connectionFactory">The RabbitMQ connection factory.</param>
    /// <param name="pooledObjectPolicy">The pooled object policy for creating channels.</param>
    internal RabbitMQClient(
        RabbitMQClientConfiguration configuration,
        IRabbitMQConnectionFactory connectionFactory,
        IPooledObjectPolicy<IRabbitMQChannel> pooledObjectPolicy)
    {
        _rabbitMQConnectionFactory = connectionFactory;

        var defaultObjectPoolProvider = new DefaultObjectPoolProvider
        {
            MaximumRetained = configuration.MaxChannels > 0 ? configuration.MaxChannels : DEFAULT_MAX_CHANNEL_COUNT,
        };
        _modelObjectPool = defaultObjectPoolProvider.Create(pooledObjectPolicy);

        _publicationAddress = new PublicationAddress(configuration.ExchangeType, configuration.Exchange, configuration.RouteKey);
    }

    public async Task PublishAsync(ReadOnlyMemory<byte> message, string? routingKey = null)
    {
        IRabbitMQChannel? channel = null;
        try
        {
            channel = _modelObjectPool.Get();
            var address = routingKey == null
                ? _publicationAddress
                : new PublicationAddress(_publicationAddress.ExchangeType, _publicationAddress.ExchangeName, routingKey);
            await channel.BasicPublishAsync(address, message).ConfigureAwait(false);
        }
        finally
        {
            if (channel != null)
            {
                _modelObjectPool.Return(channel);
            }
        }
    }

    public void Close() => AsyncHelpers.RunSync(CloseAsync);

    public async Task CloseAsync()
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

        try
        {
            await _rabbitMQConnectionFactory.CloseAsync().ConfigureAwait(false);
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

    public void Dispose()
    {
        _closeTokenSource.Dispose();

        if (_modelObjectPool is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _rabbitMQConnectionFactory.Dispose();
    }
}
