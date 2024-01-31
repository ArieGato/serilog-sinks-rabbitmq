// Copyright 2015-2022 Serilog Contributors
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

using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// Serilog RabbitMq Sink - Lets you log to RabbitMq using Serilog audit logic.
    /// </summary>
    public sealed class RabbitMQAuditSink : ILogEventSink, IDisposable
    {
        private readonly ITextFormatter _formatter;
        private readonly IRabbitMQClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="RabbitMQAuditSink" /> class.
        /// </summary>
        /// <param name="configuration">The client configuration.</param>
        /// <param name="rabbitMQSinkConfiguration">The Sink configuration.</param>
        public RabbitMQAuditSink(RabbitMQClientConfiguration configuration,
            RabbitMQSinkConfiguration rabbitMQSinkConfiguration)
        {
            _formatter = rabbitMQSinkConfiguration.TextFormatter;
            _client = new RabbitMQClient(configuration);
        }

        internal RabbitMQAuditSink(IRabbitMQClient client, ITextFormatter formatter)
        {
            _client = client;
            _formatter = formatter;
        }

        /// <summary>
        /// Emit the provided log event to the sink.
        /// </summary>
        /// <param name="logEvent">The log event to write.</param>
        public void Emit(LogEvent logEvent)
        {
            var sw = new StringWriter();
            _formatter.Format(logEvent, sw);
            _client.PublishAsync(sw.ToString()).Wait();
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            try
            {
                // Disposing channel and connection objects is not enough, they must be explicitly closed with the API methods.
                // https://www.rabbitmq.com/dotnet-api-guide.html#disconnecting
                _client.Close();
            }
            catch
            {
                // ignore exceptions
            }

            _client.Dispose();
        }
    }
}