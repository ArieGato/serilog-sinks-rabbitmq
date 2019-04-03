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
using Serilog.Configuration;
using Serilog.Sinks.RabbitMQ;
using Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ;

namespace Serilog
{
    /// <summary>
    /// Extension method to configure Serilog with a Sink for RabbitMq
    /// </summary>
    public static class LoggerConfigurationRabbitMqExtension
    {
        private const int DefaultBatchPostingLimit = 50;
        private static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Adds a sink that lets you push log messages to RabbitMQ
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            Action<RabbitMQClientConfiguration, RabbitMQSinkConfiguration> configure)
        {
            RabbitMQClientConfiguration rabbitMQConfiguration = new RabbitMQClientConfiguration();
            RabbitMQSinkConfiguration rabbitMQSinkConfiguration = new RabbitMQSinkConfiguration();
            configure(rabbitMQConfiguration, rabbitMQSinkConfiguration);

            // guards
            if (loggerConfiguration == null) throw new ArgumentNullException("loggerConfiguration");
            if (rabbitMQConfiguration.Hostnames.Count == 0) throw new ArgumentException("hostnames cannot be empty, specify at least one hostname", "hostnames");
            if (string.IsNullOrEmpty(rabbitMQConfiguration.Username)) throw new ArgumentException("username cannot be 'null' or and empty string.");
            if (rabbitMQConfiguration.Password == null) throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
            if (rabbitMQConfiguration.Port <= 0 || rabbitMQConfiguration.Port > 65535) throw new ArgumentOutOfRangeException("port", "port must be in a valid range (1 and 65535)");

            rabbitMQSinkConfiguration.BatchPostingLimit = (rabbitMQSinkConfiguration.BatchPostingLimit == default(int)) ? DefaultBatchPostingLimit : rabbitMQSinkConfiguration.BatchPostingLimit;
            rabbitMQSinkConfiguration.Period = (rabbitMQSinkConfiguration.Period == default(TimeSpan)) ? DefaultPeriod : rabbitMQSinkConfiguration.Period;

            return
                loggerConfiguration
                    .Sink(new RabbitMQSink(rabbitMQConfiguration, rabbitMQSinkConfiguration), rabbitMQSinkConfiguration.RestrictedToMinimumLevel);
        }
    }
}
