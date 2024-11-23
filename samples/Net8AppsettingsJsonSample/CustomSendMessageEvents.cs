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
using Serilog.Events;
using Serilog.Sinks.RabbitMQ;

namespace Net8AppsettingsJsonSample;

/// <summary>
/// A custom SendMessageEvents class to handle events before sending a message.
/// </summary>
public sealed class CustomSendMessageEvents : ISendMessageEvents
{
    private readonly string _defaultRoutingKey;
    private bool _isPersistent;

    /// <summary>
    /// The constructor for the CustomSendMessageEvents class.
    /// </summary>
    /// <param name="defaultRoutingKey">The default routing key.</param>
    public CustomSendMessageEvents(string defaultRoutingKey)
    {
        _defaultRoutingKey = defaultRoutingKey;
    }

    /// <inheritdoc />
    public void Initialize(RabbitMQClientConfiguration configuration) =>
        _isPersistent = configuration.DeliveryMode == RabbitMQDeliveryMode.Durable;

    /// <inheritdoc />
    public Action<LogEvent, IBasicProperties> OnSetMessageProperties => (@event, properties) =>
    {
        // example of setting persistent property based on configuration
        properties.Persistent = _isPersistent;

        // example of setting message headers based on log event properties
        @event.Properties.TryGetValue("messageType", out var messageType);
        properties.Headers = new Dictionary<string, object?>
        {
            { "messageType", messageType?.ToString() },
            { "log-level", @event.Level.ToString() },
        };

        // example of setting correlation id based on log event properties
        if (@event.Properties.TryGetValue(LogProperties.CORRELATION_ID, out var correlationId))
        {
            properties.CorrelationId = correlationId.ToString();
        }
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
