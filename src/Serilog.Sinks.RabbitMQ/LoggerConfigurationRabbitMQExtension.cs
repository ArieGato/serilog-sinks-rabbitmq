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
using RabbitMQ.Client;
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

        /// <summary>
        /// Adds a sink that lets you push log messages to RabbitMQ
        /// Will be used when configuring via configuration file
        /// If you need to overrule the text formatter, you will need to supply it here as a separate parameter instead of supplying it via the RabbitMQSinkConfiguration instance
        /// which will not work when configuring via configuration file
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            RabbitMQClientConfiguration clientConfiguration, RabbitMQSinkConfiguration sinkConfiguration, ITextFormatter textFormatter = null)
        {
            if (textFormatter != null) sinkConfiguration.TextFormatter = textFormatter;
            return RegisterSink(loggerConfiguration, clientConfiguration, sinkConfiguration);
        }

        /// <summary>
        /// Adds a sink that lets you push log messages to RabbitMQ
        /// Will be used when configuring via configuration file
        /// Is for backward-compatibility with previous version
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            string hostname, string username, string password, string exchange = null, string exchangeType = null,
            RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable, string routeKey = null, int port = 0,
            string vHost = null, ushort heartbeat = 0, IProtocol protocol = null, int batchPostingLimit = 0,
            TimeSpan period = default(TimeSpan), ITextFormatter formatter = null, IFormatProvider formatProvider = null,  SslOption sslOption = null
        )
        {
            return loggerConfiguration.RabbitMQ(new string[] {hostname}, username, password, exchange, exchangeType,
                deliveryMode, routeKey, port, vHost, heartbeat, protocol, batchPostingLimit, period, formatter, sslOption);
        }

        /// <summary>
        /// Adds a sink that lets you push log messages to RabbitMQ
        /// Will be used when configuring via configuration file
        /// Is for backward-compatibility with previous version but gives possibility to use multiple hosts
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            string[] hostnames, string username, string password, string exchange = null, string exchangeType = null,
            RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable, string routeKey = null, int port = 0,
            string vHost = null, ushort heartbeat = 0, IProtocol protocol = null, int batchPostingLimit = 0,
            TimeSpan period = default, ITextFormatter formatter = null,  SslOption sslOption = null
        )
        {
            RabbitMQClientConfiguration clientConfiguration = new RabbitMQClientConfiguration
            {
                Username = username,
                Password = password,
                Exchange = exchange,
                ExchangeType = exchangeType,
                DeliveryMode = deliveryMode,
                RouteKey = routeKey,
                Port = port,
                VHost = vHost,
                Heartbeat = heartbeat,
                Protocol = protocol,
                SslOption = sslOption == null ? null : new SslOption { ServerName = sslOption.ServerName, Enabled = sslOption.Enabled, Version = sslOption.Version }
            };
            foreach (string hostname in hostnames)
            {
                clientConfiguration.Hostnames.Add(hostname);
            }

            RabbitMQSinkConfiguration sinkConfiguration = new RabbitMQSinkConfiguration
            {
                BatchPostingLimit = batchPostingLimit, Period = period, TextFormatter = formatter
            };

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
