// Copyright 2015 Serilog Contributors
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

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// RabbitMQ Client interface
/// </summary>
public interface IRabbitMQClient : IDisposable
{
    /// <summary>
    /// Publishes a message to RabbitMq Exchange
    /// </summary>
    /// <param name="message"></param>
    void Publish(string message);

    /// <summary>
    /// Close the connection and all channels to RabbitMq
    /// </summary>
    /// <exception cref="AggregateException"></exception>
    void Close();
}