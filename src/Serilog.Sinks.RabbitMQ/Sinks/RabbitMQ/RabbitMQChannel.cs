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
/// A wrapper class for <see cref="IModel"/> to be used in <see cref="ObjectPool{T}"/>.
/// </summary>
internal sealed class RabbitMQChannel : IRabbitMQChannel
{
    private readonly IBasicProperties _properties;

    /// <summary>
    /// The wrapped <see cref="IModel"/>.
    /// </summary>
    private readonly IModel _model;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQChannel"/> class.
    /// </summary>
    /// <param name="model"></param>
    public RabbitMQChannel(IModel model)
    {
        _model = model;

        _properties = model.CreateBasicProperties();
    }

    public bool IsOpen => _model.IsOpen;

    public void BasicPublish(PublicationAddress address, ReadOnlyMemory<byte> body)
    {
        _model.BasicPublish(address, _properties, body);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            // Disposing channel and connection objects is not enough,
            // they must be explicitly closed with the API methods.
            _model.Close();
        }
        catch
        {
            // ignored
        }

        _model.Dispose();
    }
}
