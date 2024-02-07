using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.RabbitMQ.Tests
{
    internal class LogEventBuilder
    {
        private DateTimeOffset _timestamp;
        private LogEventLevel _level;
        private Exception? _exception;
        private List<MessageTemplateToken> _messageTemplateTokens;
        private List<LogEventProperty> _properties;
        private string _message;

        private LogEventBuilder()
        {
            _timestamp = DateTimeOffset.Now;
            _level = LogEventLevel.Information;
            _messageTemplateTokens = [];
            _properties = [];
            _message = "some-message";
        }

        public LogEventBuilder WithTimestamp(DateTimeOffset timestamp)
        {
            _timestamp = timestamp;
            return this;
        }

        public LogEventBuilder WithLevel(LogEventLevel level)
        {
            _level = level;
            return this;
        }

        public LogEventBuilder WithException(Exception exception)
        {
            _exception = exception;
            return this;
        }

        public LogEventBuilder WithMessage(string message)
        {
            _message = message;
            return this;
        }

        public LogEventBuilder WithProperty(LogEventProperty property)
        {
            _properties.Add(property);
            return this;
        }

        public static LogEventBuilder Create()
        {
            return new LogEventBuilder();
        }

        public LogEvent Build()
        {
            return new LogEvent(
                _timestamp,
                _level,
                _exception,
                new MessageTemplate(_message, _messageTemplateTokens),
                _properties);
        }
    }
}
