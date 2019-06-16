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
            RabbitMQClientConfiguration clientConfiguration = new RabbitMQClientConfiguration();
            RabbitMQSinkConfiguration sinkConfiguration = new RabbitMQSinkConfiguration();
            configure(clientConfiguration, sinkConfiguration);

            return RegisterSink(loggerConfiguration, clientConfiguration, sinkConfiguration);
        }

        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            RabbitMQClientConfiguration clientConfiguration, RabbitMQSinkConfiguration sinkConfiguration)
        {
            return RegisterSink(loggerConfiguration, clientConfiguration, sinkConfiguration);
        }

        static LoggerConfiguration RegisterSink(LoggerSinkConfiguration loggerConfiguration, RabbitMQClientConfiguration clientConfiguration, RabbitMQSinkConfiguration sinkConfiguration)
        {
            // guards
            if (loggerConfiguration == null) throw new ArgumentNullException("loggerConfiguration");
            if (clientConfiguration.Hostnames.Count == 0) throw new ArgumentException("hostnames cannot be empty, specify at least one hostname", "hostnames");
            if (string.IsNullOrEmpty(clientConfiguration.Username)) throw new ArgumentException("username cannot be 'null' or and empty string.");
            if (clientConfiguration.Password == null) throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
            if (clientConfiguration.Port <= 0 || clientConfiguration.Port > 65535) throw new ArgumentOutOfRangeException("port", "port must be in a valid range (1 and 65535)");

            sinkConfiguration.BatchPostingLimit = (sinkConfiguration.BatchPostingLimit == default(int)) ? DefaultBatchPostingLimit : sinkConfiguration.BatchPostingLimit;
            sinkConfiguration.Period = (sinkConfiguration.Period == default(TimeSpan)) ? DefaultPeriod : sinkConfiguration.Period;

            return
                loggerConfiguration
                    .Sink(new RabbitMQSink(clientConfiguration, sinkConfiguration), sinkConfiguration.RestrictedToMinimumLevel);
        }
    }

}
