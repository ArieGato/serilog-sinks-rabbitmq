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
/// The RabbitMQ connection factory interface
/// </summary>
public interface IRabbitMQConnectionFactory : IDisposable
{
    /// <summary>
    /// Returns the connection. Creates a new connection if none exists.
    /// </summary>
    /// <returns></returns>
    IConnection GetConnection();

    /// <summary>
    /// Close the underlying connection
    /// </summary>
    void Close();
}
