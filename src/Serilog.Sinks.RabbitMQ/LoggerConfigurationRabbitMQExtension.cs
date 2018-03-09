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
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>
        /// Adds a sink that lets you push log messages to RabbitMq
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            RabbitMQConfiguration rabbitMqConfiguration,
            ITextFormatter formatter,
            IFormatProvider formatProvider = null)
        {
            if (loggerConfiguration == null) throw new ArgumentNullException("loggerConfiguration");
            if (rabbitMqConfiguration == null) throw new ArgumentNullException("rabbitMqConfiguration");

            // calls overloaded extension method
            return loggerConfiguration.RabbitMQ(
                rabbitMqConfiguration.Hostname,
                rabbitMqConfiguration.Username,
                rabbitMqConfiguration.Password,
                rabbitMqConfiguration.Exchange,
                rabbitMqConfiguration.ExchangeType,
                rabbitMqConfiguration.DeliveryMode,
                rabbitMqConfiguration.RouteKey,
                rabbitMqConfiguration.Port,
                rabbitMqConfiguration.VHost,
                rabbitMqConfiguration.Heartbeat,
                rabbitMqConfiguration.Protocol,
                rabbitMqConfiguration.BatchPostingLimit,
                rabbitMqConfiguration.Period,
                formatter,
                formatProvider);
        }
        
        /// <summary>
        /// Configures Serilog logger configuration with RabbitMQ
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            string hostname,
            string username,
            string password,
            string exchange,
            string exchangeType,
            RabbitMQDeliveryMode deliveryMode,
            string routeKey,
            int port,
            string vHost,
            ushort heartbeat,
            IProtocol protocol,
            int batchPostingLimit,
            TimeSpan period,
            ITextFormatter formatter,
            IFormatProvider formatProvider = null)
        {
            // guards
            if (loggerConfiguration == null) throw new ArgumentNullException("loggerConfiguration");
            if (string.IsNullOrEmpty(hostname)) throw new ArgumentException("hostname cannot be 'null'. Enter a valid hostname.");
            if (string.IsNullOrEmpty(username)) throw new ArgumentException("username cannot be 'null' or and empty string.");
            if (password == null) throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException("port", "port must be in a valid range (1 and 65535)");

            // setup configuration
            var config = new RabbitMQConfiguration
            {
                Hostname = hostname,
                Username = username,
                Password = password,
                Exchange = exchange ?? string.Empty,
                ExchangeType = exchangeType ?? string.Empty,
                DeliveryMode = deliveryMode,
                RouteKey = routeKey ?? string.Empty,
                Port = port,
                VHost = vHost ?? string.Empty,
                Protocol = protocol ?? Protocols.DefaultProtocol,
                Heartbeat = heartbeat,
                BatchPostingLimit = batchPostingLimit == default(int) ? DefaultBatchPostingLimit : batchPostingLimit,
                Period = period == default(TimeSpan) ? DefaultPeriod : period
            };
            
            return
                loggerConfiguration
                    .Sink(new RabbitMQSink(config, formatter, formatProvider));
        }

        /// <summary>
        /// Configures Serilog logger configuration with RabbitMQ
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            IList<string> hostnames,
            string username,
            string password,
            string exchange,
            string exchangeType,
            RabbitMQDeliveryMode deliveryMode,
            string routeKey,
            int port,
            string vHost,
            ushort heartbeat,
            IProtocol protocol,
            int batchPostingLimit,
            TimeSpan period,
            ITextFormatter formatter,
            IFormatProvider formatProvider = null)
        {
            // guards
            if (loggerConfiguration == null) throw new ArgumentNullException("loggerConfiguration");
            if (hostnames == null) throw new ArgumentNullException("hostnames", "hostnames cannot be 'null'. Enter valid hostnames.");
            if (hostnames.Count == 0) throw new ArgumentException("hostnames cannot be empty, specify at least one hostname", "hostnames");
            if (string.IsNullOrEmpty(username)) throw new ArgumentException("username cannot be 'null' or and empty string.");
            if (password == null) throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException("port", "port must be in a valid range (1 and 65535)");

            // setup configuration
            var config = new RabbitMQConfiguration
            {
                Hostnames = hostnames,
                Username = username,
                Password = password,
                Exchange = exchange ?? string.Empty,
                ExchangeType = exchangeType ?? string.Empty,
                DeliveryMode = deliveryMode,
                RouteKey = routeKey ?? string.Empty,
                Port = port,
                VHost = vHost ?? string.Empty,
                Protocol = protocol ?? Protocols.DefaultProtocol,
                Heartbeat = heartbeat,
                BatchPostingLimit = batchPostingLimit == default(int) ? DefaultBatchPostingLimit : batchPostingLimit,
                Period = period == default(TimeSpan) ? DefaultPeriod : period
            };

            return
                loggerConfiguration
                    .Sink(new RabbitMQSink(config, formatter, formatProvider));
        }

        /// <summary>
        /// Configures Serilog logger configuration with RabbitMQ
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            IEnumerable<string> hostnames,
            string username,
            string password,
            string exchange,
            string exchangeType,
            RabbitMQDeliveryMode deliveryMode,
            string routeKey,
            int port,
            string vHost,
            ushort heartbeat,
            IProtocol protocol,
            int batchPostingLimit,
            TimeSpan period,
            ITextFormatter formatter,
            IFormatProvider formatProvider = null)
        {
            return loggerConfiguration.RabbitMQ(
                hostnames.ToList(),
                username,
                password,
                exchange,
                exchangeType,
                deliveryMode,
                routeKey,
                port,
                vHost,
                heartbeat,
                protocol,
                batchPostingLimit,
                period,
                formatter,
                formatProvider);
        }

        private const int DefaultBatchPostingLimit = 50;
        private static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2);
    }
}
