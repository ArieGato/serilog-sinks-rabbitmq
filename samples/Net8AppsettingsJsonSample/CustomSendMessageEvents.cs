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
using Serilog.Sinks.RabbitMQ;

namespace Net8AppsettingsJsonSample;

/// <summary>
/// A custom SendMessageEvents class to handle events before sending a message.
/// </summary>
public sealed class CustomSendMessageEvents : ISendMessageEvents
{
    private readonly string _defaultRoutingKey;

    /// <summary>
    /// The constructor for the CustomSendMessageEvents class.
    /// </summary>
    /// <param name="defaultRoutingKey">The default routing key.</param>
    public CustomSendMessageEvents(string defaultRoutingKey)
    {
        _defaultRoutingKey = defaultRoutingKey;
    }

    /// <inheritdoc />
    public Func<LogEvent, IDictionary<string, object?>> OnGetHeaderProperties => @event =>
    {
        @event.Properties.TryGetValue("messageType", out var messageType);
        return new Dictionary<string, object?>
        {
            { "messageType", messageType?.ToString() },
            { "log-level", @event.Level.ToString() },
        };
    };

    /// <inheritdoc />
    public Func<LogEvent, string> OnGetRoutingKey => @event =>
    {
        // example of routing based on log level
        return @event.Level switch
        {
            LogEventLevel.Error => "error",
            _ => _defaultRoutingKey
        };
    };
}
