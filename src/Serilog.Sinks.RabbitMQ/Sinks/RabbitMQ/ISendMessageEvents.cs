using RabbitMQ.Client;
using Serilog.Events;

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// Holds events that customize sent messages.
/// </summary>
public interface ISendMessageEvents
{
    /// <summary>
    /// Get routing key for the message.
    /// </summary>
    /// <param name="logEvent">The log event.</param>
    /// <param name="defaultRoutingKey">The default routing key from configuration.</param>
    /// <returns>The routing key.</returns>
    string OnGetRoutingKey(LogEvent logEvent, string defaultRoutingKey);

    /// <summary>
    /// Set message properties before publish.
    /// </summary>
    /// <param name="logEvent">The log event.</param>
    /// <param name="properties">The message properties.</param>
    void OnSetMessageProperties(LogEvent logEvent, IBasicProperties properties);
}
