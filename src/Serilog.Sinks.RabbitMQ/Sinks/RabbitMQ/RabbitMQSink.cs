﻿// Copyright 2015 Serilog Contributors
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
using System.Collections.Generic;
using System.IO;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Serilog.Sinks.PeriodicBatching;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;

namespace Serilog.Sinks.RabbitMQ
{
    /// <summary>
    /// Serilog RabbitMq Sink - Lets you log to RabbitMq using Serilog
    /// </summary>
    public class RabbitMQSink : PeriodicBatchingSink
    {
        private readonly ITextFormatter _formatter;
        private readonly IFormatProvider _formatProvider;
        private readonly RabbitMQClient _client;

        public RabbitMQSink(RabbitMQConfiguration configuration,
            ITextFormatter formatter,
            IFormatProvider formatProvider) : base(configuration.BatchPostingLimit, configuration.Period)
        {
            _formatter = formatter ?? new CompactJsonFormatter();
            _formatProvider = formatProvider;
            _client = new RabbitMQClient(configuration);
        }

        protected override void EmitBatch(IEnumerable<LogEvent> events)
        {
            foreach (var logEvent in events)
            {
                using (var sw = new StringWriter())
                {
                    _formatter.Format(logEvent, sw);
                    _client.Publish(sw.ToString());
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _client.Dispose();
        }
    }
}
