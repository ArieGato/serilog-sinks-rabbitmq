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
            IFormatProvider formatProvider = null,
            bool autoCreateExchange = false)
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
            bool autoCreateExchange = false)
        {
            // guards
            if (loggerConfiguration == null) throw new ArgumentNullException("loggerConfiguration");
            if (string.IsNullOrEmpty(hostname)) throw new ArgumentException("hostname cannot be 'null'. Enter a valid hostname.");
            if (string.IsNullOrEmpty(username)) throw new ArgumentException("username cannot be 'null' or and empty string.");
            if (password == null) throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException("port", "port must be in a valid range (1 and 65535 or 0 for default)");

            var hostnames = ApplySystemConfiguration.ParseHostName(hostname);

            // setup configuration
            var config = new RabbitMQConfiguration
            {
                Hostnames = hostnames,
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
                BatchPostingLimit = batchPostingLimit == default(int) ? DefaultBatchPostingLimit : batchPostingLimit,
                Period = period == default(TimeSpan) ? DefaultPeriod : period 
            };
            
            return
                loggerConfiguration
                    .Sink(new RabbitMQSink(config, formatter, formatProvider, autoCreateExchange));
        }

         /// <summary>
        /// Configures Serilog logger configuration with RabbitMQ using AMQP URIs
        /// </summary>
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
            IFormatProvider formatProvider = null,
            bool autoCreateExchange = false)
        {
            // guards
            if (loggerConfiguration == null) throw new ArgumentNullException(nameof(loggerConfiguration));
            if (string.IsNullOrEmpty(amqpUri)) throw new ArgumentException("amqpUri cannot be 'null'. Enter a valid uri.");

#if NET_FX 
            amqpUri = ApplySystemConfiguration.GetUri(amqpUri);
#endif

            // setup configuration
            var config = new RabbitMQConfiguration {
                AmqpUri = amqpUri,
                Exchange = exchange ?? string.Empty,
                ExchangeType = exchangeType ?? ExchangeType.Fanout,
                DeliveryMode = deliveryMode,
                RouteKey = routeKey ?? string.Empty,
                Protocol = protocol ?? Protocols.DefaultProtocol,
                Heartbeat = heartbeat,
                BatchPostingLimit = batchPostingLimit == default(int) ? DefaultBatchPostingLimit : batchPostingLimit,
                Period = period == default(TimeSpan) ? DefaultPeriod : period,
            };

            return
                loggerConfiguration
                    .Sink(new RabbitMQSink(config, formatter, formatProvider, autoCreateExchange));
        }

        /// <summary>
        /// Configures Serilog audit logger configuration with RabbitMQ
        /// </summary>
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
            IFormatProvider formatProvider = null,
            bool autoCreateExchange = false)
        {
            // guards
            if (loggerAuditSinkConfiguration == null) throw new ArgumentNullException("loggerAuditSinkConfiguration");
            if (string.IsNullOrEmpty(hostname)) throw new ArgumentException("hostname cannot be 'null'. Enter a valid hostname.");
            if (string.IsNullOrEmpty(username)) throw new ArgumentException("username cannot be 'null' or and empty string.");
            if (password == null) throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException("port", "port must be in a valid range (1 and 65535 or 0 for default)");

            var hostnames = ApplySystemConfiguration.ParseHostName(hostname);

            // setup configuration
            var config = new RabbitMQConfiguration {
                Hostnames = hostnames,
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

            return
                loggerAuditSinkConfiguration
                    .Sink(new RabbitMQAuditSink(config, formatter, formatProvider, autoCreateExchange));
        }

        /// <summary>
        /// Configures Serilog audit logger configuration with RabbitMQ using AMQP URIs
        /// </summary>
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
            IFormatProvider formatProvider = null,
            bool autoCreateExchange = false)
        {
            // guards
            if (loggerAuditSinkConfiguration == null) throw new ArgumentNullException("loggerAuditSinkConfiguration");
            if (string.IsNullOrEmpty(amqpUri)) throw new ArgumentException("amqpUri cannot be 'null'. Enter a valid uri.");

#if NET_FX
            amqpUri = ApplySystemConfiguration.GetUri(amqpUri);
#endif

            // setup configuration
            var config = new RabbitMQConfiguration {
                AmqpUri = amqpUri,
                Exchange = exchange ?? string.Empty,
                ExchangeType = exchangeType ?? ExchangeType.Fanout,
                DeliveryMode = deliveryMode,
                RouteKey = routeKey ?? string.Empty,
                Protocol = protocol ?? Protocols.DefaultProtocol,
                Heartbeat = heartbeat,
            };

            return
                loggerAuditSinkConfiguration
                    .Sink(new RabbitMQAuditSink(config, formatter, formatProvider, autoCreateExchange));
        }

        private const int DefaultBatchPostingLimit = 50;
        private static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2);
    }
}
