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
            RoutingKey = "log",
            DeliveryMode = RabbitMQDeliveryMode.NonDurable,
            ChannelCount = 65,
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
                CheckCertificateRevocation = true,
            },
        };

        // Act
        var copy = original.Clone();

        // Assert
        copy.ShouldBeEquivalentTo(original);
    }

    [Fact]
#pragma warning disable CS0618 // Type or member is obsolete
    public void MaxChannels_Setter_UpdatesChannelCount()
    {
        var sut = new RabbitMQClientConfiguration { MaxChannels = 17 };

        sut.ChannelCount.ShouldBe(17);
    }

    [Fact]
    public void ChannelCount_Setter_UpdatesMaxChannels()
    {
        var sut = new RabbitMQClientConfiguration { ChannelCount = 23 };

        sut.MaxChannels.ShouldBe(23);
    }
#pragma warning restore CS0618
}
