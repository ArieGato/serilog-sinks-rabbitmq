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

using System.Collections.Concurrent;
using RabbitMQ.Client;
using Serilog.Debugging;

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// Fixed-size pool of <see cref="IRabbitMQChannel"/> instances. Channels are pre-opened
/// in the background at construction and broken channels are replaced asynchronously to
/// keep the pool full.
/// </summary>
internal sealed class RabbitMQChannelPool : IRabbitMQChannelPool
{
    private static readonly TimeSpan WarmUpRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly RabbitMQClientConfiguration _config;
    private readonly IRabbitMQConnectionFactory _connectionFactory;
    private readonly int _size;
    private readonly ConcurrentBag<IRabbitMQChannel> _available = new();
    private readonly SemaphoreSlim _signal;
    private readonly CancellationTokenSource _shutdownCts = new();
    private volatile bool _exchangeDeclared;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQChannelPool"/> class
    /// and starts background warm-up of the configured number of channels.
    /// </summary>
    /// <param name="configuration">The RabbitMQ client configuration.</param>
    /// <param name="connectionFactory">The connection factory used to obtain the underlying <see cref="IConnection"/>.</param>
    public RabbitMQChannelPool(
        RabbitMQClientConfiguration configuration,
        IRabbitMQConnectionFactory connectionFactory)
    {
        _config = configuration;
        _connectionFactory = connectionFactory;
        _size = configuration.ChannelCount > 0 ? configuration.ChannelCount : RabbitMQClient.DEFAULT_CHANNEL_COUNT;
        _signal = new SemaphoreSlim(0, _size);

        _ = Task.Run(() => WarmUpAsync(_size, _shutdownCts.Token));
    }

    /// <inheritdoc />
    public async ValueTask<IRabbitMQChannel> GetAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        await _signal.WaitAsync(linked.Token).ConfigureAwait(false);
        _available.TryTake(out var channel);
        return channel!;
    }

    /// <inheritdoc />
    public void Return(IRabbitMQChannel channel)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            channel.Dispose();
            return;
        }

        if (channel.IsOpen)
        {
            _available.Add(channel);
            _signal.Release();
            return;
        }

        channel.Dispose();
        _ = Task.Run(() => WarmUpAsync(1, _shutdownCts.Token));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _shutdownCts.Cancel();
        }
        catch (Exception ex)
        {
            SelfLog.WriteLine("Error cancelling RabbitMQ channel pool warm-up: {0}", ex);
        }

        while (_available.TryTake(out var channel))
        {
            channel.Dispose();
        }

        _signal.Dispose();
        _shutdownCts.Dispose();
    }

    private async Task WarmUpAsync(int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var channel = await CreateChannelAsync(cancellationToken).ConfigureAwait(false);
                    _available.Add(channel);
                    _signal.Release();
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("Failed to warm up RabbitMQ channel: {0}", ex);
                    try
                    {
                        await Task.Delay(WarmUpRetryDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
    }

    private async Task<IRabbitMQChannel> CreateChannelAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.GetConnectionAsync().ConfigureAwait(false);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!_exchangeDeclared && _config.AutoCreateExchange)
        {
            await channel.ExchangeDeclareAsync(
                _config.Exchange,
                _config.ExchangeType,
                _config.DeliveryMode == RabbitMQDeliveryMode.Durable,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _exchangeDeclared = true;
        }

        return new RabbitMQChannel(channel);
    }
}
