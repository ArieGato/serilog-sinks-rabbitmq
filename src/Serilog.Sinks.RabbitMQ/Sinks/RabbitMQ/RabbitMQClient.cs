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
/// RabbitMqClient - this class is the engine that lets you send messages to RabbitMq.
/// </summary>
internal class RabbitMQClient : IRabbitMQClient
{
    private readonly ObjectPool<IRabbitMQChannel> _modelObjectPool;

    public const int DEFAULT_MAX_CHANNEL_COUNT = 64;
    private readonly CancellationTokenSource _closeTokenSource = new();

    // configuration member
    private readonly PublicationAddress _publicationAddress;
    private readonly IRabbitMQConnectionFactory _rabbitMQConnectionFactory;

    /// <summary>
    /// Constructor for RabbitMqClient.
    /// </summary>
    /// <param name="configuration">mandatory.</param>
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

    /// <summary>
    /// Publishes a message to RabbitMq Exchange.
    /// </summary>
    /// <param name="message"></param>
    public void Publish(string message)
    {
        IRabbitMQChannel? channel = null;
        try
        {
            channel = _modelObjectPool.Get();
            channel.BasicPublish(_publicationAddress, System.Text.Encoding.UTF8.GetBytes(message));
        }
        finally
        {
            if (channel != null)
            {
                _modelObjectPool.Return(channel);
            }
        }
    }

    /// <summary>
    /// Close the connection and all channels to RabbitMq.
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

        try
        {
            _rabbitMQConnectionFactory.Close();
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

        if (_modelObjectPool is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _rabbitMQConnectionFactory.Dispose();
    }
}
