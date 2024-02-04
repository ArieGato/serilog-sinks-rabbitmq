// Copyright 2015-2022 Serilog Contributors
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

using System.Net.Security;
using System.Security.Authentication;
using RabbitMQ.Client;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.PeriodicBatching;
using Serilog.Sinks.RabbitMQ;

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
            Action<RabbitMQClientConfiguration, RabbitMQSinkConfiguration> configure,
            Action<LoggerSinkConfiguration> failureSinkConfiguration = null)
        {
            var clientConfiguration = new RabbitMQClientConfiguration();
            var sinkConfiguration = new RabbitMQSinkConfiguration();

            configure(clientConfiguration, sinkConfiguration);

            return loggerConfiguration.RegisterSink(clientConfiguration, sinkConfiguration, failureSinkConfiguration);
        }

        /// <summary>
        /// Adds a sink that lets you push log messages to RabbitMQ
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            RabbitMQClientConfiguration clientConfiguration,
            RabbitMQSinkConfiguration sinkConfiguration,
            Action<LoggerSinkConfiguration> failureSinkConfiguration = null)
        {
            return loggerConfiguration.RegisterSink(clientConfiguration, sinkConfiguration, failureSinkConfiguration);
        }
        
        /// <summary>
        /// Configures Serilog logger configuration with RabbitMQ
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerSinkConfiguration loggerConfiguration,
            string[] hostnames,
            string username,
            string password,
            string exchange = null,
            string exchangeType = null,
            RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable,
            string routeKey = null,
            int port = 0,
            string vHost = null,
            ushort heartbeat = 0,
            bool sslEnabled = false,
            string sslServerName = null,
            SslProtocols sslVersion = SslProtocols.None,
            SslPolicyErrors sslAcceptablePolicyErrors = SslPolicyErrors.None,
            bool sslCheckCertificateRevocation = false,
            int batchPostingLimit = 0,
            TimeSpan period = default,
            int? queueLimit = null,
            ITextFormatter formatter = null,
            bool autoCreateExchange = false,
            int maxChannels = RabbitMQClient.DefaultMaxChannelCount,
            LogEventLevel levelSwitch = LogEventLevel.Information,
            EmitEventFailureHandling emitEventFailure = EmitEventFailureHandling.WriteToSelfLog,
            Action<LoggerSinkConfiguration> failureSinkConfiguration = null)
        {
            // setup configuration
            var clientConfiguration = new RabbitMQClientConfiguration
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
                Heartbeat = heartbeat,
                AutoCreateExchange = autoCreateExchange,
                MaxChannels = maxChannels
            };

            if (sslEnabled)
            {
                clientConfiguration.SslOption = new SslOption()
                {
                    Enabled = true,
                    ServerName = sslServerName,
                    Version = sslVersion,
                    AcceptablePolicyErrors = sslAcceptablePolicyErrors,
                    CheckCertificateRevocation = sslCheckCertificateRevocation
                };
            }

            var sinkConfiguration = new RabbitMQSinkConfiguration
            {
                BatchPostingLimit = batchPostingLimit == default ? DefaultBatchPostingLimit : batchPostingLimit,
                Period = period == default ? DefaultPeriod : period,
                QueueLimit = queueLimit,
                EmitEventFailure = emitEventFailure,
                RestrictedToMinimumLevel = levelSwitch
            };
            if (formatter != null)
            {
                sinkConfiguration.TextFormatter = formatter;
            }

            return loggerConfiguration.RegisterSink(clientConfiguration, sinkConfiguration, failureSinkConfiguration);
        }

        /// <summary>
        /// Configures Serilog audit logger configuration with RabbitMQ
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
            Action<RabbitMQClientConfiguration, RabbitMQSinkConfiguration> configure)
        {
            var clientConfiguration = new RabbitMQClientConfiguration();
            var sinkConfiguration = new RabbitMQSinkConfiguration();
            configure(clientConfiguration, sinkConfiguration);

            return loggerAuditSinkConfiguration.RegisterAuditSink(clientConfiguration, sinkConfiguration);
        }

        /// <summary>
        /// Configures Serilog audit logger configuration with RabbitMQ
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
            RabbitMQClientConfiguration clientConfiguration, RabbitMQSinkConfiguration sinkConfiguration)
        {
            return loggerAuditSinkConfiguration.RegisterAuditSink(clientConfiguration, sinkConfiguration);
        }

        /// <summary>
        /// Configures Serilog audit logger configuration with RabbitMQ
        /// </summary>
        public static LoggerConfiguration RabbitMQ(
            this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
            string[] hostnames,
            string username,
            string password,
            string exchange = null,
            string exchangeType = null,
            RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable,
            string routeKey = null,
            int port = 0,
            string vHost = null,
            ushort heartbeat = 0,
            bool sslEnabled = false,
            string sslServerName = null,
            SslProtocols sslVersion = SslProtocols.None,
            SslPolicyErrors sslAcceptablePolicyErrors = SslPolicyErrors.None,
            bool sslCheckCertificateRevocation = false,
            ITextFormatter formatter = null,
            bool autoCreateExchange = false,
            int maxChannels = RabbitMQClient.DefaultMaxChannelCount,
            LogEventLevel levelSwitch = LogEventLevel.Information)
        {
            // setup configuration
            var clientConfiguration = new RabbitMQClientConfiguration
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
                Heartbeat = heartbeat,
                AutoCreateExchange = autoCreateExchange,
                MaxChannels = maxChannels
            };

            if (sslEnabled)
            {
                clientConfiguration.SslOption = new SslOption()
                {
                    Enabled = true,
                    ServerName = sslServerName,
                    Version = sslVersion,
                    AcceptablePolicyErrors = sslAcceptablePolicyErrors,
                    CheckCertificateRevocation = sslCheckCertificateRevocation
                };
            }

            var sinkConfiguration = new RabbitMQSinkConfiguration
            {
                RestrictedToMinimumLevel = levelSwitch
            };
            if (formatter != null)
            {
                sinkConfiguration.TextFormatter = formatter;
            }

            return loggerAuditSinkConfiguration.RegisterAuditSink(clientConfiguration, sinkConfiguration);
        }
        
        private static LoggerConfiguration RegisterSink(
            this LoggerSinkConfiguration loggerSinkConfiguration,
            RabbitMQClientConfiguration clientConfiguration,
            RabbitMQSinkConfiguration sinkConfiguration,
            Action<LoggerSinkConfiguration> failureSinkConfiguration = null)
        {
            // guards
            if (loggerSinkConfiguration == null)
            {
                throw new ArgumentNullException(nameof(loggerSinkConfiguration));
            }

            sinkConfiguration.BatchPostingLimit = (sinkConfiguration.BatchPostingLimit == default)
                ? DefaultBatchPostingLimit
                : sinkConfiguration.BatchPostingLimit;
            sinkConfiguration.Period = (sinkConfiguration.Period == default) ? DefaultPeriod : sinkConfiguration.Period;

            ValidateRabbitMQClientConfiguration(clientConfiguration);

            return loggerSinkConfiguration.Sink(clientConfiguration, sinkConfiguration, failureSinkConfiguration);
        }

        private static LoggerConfiguration RegisterAuditSink(
            this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
            RabbitMQClientConfiguration clientConfiguration,
            RabbitMQSinkConfiguration sinkConfiguration)
        {
            if (loggerAuditSinkConfiguration == null)
            {
                throw new ArgumentNullException(nameof(loggerAuditSinkConfiguration));
            }

            ValidateRabbitMQClientConfiguration(clientConfiguration);

            return
                loggerAuditSinkConfiguration
                    .Sink(new RabbitMQSink(clientConfiguration, sinkConfiguration),
                        sinkConfiguration.RestrictedToMinimumLevel);
        }

        private static LoggerConfiguration Sink(
            this LoggerSinkConfiguration loggerConfiguration,
            RabbitMQClientConfiguration clientConfiguration,
            RabbitMQSinkConfiguration sinkConfiguration,
            Action<LoggerSinkConfiguration> failureSinkConfiguration)
        {
            if (failureSinkConfiguration == null)
            {
                return loggerConfiguration.Sink(clientConfiguration, sinkConfiguration);
            }

            return LoggerSinkConfiguration.Wrap(
                loggerConfiguration,
                failureSink => GetPeriodicBatchingSink(clientConfiguration, sinkConfiguration, failureSink),
                failureSinkConfiguration,
                sinkConfiguration.RestrictedToMinimumLevel);
        }

        private static LoggerConfiguration Sink(
            this LoggerSinkConfiguration loggerConfiguration,
            RabbitMQClientConfiguration clientConfiguration,
            RabbitMQSinkConfiguration sinkConfiguration)
        {
            var periodicBatchingSink = GetPeriodicBatchingSink(clientConfiguration, sinkConfiguration);

            return loggerConfiguration.Sink(periodicBatchingSink, sinkConfiguration.RestrictedToMinimumLevel);
        }

        private static PeriodicBatchingSink GetPeriodicBatchingSink(
            RabbitMQClientConfiguration clientConfiguration,
            RabbitMQSinkConfiguration sinkConfiguration,
            ILogEventSink failureSink = null)
        {
            var rabbitMQSink = new RabbitMQSink(clientConfiguration, sinkConfiguration, failureSink);
            var periodicBatchingSinkOptions = new PeriodicBatchingSinkOptions()
            {
                BatchSizeLimit = sinkConfiguration.BatchPostingLimit,
                Period = sinkConfiguration.Period,
                EagerlyEmitFirstEvent = true
            };
            if (sinkConfiguration.QueueLimit.HasValue)
            {
                periodicBatchingSinkOptions.QueueLimit = sinkConfiguration.QueueLimit.Value;
            }
            return new PeriodicBatchingSink(rabbitMQSink, periodicBatchingSinkOptions);
        }

        private static void ValidateRabbitMQClientConfiguration(RabbitMQClientConfiguration clientConfiguration)
        {
            if (clientConfiguration.Hostnames.Count == 0)
            {
                throw new ArgumentException("hostnames cannot be empty, specify at least one hostname");
            }

            if (string.IsNullOrEmpty(clientConfiguration.Username))
            {
                throw new ArgumentException("username cannot be 'null' or and empty string.");
            }

            if (clientConfiguration.Password == null)
            {
                throw new ArgumentException("password cannot be 'null'. Specify an empty string if password is empty.");
            }

            if (clientConfiguration.Port is < 0 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(clientConfiguration.Port), "port must be in a valid range (1 and 65535)");
            }
        }
    }
}