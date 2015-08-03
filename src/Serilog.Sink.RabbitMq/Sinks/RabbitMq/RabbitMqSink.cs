using System;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
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
        private readonly ITextFormatter _formatter;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="configuration">Mandatory RabbitMqConfiguration</param>
        /// <param name="formatter">ITextFormatter - for instance JsonFormatter</param>
        /// <param name="formatProvider">Optional formatProvider (null is allowed)</param>
        public RabbitMqSink(RabbitMqConfiguration configuration, ITextFormatter formatter, IFormatProvider formatProvider)
        {
            _client = new RabbitMqClient(configuration);
            _formatProvider = formatProvider;
            _formatter = formatter;
        }

        /// <summary>
        /// Emit method, used by Serilog to send log events to RabbitMq, using this sink
        /// </summary>
        /// <param name="logEvent"></param>
        public void Emit(LogEvent logEvent)
        {
            var writer = new StringWriter();
            _formatter.Format(logEvent, writer);
            _client.Publish(writer.ToString());
        }
    }
}
