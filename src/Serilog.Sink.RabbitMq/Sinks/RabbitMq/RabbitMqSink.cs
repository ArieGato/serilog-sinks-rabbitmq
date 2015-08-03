using System;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// Serilog RabbitMq Sink - Lets you log to RabbitMq using Serilog
    /// </summary>
    public class RabbitMQSink : ILogEventSink
    {
        readonly IFormatProvider _formatProvider;
        private readonly RabbitMQClient _client;
        private readonly ITextFormatter _formatter;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="configuration">Mandatory RabbitMqConfiguration</param>
        /// <param name="formatter">ITextFormatter - for instance JsonFormatter</param>
        /// <param name="formatProvider">Optional formatProvider (null is allowed)</param>
        public RabbitMQSink(RabbitMQConfiguration configuration, ITextFormatter formatter, IFormatProvider formatProvider)
        {
            _client = new RabbitMQClient(configuration);
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
