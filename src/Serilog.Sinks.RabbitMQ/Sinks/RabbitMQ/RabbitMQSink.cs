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

using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.PeriodicBatching;

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// Serilog RabbitMq Sink - Lets you log to RabbitMq using Serilog
    /// </summary>
    public class RabbitMQSink : IBatchedLogEventSink, IDisposable
    {
        private readonly ITextFormatter _formatter;
        private readonly RabbitMQClient _client;

        private bool _disposedValue;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="rabbitMQSinkConfiguration"></param>
        public RabbitMQSink(RabbitMQClientConfiguration configuration,
            RabbitMQSinkConfiguration rabbitMQSinkConfiguration)
        {
            _formatter = rabbitMQSinkConfiguration.TextFormatter;
            _client = new RabbitMQClient(configuration);
        }


        /// <inheritdoc cref="EmitBatchAsync" />
        public async Task EmitBatchAsync(IEnumerable<LogEvent> batch)
        {
            foreach (var logEvent in batch)
            {
                var sw = new StringWriter();
                _formatter.Format(logEvent, sw);
                await _client.PublishAsync(sw.ToString());
            }
        }

        /// <inheritdoc cref="OnEmptyBatchAsync" />
        public Task OnEmptyBatchAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the Serilog.Sinks.RabbitMQSink and optionally
        /// releases the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue) return;

            if (disposing)
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

            _disposedValue = true;
        }
    }
}
