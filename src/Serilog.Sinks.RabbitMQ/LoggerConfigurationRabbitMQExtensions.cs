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
using Serilog.Sinks.RabbitMQ;

namespace Serilog;

/// <summary>
/// Extension methods to configure Serilog with a sink for RabbitMQ.
/// </summary>
public static class LoggerConfigurationRabbitMQExtensions
{
    /// <summary>
    /// Default value for the maximum number of events to include in a single batch.
    /// </summary>
    internal const int DEFAULT_BATCH_POSTING_LIMIT = 50;

    /// <summary>
    /// Default value for the time to wait between checking for event batches.
    /// </summary>
    internal static readonly TimeSpan DEFAULT_BUFFERING_TIME_LIMIT = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Adds a sink that lets you push log messages to RabbitMQ.
    /// </summary>
    /// <param name="loggerConfiguration">The logger sink configuration.</param>
    /// <param name="configure">Delegate to setup client and sink configuration.</param>
    /// <param name="failureSinkConfiguration">Delegate to setup failure sink configuration.</param>
    /// <returns>The logger configuration.</returns>
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
    /// <param name="loggerConfiguration">The logger sink configuration.</param>
    /// <param name="clientConfiguration"><see cref="RabbitMQClientConfiguration"/>.</param>
    /// <param name="sinkConfiguration"><see cref="RabbitMQSinkConfiguration"/>.</param>
    /// <param name="failureSinkConfiguration">Delegate to setup failure sink configuration.</param>
    /// <returns>The logger configuration.</returns>
    public static LoggerConfiguration RabbitMQ(
        this LoggerSinkConfiguration loggerConfiguration,
        RabbitMQClientConfiguration clientConfiguration,
        RabbitMQSinkConfiguration sinkConfiguration,
        Action<LoggerSinkConfiguration>? failureSinkConfiguration = null) =>
        loggerConfiguration.RegisterSink(clientConfiguration, sinkConfiguration, failureSinkConfiguration);

    /// <summary>
    /// Adds a sink that lets you push log messages to RabbitMQ.
    /// </summary>
    /// <param name="loggerConfiguration">The logger sink configuration.</param>
    /// <param name="hostnames">The hostnames of the RabbitMQ server to connect to.</param>
    /// <param name="username">The username for authentication.</param>
    /// <param name="password">The password for authentication.</param>
    /// <param name="exchange">The exchange name.</param>
    /// <param name="exchangeType">The exchange type.</param>
    /// <param name="deliveryMode">The delivery mode.</param>
    /// <param name="routingKey">The routing key.</param>
    /// <param name="port">The port number.</param>
    /// <param name="vHost">The virtual host.</param>
    /// <param name="clientProvidedName">Client provided name to be used for connection.</param>
    /// <param name="heartbeat">The heartbeat interval.</param>
    /// <param name="sslEnabled">Indicates whether SSL is enabled.</param>
    /// <param name="sslServerName">The SSL server name.</param>
    /// <param name="sslVersion">The SSL version.</param>
    /// <param name="sslAcceptablePolicyErrors">The acceptable SSL policy errors.</param>
    /// <param name="sslCheckCertificateRevocation">Indicates whether to check certificate revocation.</param>
    /// <param name="batchPostingLimit">The maximum number of events to include in a single batch.</param>
    /// <param name="bufferingTimeLimit">The time to wait between checking for event batches.</param>
    /// <param name="queueLimit">The batch internal queue limit.</param>
    /// <param name="formatter">The text formatter.</param>
    /// <param name="autoCreateExchange">Indicates whether to automatically create the exchange.</param>
    /// <param name="channelCount">Number of channels held in the pool. Channels are opened eagerly at startup.</param>
    /// <param name="levelSwitch">The minimal log event level switch.</param>
    /// <param name="emitEventFailure">The handling of event failure.</param>
    /// <param name="failureSinkConfiguration">The failure sink configuration.</param>
    /// <param name="sendMessageEvents">Contains events for sending messages.</param>
    /// <returns>The logger configuration.</returns>
    public static LoggerConfiguration RabbitMQ(
        this LoggerSinkConfiguration loggerConfiguration,
        string[] hostnames,
        string username,
        string password,
        string? exchange = null,
        string? exchangeType = null,
        RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable,
        string? routingKey = null,
        int port = 0,
        string? vHost = null,
        string? clientProvidedName = null,
        ushort heartbeat = 0,
        bool sslEnabled = false,
        string? sslServerName = null,
        SslProtocols sslVersion = SslProtocols.None,
        SslPolicyErrors sslAcceptablePolicyErrors = SslPolicyErrors.None,
        bool sslCheckCertificateRevocation = false,
        int batchPostingLimit = DEFAULT_BATCH_POSTING_LIMIT,
        TimeSpan bufferingTimeLimit = default,
        int? queueLimit = null,
        ITextFormatter? formatter = null,
        bool autoCreateExchange = false,
        int channelCount = RabbitMQClient.DEFAULT_CHANNEL_COUNT,
        LogEventLevel levelSwitch = LogEventLevel.Verbose,
        EmitEventFailureHandling emitEventFailure = EmitEventFailureHandling.WriteToSelfLog,
        Action<LoggerSinkConfiguration>? failureSinkConfiguration = null,
        ISendMessageEvents? sendMessageEvents = null)
    {
        var (client, sink) = BuildWriteToConfigurations(
            hostnames: hostnames,
            username: username,
            password: password,
            exchange: exchange,
            exchangeType: exchangeType,
            deliveryMode: deliveryMode,
            routingKey: routingKey,
            port: port,
            vHost: vHost,
            clientProvidedName: clientProvidedName,
            heartbeat: heartbeat,
            sslEnabled: sslEnabled,
            sslServerName: sslServerName,
            sslVersion: sslVersion,
            sslAcceptablePolicyErrors: sslAcceptablePolicyErrors,
            sslCheckCertificateRevocation: sslCheckCertificateRevocation,
            batchPostingLimit: batchPostingLimit,
            bufferingTimeLimit: bufferingTimeLimit,
            queueLimit: queueLimit,
            formatter: formatter,
            autoCreateExchange: autoCreateExchange,
            channelCount: channelCount,
            levelSwitch: levelSwitch,
            emitEventFailure: emitEventFailure,
            sendMessageEvents: sendMessageEvents);

        return loggerConfiguration.RegisterSink(client, sink, failureSinkConfiguration);
    }

    /// <summary>
    /// Adds an audit sink that lets you push log messages to RabbitMQ.
    /// </summary>
    /// <param name="loggerAuditSinkConfiguration">The logger audit sink configuration.</param>
    /// <param name="configure">Delegate to setup client and sink configuration.</param>
    /// <returns>The logger configuration.</returns>
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
    /// Adds an audit sink that lets you push log messages to RabbitMQ.
    /// </summary>
    /// <param name="loggerAuditSinkConfiguration">The logger audit sink configuration.</param>
    /// <param name="clientConfiguration"><see cref="RabbitMQClientConfiguration"/>.</param>
    /// <param name="sinkConfiguration"><see cref="RabbitMQSinkConfiguration"/>.</param>
    /// <returns>The logger configuration.</returns>
    public static LoggerConfiguration RabbitMQ(
        this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
        RabbitMQClientConfiguration clientConfiguration,
        RabbitMQSinkConfiguration sinkConfiguration) =>
        loggerAuditSinkConfiguration.RegisterAuditSink(clientConfiguration, sinkConfiguration);

    /// <summary>
    /// Adds an audit sink that lets you push log messages to RabbitMQ.
    /// </summary>
    /// <param name="loggerAuditSinkConfiguration">The logger audit sink configuration.</param>
    /// <param name="hostnames">The hostnames of the RabbitMQ server to connect to.</param>
    /// <param name="username">The username for authentication.</param>
    /// <param name="password">The password for authentication.</param>
    /// <param name="exchange">The exchange name.</param>
    /// <param name="exchangeType">The exchange type.</param>
    /// <param name="deliveryMode">The delivery mode.</param>
    /// <param name="routingKey">The routing key.</param>
    /// <param name="port">The port number.</param>
    /// <param name="vHost">The virtual host.</param>
    /// <param name="clientProvidedName">Client provided name to be used for connection.</param>
    /// <param name="heartbeat">The heartbeat interval.</param>
    /// <param name="sslEnabled">Indicates whether SSL is enabled.</param>
    /// <param name="sslServerName">The SSL server name.</param>
    /// <param name="sslVersion">The SSL version.</param>
    /// <param name="sslAcceptablePolicyErrors">The acceptable SSL policy errors.</param>
    /// <param name="sslCheckCertificateRevocation">Indicates whether to check certificate revocation.</param>
    /// <param name="formatter">The text formatter.</param>
    /// <param name="autoCreateExchange">Indicates whether to automatically create the exchange.</param>
    /// <param name="channelCount">Number of channels held in the pool. Channels are opened eagerly at startup.</param>
    /// <param name="levelSwitch">The minimal log event level switch.</param>
    /// <param name="sendMessageEvents">Contains events for sending messages.</param>
    /// <returns>The logger configuration.</returns>
    public static LoggerConfiguration RabbitMQ(
        this LoggerAuditSinkConfiguration loggerAuditSinkConfiguration,
        string[] hostnames,
        string username,
        string password,
        string? exchange = null,
        string? exchangeType = null,
        RabbitMQDeliveryMode deliveryMode = RabbitMQDeliveryMode.NonDurable,
        string? routingKey = null,
        int port = 0,
        string? vHost = null,
        string? clientProvidedName = null,
        ushort heartbeat = 0,
        bool sslEnabled = false,
        string? sslServerName = null,
        SslProtocols sslVersion = SslProtocols.None,
        SslPolicyErrors sslAcceptablePolicyErrors = SslPolicyErrors.None,
        bool sslCheckCertificateRevocation = false,
        ITextFormatter? formatter = null,
        bool autoCreateExchange = false,
        int channelCount = RabbitMQClient.DEFAULT_CHANNEL_COUNT,
        LogEventLevel levelSwitch = LogEventLevel.Verbose,
        ISendMessageEvents? sendMessageEvents = null)
    {
        var (client, sink) = BuildAuditToConfigurations(
            hostnames: hostnames,
            username: username,
            password: password,
            exchange: exchange,
            exchangeType: exchangeType,
            deliveryMode: deliveryMode,
            routingKey: routingKey,
            port: port,
            vHost: vHost,
            clientProvidedName: clientProvidedName,
            heartbeat: heartbeat,
            sslEnabled: sslEnabled,
            sslServerName: sslServerName,
            sslVersion: sslVersion,
            sslAcceptablePolicyErrors: sslAcceptablePolicyErrors,
            sslCheckCertificateRevocation: sslCheckCertificateRevocation,
            formatter: formatter,
            autoCreateExchange: autoCreateExchange,
            channelCount: channelCount,
            levelSwitch: levelSwitch,
            sendMessageEvents: sendMessageEvents);

        return loggerAuditSinkConfiguration.RegisterAuditSink(client, sink);
    }

    private static LoggerConfiguration RegisterSink(
        this LoggerSinkConfiguration loggerSinkConfiguration,
        RabbitMQClientConfiguration clientConfiguration,
        RabbitMQSinkConfiguration sinkConfiguration,
        Action<LoggerSinkConfiguration>? failureSinkConfiguration = null)
    {
        if (loggerSinkConfiguration == null)
        {
            throw new ArgumentNullException(nameof(loggerSinkConfiguration));
        }

        // Order-dependent: defaults must be applied BEFORE Validate() so that a user who
        // passed default(int)/default(TimeSpan) (e.g. appsettings omission) doesn't trip the
        // positive-value checks. Do not reorder these blocks without moving the defaulting
        // into RabbitMQSinkConfiguration.Validate() itself.
        sinkConfiguration.BatchPostingLimit = sinkConfiguration.BatchPostingLimit == default
            ? DEFAULT_BATCH_POSTING_LIMIT
            : sinkConfiguration.BatchPostingLimit;

        sinkConfiguration.BufferingTimeLimit = sinkConfiguration.BufferingTimeLimit == default
            ? DEFAULT_BUFFERING_TIME_LIMIT
            : sinkConfiguration.BufferingTimeLimit;

        clientConfiguration.Validate();
        sinkConfiguration.Validate();

        if (failureSinkConfiguration == null)
        {
            var periodicBatchingSink = GetPeriodicBatchingSink(clientConfiguration, sinkConfiguration);

            return loggerSinkConfiguration.Sink(periodicBatchingSink, sinkConfiguration.RestrictedToMinimumLevel);
        }

        var wrapper = LoggerSinkConfiguration.Wrap(sink => GetPeriodicBatchingSink(clientConfiguration, sinkConfiguration, sink), failureSinkConfiguration);
        return loggerSinkConfiguration.Sink(wrapper, sinkConfiguration.RestrictedToMinimumLevel);
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

        clientConfiguration.Validate();
        sinkConfiguration.Validate();

        return loggerAuditSinkConfiguration.Sink(new RabbitMQSink(clientConfiguration, sinkConfiguration, null), sinkConfiguration.RestrictedToMinimumLevel);
    }

    /// <summary>
    /// Maps the shared client-side flat parameters (every field of <see cref="RabbitMQClientConfiguration"/>
    /// plus the SSL-knob subset) into a populated configuration. Exposed internally so the flat
    /// overloads can be unit-tested at the parameter-mapping level without constructing a real
    /// <see cref="RabbitMQSink"/> (which would spawn a background channel-pool warmup task).
    /// </summary>
    internal static RabbitMQClientConfiguration BuildClientConfiguration(
        string[] hostnames,
        string username,
        string password,
        string? exchange,
        string? exchangeType,
        RabbitMQDeliveryMode deliveryMode,
        string? routingKey,
        int port,
        string? vHost,
        string? clientProvidedName,
        ushort heartbeat,
        bool sslEnabled,
        string? sslServerName,
        SslProtocols sslVersion,
        SslPolicyErrors sslAcceptablePolicyErrors,
        bool sslCheckCertificateRevocation,
        bool autoCreateExchange,
        int channelCount,
        ISendMessageEvents? sendMessageEvents)
    {
        var clientConfiguration = new RabbitMQClientConfiguration
        {
            Hostnames = hostnames,
            Username = username,
            Password = password,
            Exchange = exchange ?? string.Empty,
            ExchangeType = exchangeType ?? ExchangeType.Fanout,
            DeliveryMode = deliveryMode,
            RoutingKey = routingKey ?? string.Empty,
            Port = port,
            VHost = vHost ?? string.Empty,
            ClientProvidedName = clientProvidedName,
            Heartbeat = heartbeat,
            AutoCreateExchange = autoCreateExchange,
            ChannelCount = channelCount,
            SendMessageEvents = sendMessageEvents ?? new SendMessageEvents(),
        };

        if (sslEnabled && sslServerName is not null)
        {
            clientConfiguration.SslOption = new SslOption
            {
                Enabled = true,
                ServerName = sslServerName,
                Version = sslVersion,
                AcceptablePolicyErrors = sslAcceptablePolicyErrors,
                CheckCertificateRevocation = sslCheckCertificateRevocation,
            };
        }

        return clientConfiguration;
    }

    /// <summary>
    /// Maps the <c>WriteTo.RabbitMQ(...)</c> flat-parameter overload into its
    /// <see cref="RabbitMQClientConfiguration"/> and <see cref="RabbitMQSinkConfiguration"/>.
    /// The public overload delegates here, then hands the results to <c>RegisterSink</c>.
    /// </summary>
    internal static (RabbitMQClientConfiguration Client, RabbitMQSinkConfiguration Sink) BuildWriteToConfigurations(
        string[] hostnames,
        string username,
        string password,
        string? exchange,
        string? exchangeType,
        RabbitMQDeliveryMode deliveryMode,
        string? routingKey,
        int port,
        string? vHost,
        string? clientProvidedName,
        ushort heartbeat,
        bool sslEnabled,
        string? sslServerName,
        SslProtocols sslVersion,
        SslPolicyErrors sslAcceptablePolicyErrors,
        bool sslCheckCertificateRevocation,
        int batchPostingLimit,
        TimeSpan bufferingTimeLimit,
        int? queueLimit,
        ITextFormatter? formatter,
        bool autoCreateExchange,
        int channelCount,
        LogEventLevel levelSwitch,
        EmitEventFailureHandling emitEventFailure,
        ISendMessageEvents? sendMessageEvents)
    {
        var clientConfiguration = BuildClientConfiguration(
            hostnames: hostnames,
            username: username,
            password: password,
            exchange: exchange,
            exchangeType: exchangeType,
            deliveryMode: deliveryMode,
            routingKey: routingKey,
            port: port,
            vHost: vHost,
            clientProvidedName: clientProvidedName,
            heartbeat: heartbeat,
            sslEnabled: sslEnabled,
            sslServerName: sslServerName,
            sslVersion: sslVersion,
            sslAcceptablePolicyErrors: sslAcceptablePolicyErrors,
            sslCheckCertificateRevocation: sslCheckCertificateRevocation,
            autoCreateExchange: autoCreateExchange,
            channelCount: channelCount,
            sendMessageEvents: sendMessageEvents);

        // Default substitution for BatchPostingLimit / BufferingTimeLimit lives in
        // RegisterSink so that the delegate, direct-config, and flat-overload paths
        // all share one defaulting site. Pass the caller's values through verbatim here.
        var sinkConfiguration = new RabbitMQSinkConfiguration
        {
            BatchPostingLimit = batchPostingLimit,
            BufferingTimeLimit = bufferingTimeLimit,
            QueueLimit = queueLimit,
            EmitEventFailure = emitEventFailure,
            RestrictedToMinimumLevel = levelSwitch,
        };

        if (formatter != null)
        {
            sinkConfiguration.TextFormatter = formatter;
        }

        return (clientConfiguration, sinkConfiguration);
    }

    /// <summary>
    /// Maps the <c>AuditTo.RabbitMQ(...)</c> flat-parameter overload into its
    /// <see cref="RabbitMQClientConfiguration"/> and <see cref="RabbitMQSinkConfiguration"/>.
    /// The audit overload exposes fewer knobs than <see cref="BuildWriteToConfigurations"/> —
    /// no batching or failure-sink parameters — because audit sinks emit synchronously.
    /// </summary>
    internal static (RabbitMQClientConfiguration Client, RabbitMQSinkConfiguration Sink) BuildAuditToConfigurations(
        string[] hostnames,
        string username,
        string password,
        string? exchange,
        string? exchangeType,
        RabbitMQDeliveryMode deliveryMode,
        string? routingKey,
        int port,
        string? vHost,
        string? clientProvidedName,
        ushort heartbeat,
        bool sslEnabled,
        string? sslServerName,
        SslProtocols sslVersion,
        SslPolicyErrors sslAcceptablePolicyErrors,
        bool sslCheckCertificateRevocation,
        ITextFormatter? formatter,
        bool autoCreateExchange,
        int channelCount,
        LogEventLevel levelSwitch,
        ISendMessageEvents? sendMessageEvents)
    {
        var clientConfiguration = BuildClientConfiguration(
            hostnames: hostnames,
            username: username,
            password: password,
            exchange: exchange,
            exchangeType: exchangeType,
            deliveryMode: deliveryMode,
            routingKey: routingKey,
            port: port,
            vHost: vHost,
            clientProvidedName: clientProvidedName,
            heartbeat: heartbeat,
            sslEnabled: sslEnabled,
            sslServerName: sslServerName,
            sslVersion: sslVersion,
            sslAcceptablePolicyErrors: sslAcceptablePolicyErrors,
            sslCheckCertificateRevocation: sslCheckCertificateRevocation,
            autoCreateExchange: autoCreateExchange,
            channelCount: channelCount,
            sendMessageEvents: sendMessageEvents);

        var sinkConfiguration = new RabbitMQSinkConfiguration
        {
            RestrictedToMinimumLevel = levelSwitch,
        };

        if (formatter != null)
        {
            sinkConfiguration.TextFormatter = formatter;
        }

        return (clientConfiguration, sinkConfiguration);
    }

    private static ILogEventSink GetPeriodicBatchingSink(
        RabbitMQClientConfiguration clientConfiguration,
        RabbitMQSinkConfiguration sinkConfiguration,
        ILogEventSink? failureSink = null)
    {
        var rabbitMQSink = new RabbitMQSink(clientConfiguration, sinkConfiguration, failureSink);
        var options = new BatchingOptions
        {
            BatchSizeLimit = sinkConfiguration.BatchPostingLimit,
            BufferingTimeLimit = sinkConfiguration.BufferingTimeLimit,
            EagerlyEmitFirstEvent = true,
        };

        if (sinkConfiguration.QueueLimit.HasValue)
        {
            options.QueueLimit = sinkConfiguration.QueueLimit.Value;
        }

        return LoggerSinkConfiguration.CreateSink(lc => lc.Sink(rabbitMQSink, options));
    }
}
