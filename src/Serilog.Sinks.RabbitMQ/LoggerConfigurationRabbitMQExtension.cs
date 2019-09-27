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

        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            RabbitMQClientConfiguration clientConfiguration, RabbitMQSinkConfiguration sinkConfiguration)
        {
            return RegisterSink(loggerConfiguration, clientConfiguration, sinkConfiguration);
        }

        /// <summary>
        /// Configures Serilog logger configuration with RabbitMQ
        /// </summary>
        /// <remarks>Leaved for compatibility with Serilog.Settings.AppSettings library.</remarks>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            string hostname,
            string username,
            string password,
            string exchange = null,
            string exchangeType = null,
            RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable,
            string routeKey = null,
            int port = 0,
            string vHost = null,
            ushort heartbeat = 0,
            IProtocol protocol = null,
            int batchPostingLimit = 0,
            TimeSpan period = default(TimeSpan),
            ITextFormatter formatter = null,
            IFormatProvider formatProvider = null,
            bool autoCreateExchange = false) {
            // guards
            if (loggerConfiguration == null) throw new ArgumentNullException("loggerConfiguration");
            if (string.IsNullOrEmpty(hostname)) throw new ArgumentException("hostname cannot be 'null'. Enter a valid hostname.");
            if (string.IsNullOrEmpty(username)) throw new ArgumentException("username cannot be 'null' or and empty string.");
            if (password == null) throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException("port", "port must be in a valid range (1 and 65535 or 0 for default)");

            // setup configuration
            var clientConfiguration = new RabbitMQClientConfiguration {
                Username = username,
                Password = password,
                Exchange = exchange ?? string.Empty,
                ExchangeType = exchangeType ?? ExchangeType.Fanout,
                DeliveryMode = deliveryMode,
                RouteKey = routeKey ?? string.Empty,
                Port = port,
                VHost = vHost ?? string.Empty,
                Protocol = protocol ?? Protocols.DefaultProtocol,
                Heartbeat = heartbeat,
            };

            var hostnames = ApplySystemConfiguration.ParseHostName(hostname);
            foreach (var item in hostnames) {
                clientConfiguration.Hostnames.Add(item);
            }

            var sinkConfiguration = new RabbitMQSinkConfiguration {
                BatchPostingLimit = batchPostingLimit == default(int) ? DefaultBatchPostingLimit : batchPostingLimit,
                Period = period == default(TimeSpan) ? DefaultPeriod : period,
                TextFormatter = formatter,
            };

            return
                loggerConfiguration
                    .Sink(new RabbitMQSink(clientConfiguration, sinkConfiguration), sinkConfiguration.RestrictedToMinimumLevel);
        }

        /// <summary>
        /// Configures Serilog logger configuration with RabbitMQ using AMQP URIs
        /// </summary>
        /// <remarks>Leaved for compatibility with Serilog.Settings.AppSettings library.</remarks>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            string amqpUri,
            string exchange = null,
            string exchangeType = null,
            RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable,
            string routeKey = null,
            ushort heartbeat = 0,
            IProtocol protocol = null,
            int batchPostingLimit = 0,
            TimeSpan period = default(TimeSpan),
            ITextFormatter formatter = null,
            bool autoCreateExchange = false) {
            // guards
            if (loggerConfiguration == null) throw new ArgumentNullException(nameof(loggerConfiguration));
            if (string.IsNullOrEmpty(amqpUri)) throw new ArgumentException("amqpUri cannot be 'null'. Enter a valid uri.");

#if NET_FX 
            amqpUri = ApplySystemConfiguration.GetUri(amqpUri);
#endif

            // setup configuration
            var clientConfiguration = new RabbitMQClientConfiguration {
                AmqpUri = new Uri(amqpUri), // TODO: validate
                Exchange = exchange ?? string.Empty,
                ExchangeType = exchangeType ?? ExchangeType.Fanout,
                DeliveryMode = deliveryMode,
                RouteKey = routeKey ?? string.Empty,
                Protocol = protocol ?? Protocols.DefaultProtocol,
                Heartbeat = heartbeat,
                AutoCreateExchange = autoCreateExchange,
            };


            var sinkConfiguration = new RabbitMQSinkConfiguration {
                BatchPostingLimit = batchPostingLimit == default(int) ? DefaultBatchPostingLimit : batchPostingLimit,
                Period = period == default(TimeSpan) ? DefaultPeriod : period,
                TextFormatter = formatter,
            };

            return
                loggerConfiguration
                    .Sink(new RabbitMQSink(clientConfiguration, sinkConfiguration), sinkConfiguration.RestrictedToMinimumLevel);
        }

        /// <summary>
        /// Configures Serilog audit logger configuration with RabbitMQ
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
            Action<RabbitMQClientConfiguration, RabbitMQSinkConfiguration> configure) {
            RabbitMQClientConfiguration clientConfiguration = new RabbitMQClientConfiguration();
            RabbitMQSinkConfiguration sinkConfiguration = new RabbitMQSinkConfiguration();
            configure(clientConfiguration, sinkConfiguration);

            return RegisterAuditSink(loggerAuditSinkConfiguration, clientConfiguration, sinkConfiguration);
        }

        /// <summary>
        /// Configures Serilog audit logger configuration with RabbitMQ using AMQP URIs
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
            RabbitMQClientConfiguration clientConfiguration, RabbitMQSinkConfiguration sinkConfiguration) {

            return RegisterAuditSink(loggerAuditSinkConfiguration, clientConfiguration, sinkConfiguration);
        }


        /// <summary>
        /// Configures Serilog audit logger configuration with RabbitMQ
        /// </summary>
        /// <remarks>Leaved for compatibility with Serilog.Settings.AppSettings library.</remarks>
        public static LoggerConfiguration RabbitMQ(
            this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
            string hostname,
            string username,
            string password,
            string exchange = null,
            string exchangeType = null,
            RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable,
            string routeKey = null,
            int port = 0,
            string vHost = null,
            ushort heartbeat = 0,
            IProtocol protocol = null,
            ITextFormatter formatter = null,
            bool autoCreateExchange = false) {
            // guards
            if (loggerAuditSinkConfiguration == null) throw new ArgumentNullException("loggerAuditSinkConfiguration");
            if (string.IsNullOrEmpty(hostname)) throw new ArgumentException("hostname cannot be 'null'. Enter a valid hostname.");
            if (string.IsNullOrEmpty(username)) throw new ArgumentException("username cannot be 'null' or and empty string.");
            if (password == null) throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException("port", "port must be in a valid range (1 and 65535 or 0 for default)");
            if (formatter == null) throw new ArgumentException("formatter cannot be 'null'. Enter a valid formatter.");

            // setup configuration
            var clientConfiguration = new RabbitMQClientConfiguration {
                Username = username,
                Password = password,
                Exchange = exchange ?? string.Empty,
                ExchangeType = exchangeType ?? ExchangeType.Fanout,
                DeliveryMode = deliveryMode,
                RouteKey = routeKey ?? string.Empty,
                Port = port,
                VHost = vHost ?? string.Empty,
                Protocol = protocol ?? Protocols.DefaultProtocol,
                Heartbeat = heartbeat,
            };

            var hostnames = ApplySystemConfiguration.ParseHostName(hostname);
            foreach(var item in hostnames) {
                clientConfiguration.Hostnames.Add(item);
            }

            var sinkConfiguration = new RabbitMQSinkConfiguration {
                TextFormatter = formatter,
            };

            return
                loggerAuditSinkConfiguration
                    .Sink(new RabbitMQAuditSink(clientConfiguration, sinkConfiguration), sinkConfiguration.RestrictedToMinimumLevel);
        }

        /// <summary>
        /// Configures Serilog audit logger configuration with RabbitMQ using AMQP URIs
        /// </summary>
        /// <remarks>Leaved for compatibility with Serilog.Settings.AppSettings library.</remarks>
        public static LoggerConfiguration RabbitMQ(
            this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
            string amqpUri,
            string exchange = null,
            string exchangeType = null,
            RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable,
            string routeKey = null,
            ushort heartbeat = 0,
            IProtocol protocol = null,
            ITextFormatter formatter = null,
            bool autoCreateExchange = false) {
            // guards
            if (loggerAuditSinkConfiguration == null) throw new ArgumentNullException("loggerAuditSinkConfiguration");
            if (formatter == null) throw new ArgumentException("formatter cannot be 'null'. Enter a valid formatter.");
            if (string.IsNullOrEmpty(amqpUri)) throw new ArgumentException("amqpUri cannot be 'null'. Enter a valid uri.");

#if NET_FX
            amqpUri = ApplySystemConfiguration.GetUri(amqpUri);
#endif

            // setup configuration
            var clientConfiguration = new RabbitMQClientConfiguration {
                AmqpUri = new Uri(amqpUri), // TODO: validate
                Exchange = exchange ?? string.Empty,
                ExchangeType = exchangeType ?? ExchangeType.Fanout,
                DeliveryMode = deliveryMode,
                RouteKey = routeKey ?? string.Empty,
                Protocol = protocol ?? Protocols.DefaultProtocol,
                Heartbeat = heartbeat,
                AutoCreateExchange = autoCreateExchange,
            };

            var sinkConfiguration = new RabbitMQSinkConfiguration {
                TextFormatter = formatter,
            };

            return
                loggerAuditSinkConfiguration
                    .Sink(new RabbitMQAuditSink(clientConfiguration, sinkConfiguration), sinkConfiguration.RestrictedToMinimumLevel);
        }

        static LoggerConfiguration RegisterSink(LoggerSinkConfiguration loggerConfiguration, RabbitMQClientConfiguration clientConfiguration, RabbitMQSinkConfiguration sinkConfiguration) {
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

        static LoggerConfiguration RegisterAuditSink(LoggerAuditSinkConfiguration loggerAuditSinkConfiguration, RabbitMQClientConfiguration clientConfiguration, RabbitMQSinkConfiguration sinkConfiguration) {
            // guards
            if (loggerAuditSinkConfiguration == null) throw new ArgumentNullException("loggerAuditSinkConfiguration");
            if (clientConfiguration.Hostnames.Count == 0) throw new ArgumentException("hostnames cannot be empty, specify at least one hostname", "hostnames");
            if (string.IsNullOrEmpty(clientConfiguration.Username)) throw new ArgumentException("username cannot be 'null' or and empty string.");
            if (clientConfiguration.Password == null) throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
            if (clientConfiguration.Port <= 0 || clientConfiguration.Port > 65535) throw new ArgumentOutOfRangeException("port", "port must be in a valid range (1 and 65535)");

            return
                loggerAuditSinkConfiguration
                    .Sink(new RabbitMQAuditSink(clientConfiguration, sinkConfiguration), sinkConfiguration.RestrictedToMinimumLevel);
        }
    }
}
