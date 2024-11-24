using RabbitMQ.Client;
using Serilog.Events;

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// A class to handle events before sending a message.
/// </summary>
internal sealed class SendMessageEvents : ISendMessageEvents
{
    /// <inheritdoc cref="ISendMessageEvents.OnGetRoutingKey"/>
    public string OnGetRoutingKey(LogEvent logEvent, string defaultRoutingKey) => defaultRoutingKey;

    /// <inheritdoc cref="ISendMessageEvents.OnSetMessageProperties"/>
    public void OnSetMessageProperties(LogEvent logEvent, IBasicProperties properties)
    {
    }
}
