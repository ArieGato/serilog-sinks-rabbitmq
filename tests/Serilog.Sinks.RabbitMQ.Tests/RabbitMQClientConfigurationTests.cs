using System.Net.Security;
using System.Security.Authentication;

namespace Serilog.Sinks.RabbitMQ.Tests;

public class RabbitMQClientConfigurationTests
{
    [Fact]
    public void From_ShouldCopyAllProperties()
    {
        // Arrange
        var original = new RabbitMQClientConfiguration()
        {
            Username = "serilog",
            Password = "serilog",
            VHost = "/",
            Exchange = "logs",
            ExchangeType = "direct",
            RouteKey = "log",
            DeliveryMode = RabbitMQDeliveryMode.NonDurable,
            MaxChannels = 65,
            Port = 5673,
            AutoCreateExchange = true,
            Heartbeat = 21,
            Hostnames = ["localhost", "127.0.0.1"],
            SslOption = new SslOption
            {
                AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch,
                Enabled = true,
                ServerName = "localhost",
                CertPassphrase = "secret",
                CertPath = "path",
                Version = SslProtocols.Tls13,
                CheckCertificateRevocation = true
            }
        };

        // Act
        var copy = new RabbitMQClientConfiguration().From(original);

        // Assert
        copy.ShouldBeEquivalentTo(original);
    }
}
