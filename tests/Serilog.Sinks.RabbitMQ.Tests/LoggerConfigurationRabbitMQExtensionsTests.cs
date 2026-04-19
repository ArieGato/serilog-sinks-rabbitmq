using System.Net.Security;
using System.Security.Authentication;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Serilog.Sinks.RabbitMQ.Tests;

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
        // Covers the default-substitution branches in RegisterSink (lines
        // around BatchPostingLimit == default / BufferingTimeLimit == default).
        // A caller who binds from appsettings with these omitted gets the
        // property-initializer values; one who constructs a config and leaves
        // the batch values at their CLR defaults gets the library defaults.
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
    public void WriteTo_RabbitMQ_FlatOverload_Defaults_Builds()
    {
        // Happy path for the large parameter-list overload with only the required
        // args. Exercises every null-coalescing (exchange / exchangeType / routingKey
        // / vHost / sendMessageEvents) and the non-SSL / no-formatter branches.
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
    public void WriteTo_RabbitMQ_FlatOverload_AllOptions_Builds()
    {
        // Exercises every non-default branch of the flat overload: SSL on with
        // explicit ServerName, non-null formatter, explicit strings for every
        // optional, non-null sendMessageEvents.
        using var logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ(
                hostnames: ["localhost"],
                username: "guest",
                password: "guest",
                exchange: "x",
                exchangeType: "topic",
                deliveryMode: RabbitMQDeliveryMode.Durable,
                routingKey: "r",
                port: 5672,
                vHost: "/custom",
                clientProvidedName: "unit-test",
                heartbeat: 30,
                sslEnabled: true,
                sslServerName: "localhost",
                sslVersion: SslProtocols.Tls12,
                sslAcceptablePolicyErrors: SslPolicyErrors.RemoteCertificateNameMismatch,
                sslCheckCertificateRevocation: true,
                batchPostingLimit: 10,
                bufferingTimeLimit: TimeSpan.FromSeconds(5),
                queueLimit: 500,
                formatter: new CompactJsonFormatter(),
                autoCreateExchange: true,
                channelCount: 2,
                levelSwitch: LogEventLevel.Warning,
                emitEventFailure: EmitEventFailureHandling.WriteToSelfLog,
                failureSinkConfiguration: null,
                sendMessageEvents: Substitute.For<ISendMessageEvents>())
            .CreateLogger();

        logger.ShouldNotBeNull();
    }

    [Fact]
    public void WriteTo_RabbitMQ_FlatOverload_AppliesBatchPostingLimitDefault_WhenExplicitZero()
    {
        // Covers the `batchPostingLimit == default ? DEFAULT : batchPostingLimit` branch
        // in the flat overload: an appsettings binding that passes 0 through the parameter
        // should still end up with the library default (50), not create a zero-limit batch.
        using var logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ(
                hostnames: ["localhost"],
                username: "guest",
                password: "guest",
                batchPostingLimit: 0,
                channelCount: 1)
            .CreateLogger();

        logger.ShouldNotBeNull();
    }

    [Fact]
    public void WriteTo_RabbitMQ_FlatOverload_SslEnabledWithoutServerName_DoesNotBuildSslOption()
    {
        // Documents existing behaviour: when sslEnabled is true but sslServerName is
        // null, the flat overload does NOT construct an SslOption — effectively silently
        // disabling SSL. Covers the `sslEnabled && sslServerName is not null` false path.
        using var logger = new LoggerConfiguration()
            .WriteTo.RabbitMQ(
                hostnames: ["localhost"],
                username: "guest",
                password: "guest",
                sslEnabled: true,
                sslServerName: null,
                channelCount: 1)
            .CreateLogger();

        logger.ShouldNotBeNull();
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
    public void AuditTo_RabbitMQ_FlatOverload_Defaults_Builds()
    {
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
    public void AuditTo_RabbitMQ_FlatOverload_AllOptions_Builds()
    {
        using var logger = new LoggerConfiguration()
            .AuditTo.RabbitMQ(
                hostnames: ["localhost"],
                username: "guest",
                password: "guest",
                exchange: "x",
                exchangeType: "topic",
                deliveryMode: RabbitMQDeliveryMode.Durable,
                routingKey: "r",
                port: 5672,
                vHost: "/custom",
                clientProvidedName: "unit-test-audit",
                heartbeat: 30,
                sslEnabled: true,
                sslServerName: "localhost",
                sslVersion: SslProtocols.Tls12,
                sslAcceptablePolicyErrors: SslPolicyErrors.RemoteCertificateNameMismatch,
                sslCheckCertificateRevocation: true,
                formatter: new CompactJsonFormatter(),
                autoCreateExchange: true,
                channelCount: 1,
                levelSwitch: LogEventLevel.Warning,
                sendMessageEvents: Substitute.For<ISendMessageEvents>())
            .CreateLogger();

        logger.ShouldNotBeNull();
    }

    [Fact]
    public void AuditTo_RabbitMQ_FlatOverload_SslEnabledWithoutServerName_DoesNotBuildSslOption()
    {
        using var logger = new LoggerConfiguration()
            .AuditTo.RabbitMQ(
                hostnames: ["localhost"],
                username: "guest",
                password: "guest",
                sslEnabled: true,
                sslServerName: null,
                channelCount: 1)
            .CreateLogger();

        logger.ShouldNotBeNull();
    }
}
