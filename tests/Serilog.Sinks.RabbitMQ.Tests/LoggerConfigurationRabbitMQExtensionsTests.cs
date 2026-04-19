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
}
