using System;
using Serilog.Configuration;
using Serilog.Formatting;
using Serilog.Sinks.RabbitMQ;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;

namespace Serilog
{
    /// <summary>
    /// Extension method to configure Serilog with a Sink for RabbitMq
    /// </summary>
    public static class LoggerConfigurationRabbitMqExtension
    {
        /// <summary>
        /// Adds a sink that lets you push log messages to RabbitMq
        /// </summary>
        /// <param name="loggerConfiguration"></param>
        /// <param name="rabbitMqConfiguration">Mandatory RabbitMq configuration</param>
        /// <param name="formatProvider">Optional formatProvider. If not specified, then default is 'null'</param>
        /// <returns></returns>
        public static LoggerConfiguration RabbitMq(
            this LoggerSinkConfiguration loggerConfiguration,
            RabbitMQConfiguration rabbitMqConfiguration,
            ITextFormatter formatter,
            IFormatProvider formatProvider = null)
        {
            if (loggerConfiguration == null) throw new ArgumentNullException("loggerConfiguration");
            return
                loggerConfiguration
                    .Sink(new RabbitMQSink(rabbitMqConfiguration, formatter, formatProvider));
        }
    }
}
