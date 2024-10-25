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

using Serilog.Events;

namespace Serilog.Sinks.RabbitMQ;

/// <inheritdoc />
internal sealed class DefaultSendMessageEvents : ISendMessageEvents
{
    private readonly string? _routingKey;

    /// <summary>
    /// The constructor for the DefaultSendMessageEvents class.
    /// </summary>
    /// <param name="routingKey">The routing key to use.</param>
    public DefaultSendMessageEvents(string? routingKey)
    {
        _routingKey = routingKey;
    }

    /// <inheritdoc />
    public Func<LogEvent, IDictionary<string, object?>> OnGetHeaderProperties => _ => new Dictionary<string, object?>();

    /// <inheritdoc />
    public Func<LogEvent, string?> OnGetRoutingKey => _ => _routingKey;
}
