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

namespace Serilog;

/// <summary>
/// Extension method to configure Serilog with a Sink for RabbitMQ.
/// </summary>
public static class LoggerConfigurationRabbitMQExtension
{
    private const int DEFAULT_BATCH_POSTING_LIMIT = 50;
    private static readonly TimeSpan _defaultPeriod = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Adds a sink that lets you push log messages to RabbitMQ.
    /// </summary>
    public static LoggerConfiguration RabbitMQ(
        this LoggerSinkConfiguration loggerConfiguration,
        Action<RabbitMQClientConfiguration, RabbitMQSinkConfiguration> configure,
        Action<LoggerSinkConfiguration>? failureSinkConfiguration = null)
    {
        var clientConfiguration = new RabbitMQClientConfiguration();
        var sinkConfiguration = new RabbitMQSinkConfiguration();

        configure(clientConfiguration, sinkConfiguration);

        return loggerConfiguration.RegisterSink(clientConfiguration, sinkConfiguration, failureSinkConfiguration);
    }

    /// <summary>
    /// Adds a sink that lets you push log messages to RabbitMQ.
    /// </summary>
    public static LoggerConfiguration RabbitMQ(
        this LoggerSinkConfiguration loggerConfiguration,
        RabbitMQClientConfiguration clientConfiguration,
        RabbitMQSinkConfiguration sinkConfiguration,
        Action<LoggerSinkConfiguration>? failureSinkConfiguration = null)
    {
        return loggerConfiguration.RegisterSink(clientConfiguration, sinkConfiguration, failureSinkConfiguration);
    }

    /// <summary>
    /// Configures Serilog logger sink configuration with RabbitMQ.
    /// </summary>
    /// <param name="loggerConfiguration">The logger sink configuration.</param>
    /// <param name="hostnames">The hostnames of the RabbitMQ server to connect to.</param>
    /// <param name="username">The username for authentication.</param>
    /// <param name="password">The password for authentication.</param>
    /// <param name="exchange">The exchange name.</param>
    /// <param name="exchangeType">The exchange type.</param>
    /// <param name="deliveryMode">The delivery mode.</param>
    /// <param name="routeKey">The routing key.</param>
    /// <param name="port">The port number.</param>
    /// <param name="vHost">The virtual host.</param>
    /// <param name="heartbeat">The heartbeat interval.</param>
    /// <param name="sslEnabled">Indicates whether SSL is enabled.</param>
    /// <param name="sslServerName">The SSL server name.</param>
    /// <param name="sslVersion">The SSL version.</param>
    /// <param name="sslAcceptablePolicyErrors">The acceptable SSL policy errors.</param>
    /// <param name="sslCheckCertificateRevocation">Indicates whether to check certificate revocation.</param>
    /// <param name="batchPostingLimit">The maximum number of events to include in a single batch.</param>
    /// <param name="period">The time to wait between checking for event batches.</param>
    /// <param name="queueLimit">The batch internal queue limit.</param>
    /// <param name="formatter">The text formatter.</param>
    /// <param name="autoCreateExchange">Indicates whether to automatically create the exchange.</param>
    /// <param name="maxChannels">The maximum number of channels.</param>
    /// <param name="levelSwitch">The minimal log event level switch.</param>
    /// <param name="emitEventFailure">The handling of event failure.</param>
    /// <param name="failureSinkConfiguration">The failure sink configuration.</param>
    /// <returns>The logger configuration.</returns>
    public static LoggerConfiguration RabbitMQ(
        this LoggerSinkConfiguration loggerConfiguration,
        string[] hostnames,
        string? username,
        string? password,
        string? exchange = null,
        string? exchangeType = null,
        RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable,
        string? routeKey = null,
        int port = 0,
        string? vHost = null,
        ushort heartbeat = 0,
        bool sslEnabled = false,
        string? sslServerName = null,
        SslProtocols sslVersion = SslProtocols.None,
        SslPolicyErrors sslAcceptablePolicyErrors = SslPolicyErrors.None,
        bool sslCheckCertificateRevocation = false,
        int batchPostingLimit = 0,
        TimeSpan period = default,
        int? queueLimit = null,
        ITextFormatter? formatter = null,
        bool autoCreateExchange = false,
        int maxChannels = RabbitMQClient.DEFAULT_MAX_CHANNEL_COUNT,
        LogEventLevel levelSwitch = LogEventLevel.Information,
        EmitEventFailureHandling emitEventFailure = EmitEventFailureHandling.WriteToSelfLog,
        Action<LoggerSinkConfiguration>? failureSinkConfiguration = null)
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
            MaxChannels = maxChannels,
        };

        if (sslEnabled)
        {
            clientConfiguration.SslOption = new SslOption()
            {
                Enabled = true,
                ServerName = sslServerName,
                Version = sslVersion,
                AcceptablePolicyErrors = sslAcceptablePolicyErrors,
                CheckCertificateRevocation = sslCheckCertificateRevocation,
            };
        }

        var sinkConfiguration = new RabbitMQSinkConfiguration
        {
            BatchPostingLimit = batchPostingLimit == default ? DEFAULT_BATCH_POSTING_LIMIT : batchPostingLimit,
            Period = period == default ? _defaultPeriod : period,
            QueueLimit = queueLimit,
            EmitEventFailure = emitEventFailure,
            RestrictedToMinimumLevel = levelSwitch,
        };
        if (formatter != null)
        {
            sinkConfiguration.TextFormatter = formatter;
        }

        return loggerConfiguration.RegisterSink(clientConfiguration, sinkConfiguration, failureSinkConfiguration);
    }

    /// <summary>
    /// Configures Serilog audit logger configuration with RabbitMQ.
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
    /// Configures Serilog audit logger configuration with RabbitMQ.
    /// </summary>
    public static LoggerConfiguration RabbitMQ(
        this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
        RabbitMQClientConfiguration clientConfiguration,
        RabbitMQSinkConfiguration sinkConfiguration)
    {
        return loggerAuditSinkConfiguration.RegisterAuditSink(clientConfiguration, sinkConfiguration);
    }

    /// <summary>
    /// Configures Serilog audit sink logger configuration with RabbitMQ.
    /// </summary>
    /// <param name="loggerAuditSinkConfiguration">The logger audit sink configuration.</param>
    /// <param name="hostnames">The hostnames of the RabbitMQ server to connect to.</param>
    /// <param name="username">The username for authentication.</param>
    /// <param name="password">The password for authentication.</param>
    /// <param name="exchange">The exchange name.</param>
    /// <param name="exchangeType">The exchange type.</param>
    /// <param name="deliveryMode">The delivery mode.</param>
    /// <param name="routeKey">The routing key.</param>
    /// <param name="port">The port number.</param>
    /// <param name="vHost">The virtual host.</param>
    /// <param name="heartbeat">The heartbeat interval.</param>
    /// <param name="sslEnabled">Indicates whether SSL is enabled.</param>
    /// <param name="sslServerName">The SSL server name.</param>
    /// <param name="sslVersion">The SSL version.</param>
    /// <param name="sslAcceptablePolicyErrors">The acceptable SSL policy errors.</param>
    /// <param name="sslCheckCertificateRevocation">Indicates whether to check certificate revocation.</param>
    /// <param name="formatter">The text formatter.</param>
    /// <param name="autoCreateExchange">Indicates whether to automatically create the exchange.</param>
    /// <param name="maxChannels">The maximum number of channels.</param>
    /// <param name="levelSwitch">The minimal log event level switch.</param>
    /// <returns>The logger configuration.</returns>
    public static LoggerConfiguration RabbitMQ(
        this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
        string[] hostnames,
        string username,
        string password,
        string? exchange = null,
        string? exchangeType = null,
        RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable,
        string? routeKey = null,
        int port = 0,
        string? vHost = null,
        ushort heartbeat = 0,
        bool sslEnabled = false,
        string? sslServerName = null,
        SslProtocols sslVersion = SslProtocols.None,
        SslPolicyErrors sslAcceptablePolicyErrors = SslPolicyErrors.None,
        bool sslCheckCertificateRevocation = false,
        ITextFormatter? formatter = null,
        bool autoCreateExchange = false,
        int maxChannels = RabbitMQClient.DEFAULT_MAX_CHANNEL_COUNT,
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
            MaxChannels = maxChannels,
        };

        if (sslEnabled)
        {
            clientConfiguration.SslOption = new SslOption()
            {
                Enabled = true,
                ServerName = sslServerName,
                Version = sslVersion,
                AcceptablePolicyErrors = sslAcceptablePolicyErrors,
                CheckCertificateRevocation = sslCheckCertificateRevocation,
            };
        }

        var sinkConfiguration = new RabbitMQSinkConfiguration
        {
            RestrictedToMinimumLevel = levelSwitch,
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
        Action<LoggerSinkConfiguration>? failureSinkConfiguration = null)
    {
        // guards
        if (loggerSinkConfiguration == null)
        {
            throw new ArgumentNullException(nameof(loggerSinkConfiguration));
        }

        sinkConfiguration.BatchPostingLimit = (sinkConfiguration.BatchPostingLimit == default)
            ? DEFAULT_BATCH_POSTING_LIMIT
            : sinkConfiguration.BatchPostingLimit;
        sinkConfiguration.Period = (sinkConfiguration.Period == default) ? _defaultPeriod : sinkConfiguration.Period;

        ValidateRabbitMQClientConfiguration(clientConfiguration);

        if (failureSinkConfiguration == null)
        {
            var periodicBatchingSink = GetPeriodicBatchingSink(clientConfiguration, sinkConfiguration);

            return loggerSinkConfiguration.Sink(periodicBatchingSink, sinkConfiguration.RestrictedToMinimumLevel);
        }

        return LoggerSinkConfiguration.Wrap(
            loggerSinkConfiguration,
            failureSink => GetPeriodicBatchingSink(clientConfiguration, sinkConfiguration, failureSink),
            failureSinkConfiguration,
            sinkConfiguration.RestrictedToMinimumLevel);
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

        return loggerAuditSinkConfiguration
            .Sink(new RabbitMQSink(clientConfiguration, sinkConfiguration), sinkConfiguration.RestrictedToMinimumLevel);
    }

    private static PeriodicBatchingSink GetPeriodicBatchingSink(
        RabbitMQClientConfiguration clientConfiguration,
        RabbitMQSinkConfiguration sinkConfiguration,
        ILogEventSink? failureSink = null)
    {
        var rabbitMQSink = new RabbitMQSink(clientConfiguration, sinkConfiguration, failureSink);
        var periodicBatchingSinkOptions = new PeriodicBatchingSinkOptions()
        {
            BatchSizeLimit = sinkConfiguration.BatchPostingLimit,
            Period = sinkConfiguration.Period,
            EagerlyEmitFirstEvent = true,
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
