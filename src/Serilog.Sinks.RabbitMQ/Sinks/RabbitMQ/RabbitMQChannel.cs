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

/// <summary>
/// A wrapper class for <see cref="IChannel"/> to be used in <see cref="ObjectPool{T}"/>.
/// </summary>
internal sealed class RabbitMQChannel : IRabbitMQChannel
{
    private readonly BasicProperties _properties;
    private readonly IChannel _channel;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQChannel"/> class.
    /// </summary>
    /// <param name="channel">The wrapped <see cref="IChannel"/>.</param>
    public RabbitMQChannel(IChannel channel)
    {
        _channel = channel;

        // _properties = channel.CreateBasicProperties();
        _properties = new BasicProperties { AppId = "AppId" };
    }

    public bool IsOpen => _channel.IsOpen;

    public async Task BasicPublishAsync(PublicationAddress address, ReadOnlyMemory<byte> body)
        => await _channel.BasicPublishAsync(address, _properties, body).ConfigureAwait(false);

    public void Dispose()
    {
        try
        {
            // Disposing channel and connection objects is not enough,
            // they must be explicitly closed with the API methods.
            _channel.CloseAsync();
        }
        catch
        {
            // ignored
        }

        _channel.Dispose();
    }
}
