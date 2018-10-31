using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Raw;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;
using System;
using System.IO;

namespace Serilog.Sinks.RabbitMQ
{
    // <summary>
    /// Serilog RabbitMq Sink - Lets you log to RabbitMq using Serilog audit logic
    /// </summary>
    public class RabbitMQAuditSink : ILogEventSink, IDisposable {
        private readonly ITextFormatter _formatter;
        private readonly IFormatProvider _formatProvider;
        private readonly RabbitMQClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="RabbitMQAuditSink"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="formatter">The formatter, or null.</param>
        /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
        public RabbitMQAuditSink(RabbitMQConfiguration configuration, ITextFormatter formatter, IFormatProvider formatProvider) {
            _formatter = formatter ?? new RawFormatter();
            _formatProvider = formatProvider;
            _client = new RabbitMQClient(configuration);
        }

        /// <summary>
        /// Emit the provided log event to the sink.
        /// </summary>
        /// <param name="logEvent">The log event to write.</param>
        public void Emit(LogEvent logEvent) {
            try {
                var sw = new StringWriter();
                _formatter.Format(logEvent, sw);
                _client.Publish(sw.ToString());
            }
            catch (Exception ex) {
                SelfLog.WriteLine("Unable to write log event to the RabbitMQ due to following error: {1}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing) {
            if (disposing) {
                _client.Dispose();
            }
        }
    }
}
