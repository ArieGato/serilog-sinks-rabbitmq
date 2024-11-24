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

using RabbitMQ.Client;

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// The RabbitMQ Channel interface.
/// </summary>
internal interface IRabbitMQChannel : IDisposable
{
    /// <summary>
    /// Returns <see langword="true"/> when the channel is open.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Publishes a message to RabbitMQ Exchange.
    /// </summary>
    /// <param name="address"><see cref="PublicationAddress"/>.</param>
    /// <param name="basicProperties">The message properties.</param>
    /// <param name="body">Message body.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask BasicPublishAsync(PublicationAddress address, BasicProperties basicProperties, ReadOnlyMemory<byte> body);
}
