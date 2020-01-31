// Copyright 2015 Serilog Contributors
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

using System.IO;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;
using Serilog.Sinks.PeriodicBatching;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// Builds a routing key for a <see cref="LogEvent"/>.
    /// </summary>
    /// <param name="logEvent">The log event to build the routing key for.</param>
    public delegate string BuildRoutingKey(LogEvent logEvent);

    /// <summary>
    /// Serilog RabbitMq Sink - Lets you log to RabbitMq using Serilog
    /// </summary>
    public class RabbitMQSink : PeriodicBatchingSink
    {
        private readonly ITextFormatter _formatter;
        private readonly RabbitMQClient _client;
        private readonly BuildRoutingKey _buildRoutingKey;

        public RabbitMQSink(RabbitMQClientConfiguration configuration,
            RabbitMQSinkConfiguration rabbitMQSinkConfiguration) : base(rabbitMQSinkConfiguration.BatchPostingLimit, rabbitMQSinkConfiguration.Period)
        {
            _formatter = rabbitMQSinkConfiguration.TextFormatter;
            _client = new RabbitMQClient(configuration);
            _buildRoutingKey = rabbitMQSinkConfiguration.BuildRoutingKey;
        }

        protected override async Task EmitBatchAsync(IEnumerable<LogEvent> events)
        {
            foreach (var logEvent in events)
            {
                var routingKey = _buildRoutingKey?.Invoke(logEvent);

                var sw = new StringWriter();
                _formatter.Format(logEvent, sw);
                await _client.PublishAsync(sw.ToString(), routingKey);
            }
        }

        protected override void Dispose(bool disposing)
        {
            // base.Dispose must be called first, because it flushes all pending EmitBatchAsync.
            // Closing the client first would have resulted in an infinite retry loop to flush.
            base.Dispose(disposing);

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
