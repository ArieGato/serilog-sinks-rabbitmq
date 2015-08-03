using System;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.RabbitMq.Sinks.RabbitMq;

namespace Serilog.Sinks.RabbitMq
{
    /// <summary>
    /// Serilog RabbitMq Sink - Lets you log to RabbitMq using Serilog
    /// </summary>
    public class RabbitMqSink : ILogEventSink
    {
        readonly IFormatProvider _formatProvider;
        private readonly RabbitMqClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="configuration">Mandatory RabbitMqConfiguration</param>
        /// <param name="formatProvider">Optional formatProvider (null is allowed)</param>
        public RabbitMqSink(RabbitMqConfiguration configuration, IFormatProvider formatProvider)
        {
            _client = new RabbitMqClient(configuration);
            _formatProvider = formatProvider;
        }

        /// <summary>
        /// Emit method, used by Serilog to send log events to RabbitMq, using this sink
        /// </summary>
        /// <param name="logEvent"></param>
        public void Emit(LogEvent logEvent)
        {
            var message = new RabbitMqMessage(logEvent.RenderMessage(_formatProvider));

            _client.Publish(message.ToString());
        }
    }
}
