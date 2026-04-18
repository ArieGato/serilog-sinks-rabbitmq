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

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// This class is the engine that lets you send messages to RabbitMQ.
/// </summary>
internal sealed class RabbitMQClient : IRabbitMQClient
{
    private readonly IRabbitMQChannelPool _channelPool;

    /// <summary>
    /// Default number of channels held in the pool.
    /// </summary>
    internal const int DEFAULT_CHANNEL_COUNT = 64;

    private readonly CancellationTokenSource _closeTokenSource = new();

    private readonly PublicationAddress _publicationAddress;
    private readonly IRabbitMQConnectionFactory _rabbitMQConnectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQClient"/> class.
    /// </summary>
    /// <param name="configuration">The RabbitMQ client configuration.</param>
    public RabbitMQClient(RabbitMQClientConfiguration configuration)
    {
        _rabbitMQConnectionFactory = new RabbitMQConnectionFactory(configuration, _closeTokenSource);
        _channelPool = new RabbitMQChannelPool(configuration, _rabbitMQConnectionFactory);
        _publicationAddress = new PublicationAddress(configuration.ExchangeType, configuration.Exchange, configuration.RoutingKey);
    }

    /// <summary>
    /// Internal constructor for testing.
    /// </summary>
    /// <param name="configuration">The RabbitMQ configuration.</param>
    /// <param name="connectionFactory">The RabbitMQ connection factory.</param>
    /// <param name="channelPool">The channel pool.</param>
    internal RabbitMQClient(
        RabbitMQClientConfiguration configuration,
        IRabbitMQConnectionFactory connectionFactory,
        IRabbitMQChannelPool channelPool)
    {
        _rabbitMQConnectionFactory = connectionFactory;
        _channelPool = channelPool;
        _publicationAddress = new PublicationAddress(configuration.ExchangeType, configuration.Exchange, configuration.RoutingKey);
    }

    /// <inheritdoc />
    public async Task PublishAsync(ReadOnlyMemory<byte> message, BasicProperties basicProperties, string? routingKey = null)
    {
        IRabbitMQChannel? channel = null;
        try
        {
            channel = await _channelPool.GetAsync(_closeTokenSource.Token).ConfigureAwait(false);
            var address = routingKey == null
                ? _publicationAddress
                : new PublicationAddress(_publicationAddress.ExchangeType, _publicationAddress.ExchangeName, routingKey);
            await channel.BasicPublishAsync(address, basicProperties, message).ConfigureAwait(false);
        }
        finally
        {
            if (channel != null)
            {
                await _channelPool.ReturnAsync(channel).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync().ConfigureAwait(false);
        }
        catch
        {
            // DisposeAsync is best-effort; callers wanting to observe errors should use CloseAsync directly.
        }

        await _channelPool.DisposeAsync().ConfigureAwait(false);
        await _rabbitMQConnectionFactory.DisposeAsync().ConfigureAwait(false);
        _closeTokenSource.Dispose();
    }
}
