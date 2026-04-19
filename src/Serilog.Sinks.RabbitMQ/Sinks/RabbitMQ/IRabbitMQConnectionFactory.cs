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
/// The RabbitMQ connection factory interface.
/// </summary>
internal interface IRabbitMQConnectionFactory : IAsyncDisposable
{
    /// <summary>
    /// Returns the cached <see cref="IConnection"/>, creating it on the first call.
    /// Subsequent calls return the same instance. Creation is gated internally so
    /// concurrent callers see a single connection.
    /// </summary>
    /// <returns>The shared, lazily-created connection.</returns>
    Task<IConnection> GetConnectionAsync();

    /// <summary>
    /// Close the underlying connection.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task CloseAsync();
}
