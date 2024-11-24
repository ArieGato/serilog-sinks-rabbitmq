using RabbitMQ.Client;
using Serilog.Events;

namespace Serilog.Sinks.RabbitMQ;

/// <summary>
/// A class to handle events before sending a message.
/// </summary>
public class SendMessageEvents
{
    /// <summary>
    /// Configuration for RabbitMQ client.
    /// </summary>
    protected RabbitMQClientConfiguration Configuration { get; private set; } = default!;

    internal void Initialize(RabbitMQClientConfiguration configuration) => Configuration = configuration.Clone();

    /// <summary>
    /// Get routing key for the message.
    /// </summary>
    public virtual Func<LogEvent, string> OnGetRoutingKey => _ => Configuration.RoutingKey;

    /// <summary>
    /// Set message properties before publish.
    /// </summary>
    public virtual Action<LogEvent, IBasicProperties> OnSetMessageProperties =>
        (_, properties) => properties.Persistent = Configuration.DeliveryMode == RabbitMQDeliveryMode.Durable;
}
