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

using System;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Raw;
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

        public RabbitMQSink(
            RabbitMQConfiguration configuration,
            ITextFormatter formatter,
            IFormatProvider formatProvider
       )
        {

            // prepare client
            _client = new RabbitMQClient(configuration);
            _formatProvider = formatProvider;
            _formatter = formatter ?? new RawFormatter();
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
