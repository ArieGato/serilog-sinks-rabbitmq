using System.Net.Security;
using System.Security.Authentication;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace Serilog.Sinks.RabbitMQ.Tests;

// Most tests below operate on the internal Build{WriteTo,AuditTo}Configurations helpers,
// which do not construct a real RabbitMQSink and therefore do not spawn background channel-
// pool warmup tasks. A handful of tests at the top still drive the full extension surface
// (Validate regression guards, failure-sink wiring, QueueLimit forwarding, RegisterSink
// default substitution). Those do build a real sink, so the class remains in the
// [Collection("SelfLog")] group to serialise against the SelfLog-asserting tests
// elsewhere (issue #282).
[Collection("SelfLog")]
public class LoggerConfigurationRabbitMQExtensionsTests
{
    private static RabbitMQClientConfiguration ValidClientConfiguration() => new()
    {
        Hostnames = ["localhost"],
        Username = "guest",
        Password = "guest",
        Port = 5672,
        Exchange = "x",
        ExchangeType = "topic",
        ChannelCount = 1,
    };

    private static RabbitMQSinkConfiguration ValidSinkConfiguration() => new()
    {
        BatchPostingLimit = 50,
        BufferingTimeLimit = TimeSpan.FromSeconds(2),
    };

    [Fact]
    public void WriteTo_RabbitMQ_RunsClientConfigurationValidate()
    {
        // Regression guard: proves the plumbing in RegisterSink still routes through
        // RabbitMQClientConfiguration.Validate(). If someone accidentally removes that
        // call, the invalid hostnames configuration below would no longer throw at
        // sink-registration time.
        var loggerConfig = new LoggerConfiguration();
        var clientConfig = ValidClientConfiguration();
        clientConfig.Hostnames = [];

        Should.Throw<ArgumentException>(() =>
            loggerConfig.WriteTo.RabbitMQ(clientConfig, ValidSinkConfiguration()))
            .Message.ShouldContain("hostnames");
    }

    [Fact]
    public void WriteTo_RabbitMQ_RunsSinkConfigurationValidate()
    {
        // Same regression guard, for sinkConfiguration.Validate(). A previously-unvalidated
        // invalid QueueLimit now trips at configuration time.
        var loggerConfig = new LoggerConfiguration();
        var sinkConfig = ValidSinkConfiguration();
        sinkConfig.QueueLimit = -5;

        Should.Throw<ArgumentOutOfRangeException>(() =>
            loggerConfig.WriteTo.RabbitMQ(ValidClientConfiguration(), sinkConfig))
            .ParamName.ShouldBe("QueueLimit");
    }

    [Fact]
    public void AuditTo_RabbitMQ_RunsClientConfigurationValidate()
    {
        var loggerConfig = new LoggerConfiguration();
        var clientConfig = ValidClientConfiguration();
        clientConfig.Username = string.Empty;

        Should.Throw<ArgumentException>(() =>
            loggerConfig.AuditTo.RabbitMQ(clientConfig, ValidSinkConfiguration()))
            .Message.ShouldContain("username");
    }

    [Fact]
    public void AuditTo_RabbitMQ_RunsSinkConfigurationValidate()
    {
        var loggerConfig = new LoggerConfiguration();
        var sinkConfig = ValidSinkConfiguration();
        sinkConfig.TextFormatter = null!;

        Should.Throw<ArgumentException>(() =>
            loggerConfig.AuditTo.RabbitMQ(ValidClientConfiguration(), sinkConfig))
            .ParamName.ShouldBe("TextFormatter");
    }

    [Fact]
    public void WriteTo_RabbitMQ_Delegate_Builds_AndAppliesCallerOverrides()
    {
        // The delegate overload lets the caller populate both configurations inline.
        // Proves the delegate is invoked and the resulting configuration drives RegisterSink.
        var delegateCalled = false;

        using var logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ(
                (client, sink) =>
                {
                    delegateCalled = true;
                    client.Hostnames = ["localhost"];
                    client.Username = "guest";
                    client.Password = "guest";
                    client.Exchange = "x";
                    client.ExchangeType = "topic";
                    client.ChannelCount = 1;
                    sink.BatchPostingLimit = 25;
                    sink.BufferingTimeLimit = TimeSpan.FromSeconds(1);
                })
            .CreateLogger();

        delegateCalled.ShouldBeTrue();
        logger.ShouldNotBeNull();
    }

    [Fact]
    public void WriteTo_RabbitMQ_WithFailureSinkConfiguration_Builds()
    {
        // Exercises the `failureSinkConfiguration != null` branch in RegisterSink —
        // the wrapper sink path that routes failed events through the failure sink.
        using var logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ(
                ValidClientConfiguration(),
                ValidSinkConfiguration(),
                failureSinkConfiguration: fb => fb.Sink(Substitute.For<Core.ILogEventSink>()))
            .CreateLogger();

        logger.ShouldNotBeNull();
    }

    [Fact]
    public void WriteTo_RabbitMQ_WithQueueLimit_Builds()
    {
        // Exercises the `sinkConfiguration.QueueLimit.HasValue` branch in
        // GetPeriodicBatchingSink that forwards QueueLimit to BatchingOptions.
        var sinkConfig = ValidSinkConfiguration();
        sinkConfig.QueueLimit = 1000;

        using var logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ(ValidClientConfiguration(), sinkConfig)
            .CreateLogger();

        logger.ShouldNotBeNull();
    }

    [Fact]
    public void WriteTo_RabbitMQ_AppliesDefaults_WhenBatchingValuesAreDefault()
    {
        // Covers the default-substitution branches in RegisterSink (BatchPostingLimit ==
        // default / BufferingTimeLimit == default). A caller who binds from appsettings
        // with those values omitted gets the property-initializer values; one who
        // constructs a config and leaves the batch values at their CLR defaults gets the
        // library defaults. Asserting the mutation on the caller's sinkConfig is a real
        // behavioural check (unlike the coverage-only flat-overload tests above).
        var sinkConfig = new RabbitMQSinkConfiguration
        {
            BatchPostingLimit = 0,
            BufferingTimeLimit = TimeSpan.Zero,
        };

        using var logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ(ValidClientConfiguration(), sinkConfig)
            .CreateLogger();

        logger.ShouldNotBeNull();
        sinkConfig.BatchPostingLimit.ShouldBe(50);
        sinkConfig.BufferingTimeLimit.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AuditTo_RabbitMQ_Delegate_Builds_AndAppliesCallerOverrides()
    {
        var delegateCalled = false;

        using var logger = new LoggerConfiguration()
            .AuditTo.RabbitMQ(
                (client, sink) =>
                {
                    delegateCalled = true;
                    client.Hostnames = ["localhost"];
                    client.Username = "guest";
                    client.Password = "guest";
                    client.ChannelCount = 1;
                })
            .CreateLogger();

        delegateCalled.ShouldBeTrue();
        logger.ShouldNotBeNull();
    }

    [Fact]
    public void WriteTo_RabbitMQ_FlatOverload_DelegatesToBuilderAndRegisterSink()
    {
        // Smoke test: proves the public flat overload wires through BuildWriteToConfigurations
        // and RegisterSink. Builder-level mapping is asserted exhaustively in the
        // BuildWriteToConfigurations_* tests below; here we only need to cover the
        // delegation itself. A real RabbitMQSink is constructed — its background warmup
        // will fail (no broker) but is harmless, and [Collection("SelfLog")] serialises
        // the noise.
        using var logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ(
                hostnames: ["localhost"],
                username: "guest",
                password: "guest",
                channelCount: 1)
            .CreateLogger();

        logger.ShouldNotBeNull();
    }

    [Fact]
    public void AuditTo_RabbitMQ_FlatOverload_DelegatesToBuilderAndRegisterAuditSink()
    {
        // Smoke test for the audit-side delegation; see WriteTo counterpart above.
        using var logger = new LoggerConfiguration()
            .AuditTo.RabbitMQ(
                hostnames: ["localhost"],
                username: "guest",
                password: "guest",
                channelCount: 1)
            .CreateLogger();

        logger.ShouldNotBeNull();
    }

    [Fact]
    public void BuildWriteToConfigurations_MapsEveryOption_WhenAllSet()
    {
        var formatter = Substitute.For<ITextFormatter>();
        var sendMessageEvents = Substitute.For<ISendMessageEvents>();

        var (client, sink) = LoggerConfigurationRabbitMQExtensions.BuildWriteToConfigurations(
            hostnames: ["host-a", "host-b"],
            username: "user",
            password: "pass",
            exchange: "logs",
            exchangeType: "topic",
            deliveryMode: RabbitMQDeliveryMode.Durable,
            routingKey: "info",
            port: 5673,
            vHost: "/tenant",
            clientProvidedName: "write-to-test",
            heartbeat: 45,
            sslEnabled: true,
            sslServerName: "broker.example.com",
            sslVersion: SslProtocols.Tls12,
            sslAcceptablePolicyErrors: SslPolicyErrors.RemoteCertificateNameMismatch,
            sslCheckCertificateRevocation: true,
            batchPostingLimit: 25,
            bufferingTimeLimit: TimeSpan.FromSeconds(5),
            queueLimit: 500,
            formatter: formatter,
            autoCreateExchange: true,
            channelCount: 7,
            levelSwitch: LogEventLevel.Warning,
            emitEventFailure: EmitEventFailureHandling.ThrowException,
            sendMessageEvents: sendMessageEvents);

        client.Hostnames.ShouldBe(["host-a", "host-b"]);
        client.Username.ShouldBe("user");
        client.Password.ShouldBe("pass");
        client.Exchange.ShouldBe("logs");
        client.ExchangeType.ShouldBe("topic");
        client.DeliveryMode.ShouldBe(RabbitMQDeliveryMode.Durable);
        client.RoutingKey.ShouldBe("info");
        client.Port.ShouldBe(5673);
        client.VHost.ShouldBe("/tenant");
        client.ClientProvidedName.ShouldBe("write-to-test");
        client.Heartbeat.ShouldBe((ushort)45);
        client.AutoCreateExchange.ShouldBeTrue();
        client.ChannelCount.ShouldBe(7);
        client.SendMessageEvents.ShouldBeSameAs(sendMessageEvents);

        client.SslOption.ShouldNotBeNull();
        client.SslOption!.Enabled.ShouldBeTrue();
        client.SslOption.ServerName.ShouldBe("broker.example.com");
        client.SslOption.Version.ShouldBe(SslProtocols.Tls12);
        client.SslOption.AcceptablePolicyErrors.ShouldBe(SslPolicyErrors.RemoteCertificateNameMismatch);
        client.SslOption.CheckCertificateRevocation.ShouldBeTrue();

        sink.BatchPostingLimit.ShouldBe(25);
        sink.BufferingTimeLimit.ShouldBe(TimeSpan.FromSeconds(5));
        sink.QueueLimit.ShouldBe(500);
        sink.TextFormatter.ShouldBeSameAs(formatter);
        sink.RestrictedToMinimumLevel.ShouldBe(LogEventLevel.Warning);
        sink.EmitEventFailure.ShouldBe(EmitEventFailureHandling.ThrowException);
    }

    [Fact]
    public void BuildWriteToConfigurations_AppliesDefaults_WhenOptionalArgsAreNullOrDefault()
    {
        // All optional reference-ish args are null; numeric defaults match the public
        // flat overload's parameter defaults. Every null-coalescing branch resolves to
        // its library default. Batching values are passed through verbatim — default
        // substitution for BatchPostingLimit / BufferingTimeLimit now lives solely in
        // RegisterSink (covered end-to-end by
        // WriteTo_RabbitMQ_AppliesDefaults_WhenBatchingValuesAreDefault).
        var (client, sink) = LoggerConfigurationRabbitMQExtensions.BuildWriteToConfigurations(
            hostnames: ["host-a"],
            username: "user",
            password: "pass",
            exchange: null,
            exchangeType: null,
            deliveryMode: RabbitMQDeliveryMode.NonDurable,
            routingKey: null,
            port: 0,
            vHost: null,
            clientProvidedName: null,
            heartbeat: 0,
            sslEnabled: false,
            sslServerName: null,
            sslVersion: SslProtocols.None,
            sslAcceptablePolicyErrors: SslPolicyErrors.None,
            sslCheckCertificateRevocation: false,
            batchPostingLimit: 0,
            bufferingTimeLimit: TimeSpan.Zero,
            queueLimit: null,
            formatter: null,
            autoCreateExchange: false,
            channelCount: 64,
            levelSwitch: LogEventLevel.Verbose,
            emitEventFailure: EmitEventFailureHandling.WriteToSelfLog,
            sendMessageEvents: null);

        // Null-coalescing on the client side.
        client.Exchange.ShouldBe(string.Empty);
        client.ExchangeType.ShouldBe(ExchangeType.Fanout);
        client.RoutingKey.ShouldBe(string.Empty);
        client.VHost.ShouldBe(string.Empty);
        client.ClientProvidedName.ShouldBeNull();
        client.SendMessageEvents.ShouldNotBeNull();
        client.SendMessageEvents.ShouldBeOfType<SendMessageEvents>();
        client.SslOption.ShouldBeNull();

        // Batching values pass through unchanged; RegisterSink is the single defaulting site.
        sink.BatchPostingLimit.ShouldBe(0);
        sink.BufferingTimeLimit.ShouldBe(TimeSpan.Zero);
        sink.QueueLimit.ShouldBeNull();
        sink.TextFormatter.ShouldBeOfType<CompactJsonFormatter>();
        sink.RestrictedToMinimumLevel.ShouldBe(LogEventLevel.Verbose);
        sink.EmitEventFailure.ShouldBe(EmitEventFailureHandling.WriteToSelfLog);
    }

    [Fact]
    public void BuildWriteToConfigurations_LeavesSslOptionNull_WhenSslEnabledWithoutServerName()
    {
        // Documented edge case: sslEnabled=true but sslServerName=null → SslOption is NOT
        // constructed (effectively disabling SSL). The previous coverage-only test could
        // only assert `logger.ShouldNotBeNull()`; here we assert the actual outcome.
        var (client, _) = LoggerConfigurationRabbitMQExtensions.BuildWriteToConfigurations(
            hostnames: ["host-a"],
            username: "user",
            password: "pass",
            exchange: null,
            exchangeType: null,
            deliveryMode: RabbitMQDeliveryMode.NonDurable,
            routingKey: null,
            port: 0,
            vHost: null,
            clientProvidedName: null,
            heartbeat: 0,
            sslEnabled: true,
            sslServerName: null,
            sslVersion: SslProtocols.None,
            sslAcceptablePolicyErrors: SslPolicyErrors.None,
            sslCheckCertificateRevocation: false,
            batchPostingLimit: 50,
            bufferingTimeLimit: TimeSpan.FromSeconds(2),
            queueLimit: null,
            formatter: null,
            autoCreateExchange: false,
            channelCount: 64,
            levelSwitch: LogEventLevel.Verbose,
            emitEventFailure: EmitEventFailureHandling.WriteToSelfLog,
            sendMessageEvents: null);

        client.SslOption.ShouldBeNull();
    }

    [Fact]
    public void BuildAuditToConfigurations_MapsEveryOption_WhenAllSet()
    {
        var formatter = Substitute.For<ITextFormatter>();
        var sendMessageEvents = Substitute.For<ISendMessageEvents>();

        var (client, sink) = LoggerConfigurationRabbitMQExtensions.BuildAuditToConfigurations(
            hostnames: ["host-a", "host-b"],
            username: "user",
            password: "pass",
            exchange: "audit",
            exchangeType: "direct",
            deliveryMode: RabbitMQDeliveryMode.Durable,
            routingKey: "warn",
            port: 5673,
            vHost: "/audit",
            clientProvidedName: "audit-to-test",
            heartbeat: 45,
            sslEnabled: true,
            sslServerName: "audit.example.com",
            sslVersion: SslProtocols.Tls13,
            sslAcceptablePolicyErrors: SslPolicyErrors.RemoteCertificateChainErrors,
            sslCheckCertificateRevocation: true,
            formatter: formatter,
            autoCreateExchange: true,
            channelCount: 3,
            levelSwitch: LogEventLevel.Error,
            sendMessageEvents: sendMessageEvents);

        client.Hostnames.ShouldBe(["host-a", "host-b"]);
        client.Username.ShouldBe("user");
        client.Password.ShouldBe("pass");
        client.Exchange.ShouldBe("audit");
        client.ExchangeType.ShouldBe("direct");
        client.DeliveryMode.ShouldBe(RabbitMQDeliveryMode.Durable);
        client.RoutingKey.ShouldBe("warn");
        client.Port.ShouldBe(5673);
        client.VHost.ShouldBe("/audit");
        client.ClientProvidedName.ShouldBe("audit-to-test");
        client.Heartbeat.ShouldBe((ushort)45);
        client.AutoCreateExchange.ShouldBeTrue();
        client.ChannelCount.ShouldBe(3);
        client.SendMessageEvents.ShouldBeSameAs(sendMessageEvents);

        client.SslOption.ShouldNotBeNull();
        client.SslOption!.Enabled.ShouldBeTrue();
        client.SslOption.ServerName.ShouldBe("audit.example.com");
        client.SslOption.Version.ShouldBe(SslProtocols.Tls13);
        client.SslOption.AcceptablePolicyErrors.ShouldBe(SslPolicyErrors.RemoteCertificateChainErrors);
        client.SslOption.CheckCertificateRevocation.ShouldBeTrue();

        // Audit builder never populates batching fields — callers cannot pass them.
        // Property-initializer defaults survive.
        sink.BatchPostingLimit.ShouldBe(50);
        sink.BufferingTimeLimit.ShouldBe(TimeSpan.FromSeconds(2));
        sink.QueueLimit.ShouldBeNull();
        sink.TextFormatter.ShouldBeSameAs(formatter);
        sink.RestrictedToMinimumLevel.ShouldBe(LogEventLevel.Error);
    }

    [Fact]
    public void BuildAuditToConfigurations_AppliesDefaults_WhenOptionalArgsAreNullOrDefault()
    {
        var (client, sink) = LoggerConfigurationRabbitMQExtensions.BuildAuditToConfigurations(
            hostnames: ["host-a"],
            username: "user",
            password: "pass",
            exchange: null,
            exchangeType: null,
            deliveryMode: RabbitMQDeliveryMode.NonDurable,
            routingKey: null,
            port: 0,
            vHost: null,
            clientProvidedName: null,
            heartbeat: 0,
            sslEnabled: false,
            sslServerName: null,
            sslVersion: SslProtocols.None,
            sslAcceptablePolicyErrors: SslPolicyErrors.None,
            sslCheckCertificateRevocation: false,
            formatter: null,
            autoCreateExchange: false,
            channelCount: 64,
            levelSwitch: LogEventLevel.Verbose,
            sendMessageEvents: null);

        client.Exchange.ShouldBe(string.Empty);
        client.ExchangeType.ShouldBe(ExchangeType.Fanout);
        client.RoutingKey.ShouldBe(string.Empty);
        client.VHost.ShouldBe(string.Empty);
        client.ClientProvidedName.ShouldBeNull();
        client.SendMessageEvents.ShouldNotBeNull();
        client.SendMessageEvents.ShouldBeOfType<SendMessageEvents>();
        client.SslOption.ShouldBeNull();

        sink.TextFormatter.ShouldBeOfType<CompactJsonFormatter>();
        sink.RestrictedToMinimumLevel.ShouldBe(LogEventLevel.Verbose);
    }

    [Fact]
    public void BuildAuditToConfigurations_LeavesSslOptionNull_WhenSslEnabledWithoutServerName()
    {
        var (client, _) = LoggerConfigurationRabbitMQExtensions.BuildAuditToConfigurations(
            hostnames: ["host-a"],
            username: "user",
            password: "pass",
            exchange: null,
            exchangeType: null,
            deliveryMode: RabbitMQDeliveryMode.NonDurable,
            routingKey: null,
            port: 0,
            vHost: null,
            clientProvidedName: null,
            heartbeat: 0,
            sslEnabled: true,
            sslServerName: null,
            sslVersion: SslProtocols.None,
            sslAcceptablePolicyErrors: SslPolicyErrors.None,
            sslCheckCertificateRevocation: false,
            formatter: null,
            autoCreateExchange: false,
            channelCount: 64,
            levelSwitch: LogEventLevel.Verbose,
            sendMessageEvents: null);

        client.SslOption.ShouldBeNull();
    }
}
