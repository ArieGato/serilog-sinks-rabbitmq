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

using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.PeriodicBatching;

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// Serilog RabbitMq Sink - Lets you log to RabbitMq using Serilog
    /// </summary>
    public sealed class RabbitMQSink : IBatchedLogEventSink, IDisposable
    {
        private readonly ITextFormatter _formatter;
        private readonly IRabbitMQClient _client;

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

        /// <summary>
        /// Constructor for testing purposes
        /// </summary>
        /// <param name="client"></param>
        /// <param name="textFormatter"></param>
        internal RabbitMQSink(IRabbitMQClient client, ITextFormatter textFormatter)
        {
            _formatter = textFormatter;
            _client = client;
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
            if (_disposedValue) return;

            try
            {
                // Disposing channel and connection objects is not enough, they must be explicitly closed with the API methods.
                // https://www.rabbitmq.com/dotnet-api-guide.html#disconnecting
                _client.Close();
            }
            catch (Exception exception)
            {
                // ignored
                SelfLog.WriteLine("Exception occurred closing RabbitMQClient {0}", exception.Message);
            }

            _client.Dispose();

            _disposedValue = true;
        }
    }
}
