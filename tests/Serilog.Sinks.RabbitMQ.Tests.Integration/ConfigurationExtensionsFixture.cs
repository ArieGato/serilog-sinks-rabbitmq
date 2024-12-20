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
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Serilog.Sinks.RabbitMQ.Tests.Integration;

[Collection("Sequential")]
public class ConfigurationExtensionsFixture : IClassFixture<RabbitMQFixture>
{
    private readonly RabbitMQFixture _rabbitMQFixture;

    public ConfigurationExtensionsFixture(RabbitMQFixture rabbitMQFixture)
    {
        _rabbitMQFixture = rabbitMQFixture;
    }

    [Fact]
    public async Task ReadFromAppSettings_ShouldNotThrow()
    {
        var loggerConfiguration = new LoggerConfiguration();
        var logger = loggerConfiguration.ReadFrom.AppSettings(settingPrefix: "W")
            .CreateLogger();

        using var cleanupModel = await _rabbitMQFixture.GetConsumingModelAsync();
        await cleanupModel.ExchangeDeleteAsync("serilog-settings-sink-exchange");
        await cleanupModel.CloseAsync();

        // should not throw
        logger.Dispose();
    }

    [Fact]
    public async Task ReadFromAppSettings_ShouldNotThrow_WhenMultipleHostsAreConfigured()
    {
        var loggerConfiguration = new LoggerConfiguration();
        var logger = loggerConfiguration.ReadFrom.AppSettings(settingPrefix: "H")
            .CreateLogger();

        using var cleanupModel = await _rabbitMQFixture.GetConsumingModelAsync();
        await cleanupModel.ExchangeDeleteAsync("serilog-settings-sink-exchange");
        await cleanupModel.CloseAsync();

        // should not throw
        logger.Dispose();
    }

    [Fact]
    public async Task AuditFromAppSettings_ShouldNotThrow()
    {
        var loggerConfiguration = new LoggerConfiguration();
        var logger = loggerConfiguration.ReadFrom.AppSettings(settingPrefix: "A")
            .CreateLogger();

        using var cleanupModel = await _rabbitMQFixture.GetConsumingModelAsync();
        await cleanupModel.ExchangeDeleteAsync("serilog-settings-sink-audit-exchange");
        await cleanupModel.CloseAsync();

        // should not throw
        logger.Dispose();
    }

    [Fact]
    public void WriteWithProperties_ShouldNotThrow()
    {
        var loggerConfiguration = new LoggerConfiguration();
        var logger = loggerConfiguration.WriteTo.RabbitMQ(
                hostnames: [RabbitMQFixture.SslCertHostName],
                username: RabbitMQFixture.UserName,
                password: RabbitMQFixture.Password,
                exchange: RabbitMQFixture.SerilogSinkExchange,
                exchangeType: RabbitMQFixture.SerilogSinkExchangeType,
                deliveryMode: RabbitMQDeliveryMode.Durable,
                sslEnabled: true,
                sslAcceptablePolicyErrors: SslPolicyErrors.RemoteCertificateNameMismatch,
                sslServerName: "localhost",
                sslCheckCertificateRevocation: false,
                sslVersion: SslProtocols.Tls13,
                formatter: new RenderedCompactJsonFormatter())
            .CreateLogger();

        // should not throw
        logger.Dispose();
    }

    [Fact]
    public void AuditWithProperties_ShouldNotThrow()
    {
        var loggerConfiguration = new LoggerConfiguration();
        var logger = loggerConfiguration.AuditTo.RabbitMQ(
                hostnames: [RabbitMQFixture.SslCertHostName],
                username: RabbitMQFixture.UserName,
                password: RabbitMQFixture.Password,
                exchange: RabbitMQFixture.SerilogAuditSinkExchange,
                exchangeType: RabbitMQFixture.SerilogAuditSinkExchangeType,
                deliveryMode: RabbitMQDeliveryMode.Durable,
                sslEnabled: true,
                sslAcceptablePolicyErrors: SslPolicyErrors.RemoteCertificateNameMismatch,
                sslServerName: "localhost",
                sslCheckCertificateRevocation: false,
                sslVersion: SslProtocols.Tls13,
                formatter: new RenderedCompactJsonFormatter())
            .CreateLogger();

        // should not throw
        logger.Dispose();
    }

    [Fact]
    public void WriteToWithConfigurationObjects_ShouldNotThrow()
    {
        var loggerConfiguration = new LoggerConfiguration();

        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration
        {
            AutoCreateExchange = true,
            DeliveryMode = RabbitMQDeliveryMode.Durable,
            Exchange = RabbitMQFixture.SerilogSinkExchange,
            ExchangeType = RabbitMQFixture.SerilogSinkExchangeType,
            Heartbeat = 21,
            Hostnames = [RabbitMQFixture.SslCertHostName],
            MaxChannels = 32,
            Password = RabbitMQFixture.Password,
            Port = 5672,
            RoutingKey = string.Empty,
            SslOption = new SslOption
            {
                AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch,
                Enabled = false,
                ServerName = "localhost",
                CertPassphrase = "secret",
                CertPath = "path",
                Version = SslProtocols.Tls13,
                CheckCertificateRevocation = true,
            },
            Username = RabbitMQFixture.UserName,
            VHost = "/",
        };

        var rabbitMQSinkConfiguration = new RabbitMQSinkConfiguration
        {
            BatchPostingLimit = 100,
            BufferingTimeLimit = TimeSpan.FromSeconds(3),
            QueueLimit = 1000,
            EmitEventFailure = EmitEventFailureHandling.ThrowException,
            TextFormatter = new JsonFormatter(),
            RestrictedToMinimumLevel = LogEventLevel.Information,
        };

        var logger = loggerConfiguration.WriteTo.RabbitMQ(
                rabbitMQClientConfiguration,
                rabbitMQSinkConfiguration,
                configuration => configuration.Console())
            .CreateLogger();

        // should not throw
        logger.Dispose();
    }

    [Fact]
    public void AuditWithConfigurationObjects_ShouldNotThrow()
    {
        var loggerConfiguration = new LoggerConfiguration();

        var rabbitMQClientConfiguration = new RabbitMQClientConfiguration
        {
            AutoCreateExchange = true,
            DeliveryMode = RabbitMQDeliveryMode.Durable,
            Exchange = RabbitMQFixture.SerilogSinkExchange,
            ExchangeType = RabbitMQFixture.SerilogSinkExchangeType,
            Heartbeat = 21,
            Hostnames = [RabbitMQFixture.SslCertHostName],
            MaxChannels = 32,
            Password = RabbitMQFixture.Password,
            Port = 5672,
            RoutingKey = string.Empty,
            SslOption = new SslOption
            {
                AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch,
                Enabled = false,
                ServerName = "localhost",
                CertPassphrase = "secret",
                CertPath = "path",
                Version = SslProtocols.Tls13,
                CheckCertificateRevocation = true,
            },
            Username = RabbitMQFixture.UserName,
            VHost = "/",
        };

        var rabbitMQSinkConfiguration = new RabbitMQSinkConfiguration
        {
            TextFormatter = new JsonFormatter(),
            RestrictedToMinimumLevel = LogEventLevel.Information,
        };

        var logger = loggerConfiguration.AuditTo.RabbitMQ(rabbitMQClientConfiguration, rabbitMQSinkConfiguration)
            .CreateLogger();

        // should not throw
        logger.Dispose();
    }

    [Fact]
    public void AuditToWithAction_ShouldNotThrow()
    {
        var loggerConfiguration = new LoggerConfiguration();
        var logger = loggerConfiguration.AuditTo.RabbitMQ(
                (rabbitMQClientConfiguration, rabbitMQSinkConfiguration) =>
                {
                    rabbitMQClientConfiguration.AutoCreateExchange = true;
                    rabbitMQClientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
                    rabbitMQClientConfiguration.Exchange = RabbitMQFixture.SerilogSinkExchange;
                    rabbitMQClientConfiguration.ExchangeType = RabbitMQFixture.SerilogSinkExchangeType;
                    rabbitMQClientConfiguration.Heartbeat = 21;
                    rabbitMQClientConfiguration.Hostnames = [RabbitMQFixture.SslCertHostName];
                    rabbitMQClientConfiguration.MaxChannels = 32;
                    rabbitMQClientConfiguration.Password = RabbitMQFixture.Password;
                    rabbitMQClientConfiguration.Port = 5672;
                    rabbitMQClientConfiguration.RoutingKey = string.Empty;
                    rabbitMQClientConfiguration.SslOption = new SslOption
                    {
                        AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch,
                        Enabled = false,
                        ServerName = "localhost",
                        CertPassphrase = "secret",
                        CertPath = "path",
                        Version = SslProtocols.Tls13,
                        CheckCertificateRevocation = true,
                    };
                    rabbitMQClientConfiguration.Username = RabbitMQFixture.UserName;
                    rabbitMQClientConfiguration.VHost = "/";

                    rabbitMQSinkConfiguration.TextFormatter = new JsonFormatter();
                    rabbitMQSinkConfiguration.RestrictedToMinimumLevel = LogEventLevel.Information;
                })
            .CreateLogger();

        // should not throw
        logger.Dispose();
    }

    [Fact]
    public async Task WriteTo_ShouldNotThrow()
    {
        var loggerConfiguration = new LoggerConfiguration();
        var logger = loggerConfiguration.WriteTo.RabbitMQ(
                (rabbitMQClientConfiguration, rabbitMQSinkConfiguration) =>
                {
                    rabbitMQClientConfiguration.AutoCreateExchange = true;
                    rabbitMQClientConfiguration.DeliveryMode = RabbitMQDeliveryMode.Durable;
                    rabbitMQClientConfiguration.Exchange = RabbitMQFixture.SerilogSinkExchange;
                    rabbitMQClientConfiguration.ExchangeType = RabbitMQFixture.SerilogSinkExchangeType;
                    rabbitMQClientConfiguration.Heartbeat = 21;
                    rabbitMQClientConfiguration.Hostnames = [RabbitMQFixture.SslCertHostName];
                    rabbitMQClientConfiguration.MaxChannels = 32;
                    rabbitMQClientConfiguration.Password = RabbitMQFixture.Password;
                    rabbitMQClientConfiguration.Port = 5672;
                    rabbitMQClientConfiguration.RoutingKey = string.Empty;
                    rabbitMQClientConfiguration.Username = RabbitMQFixture.UserName;
                    rabbitMQClientConfiguration.VHost = "/";

                    rabbitMQSinkConfiguration.TextFormatter = new JsonFormatter();
                    rabbitMQSinkConfiguration.BatchPostingLimit = 50;
                    rabbitMQSinkConfiguration.BufferingTimeLimit = TimeSpan.FromSeconds(5);
                    rabbitMQSinkConfiguration.RestrictedToMinimumLevel = LogEventLevel.Information;
                    rabbitMQSinkConfiguration.EmitEventFailure = EmitEventFailureHandling.WriteToFailureSink |
                                                                 EmitEventFailureHandling.ThrowException;
                },
                failureSinkConfiguration => failureSinkConfiguration.Console())
            .CreateLogger();

        // Actually log something to trigger the exchange creation
        logger.Information("Some text");

        await Task.Delay(1000); // wait batch execution

        // should not throw
        logger.Dispose();
    }

    [Fact]
    public void WriteToRabbitMQ_ShouldThrowException_WhenHostNamesIsEmpty()
    {
        var loggerConfiguration = new LoggerConfiguration();

        var act = () => loggerConfiguration.WriteTo.RabbitMQ(
            hostnames: [],
            username: "username",
            password: "password",
            port: 5672);

        var ex = Should.Throw<ArgumentException>(act);
        ex.Message.ShouldBe("hostnames cannot be empty, specify at least one hostname");
    }

    [Fact]
    public void WriteToRabbitMQ_ShouldThrowException_WhenUsernameIsEmpty()
    {
        var loggerConfiguration = new LoggerConfiguration();

        var act = () => loggerConfiguration.WriteTo.RabbitMQ(
            hostnames: ["localhost"],
            username: null!,
            password: "password",
            port: 5672);

        var ex = Should.Throw<ArgumentException>(act);
        ex.Message.ShouldBe("username cannot be 'null' or and empty string.");
    }

    [Fact]
    public void WriteToRabbitMQ_ShouldThrowException_WhenPasswordIsNull()
    {
        var loggerConfiguration = new LoggerConfiguration();

        var act = () => loggerConfiguration.WriteTo.RabbitMQ(
            hostnames: ["localhost"],
            username: "username",
            password: null!,
            port: 5672);

        var ex = Should.Throw<ArgumentException>(act);
        ex.Message.ShouldBe("password cannot be 'null'. Specify an empty string if password is empty.");
    }

    [Fact]
    public void WriteToRabbitMQ_ShouldThrowException_WhenPortNumberIsZero()
    {
        var loggerConfiguration = new LoggerConfiguration();

        var act = () => loggerConfiguration.WriteTo.RabbitMQ(
            hostnames: ["localhost"],
            username: "username",
            password: "password",
            port: -1);

        var ex = Should.Throw<ArgumentException>(act);
        ex.Message.ShouldStartWith("port must be in a valid range (1 and 65535)");
        ex.ParamName.ShouldBe("Port");
    }

    [Fact]
    public void WriteToRabbitMQ_ShouldThrowException_WhenPortNumberIsGreaterThan65535()
    {
        var loggerConfiguration = new LoggerConfiguration();

        var act = () => loggerConfiguration.WriteTo.RabbitMQ(
            hostnames: ["localhost"],
            username: "username",
            password: "password",
            port: 65536);

        var ex = Should.Throw<ArgumentException>(act);
        ex.Message.ShouldStartWith("port must be in a valid range (1 and 65535)");
        ex.ParamName.ShouldBe("Port");
    }
}
