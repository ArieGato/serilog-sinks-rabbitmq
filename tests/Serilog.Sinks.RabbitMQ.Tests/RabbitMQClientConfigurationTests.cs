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

    private static RabbitMQClientConfiguration ValidSample() => new()
    {
        Hostnames = ["localhost"],
        Username = "guest",
        Password = "guest",
        Port = 5672,
    };

    [Fact]
    public void Validate_DoesNotThrow_WhenConfigurationIsValid()
    {
        var sut = ValidSample();

        Should.NotThrow(sut.Validate);
    }

    [Fact]
    public void Validate_Throws_WhenHostnamesIsEmpty()
    {
        var sut = ValidSample();
        sut.Hostnames = [];

        Should.Throw<ArgumentException>(sut.Validate).Message.ShouldContain("hostnames");
    }

    [Fact]
    public void Validate_Throws_WhenHostnamesIsNull()
    {
        // Covers the `Hostnames is null` short-circuit in Validate(). A nullable-disabled
        // caller can assign null even though the property type is non-nullable.
        var sut = ValidSample();
        sut.Hostnames = null!;

        Should.Throw<ArgumentException>(sut.Validate).Message.ShouldContain("hostnames");
    }

    [Fact]
    public void Validate_Throws_WhenUsernameIsNullOrEmpty()
    {
        var sut = ValidSample();
        sut.Username = string.Empty;

        Should.Throw<ArgumentException>(sut.Validate).Message.ShouldContain("username");
    }

    [Fact]
    public void Validate_Throws_WhenPasswordIsNull()
    {
        var sut = ValidSample();
        sut.Password = null!;

        Should.Throw<ArgumentException>(sut.Validate).Message.ShouldContain("password");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_Throws_WhenPortIsOutOfRange(int port)
    {
        var sut = ValidSample();
        sut.Port = port;

        Should.Throw<ArgumentOutOfRangeException>(sut.Validate).ParamName.ShouldBe("Port");
    }

    [Fact]
    public void Validate_IsIdempotent_WhenCalledRepeatedlyOnValidConfiguration()
    {
        var sut = ValidSample();

        Should.NotThrow(sut.Validate);
        Should.NotThrow(sut.Validate);
    }
}
