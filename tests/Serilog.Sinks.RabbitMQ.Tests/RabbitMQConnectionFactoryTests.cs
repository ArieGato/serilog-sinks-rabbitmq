namespace Serilog.Sinks.RabbitMQ.Tests;

public class RabbitMQConnectionFactoryTests
{
    private static RabbitMQClientConfiguration SslSample(params string[] hostnames) => new()
    {
        Hostnames = hostnames.ToList(),
        Username = "guest",
        Password = "guest",
        SslOption = new SslOption { Enabled = true },
    };

    [Fact]
    public void GetAmqpTcpEndpoints_EachEndpointHasServerNameMatchingItsOwnHostname_WhenNotExplicitlySet()
    {
        // Bug reproducer for issue #289. Prior behaviour: the SslOption was shared by
        // reference across every endpoint, and the first iteration's ServerName
        // assignment leaked into later iterations. Second endpoint's Ssl.ServerName
        // ended up equal to the FIRST hostname.
        var configuration = SslSample("rabbit-a.example.com", "rabbit-b.example.com");
        using var cts = new CancellationTokenSource();
        var sut = new RabbitMQConnectionFactory(configuration, cts);

        var endpoints = sut.GetAmqpTcpEndpoints().ToArray();

        endpoints.Length.ShouldBe(2);
        endpoints[0].Ssl.ServerName.ShouldBe("rabbit-a.example.com");
        endpoints[1].Ssl.ServerName.ShouldBe("rabbit-b.example.com");
    }

    [Fact]
    public void GetAmqpTcpEndpoints_EachEndpointHasIndependentSslOption()
    {
        // The returned endpoints must not share a single SslOption reference; otherwise
        // any downstream mutation bleeds across connections.
        var configuration = SslSample("rabbit-a.example.com", "rabbit-b.example.com");
        using var cts = new CancellationTokenSource();
        var sut = new RabbitMQConnectionFactory(configuration, cts);

        var endpoints = sut.GetAmqpTcpEndpoints().ToArray();

        ReferenceEquals(endpoints[0].Ssl, endpoints[1].Ssl).ShouldBeFalse();
    }

    [Fact]
    public void GetAmqpTcpEndpoints_DoesNotMutateCallerProvidedSslOption()
    {
        // The caller's SslOption.ServerName must stay as they left it. Currently the
        // old code writes through the shared reference and silently updates this to
        // the first hostname.
        var configuration = SslSample("rabbit-a.example.com", "rabbit-b.example.com");
        using var cts = new CancellationTokenSource();
        var sut = new RabbitMQConnectionFactory(configuration, cts);

        _ = sut.GetAmqpTcpEndpoints().ToArray();

        configuration.SslOption!.ServerName.ShouldBeNullOrEmpty();
    }

    [Fact]
    public void GetAmqpTcpEndpoints_AppliesConfiguredPortToEveryEndpoint()
    {
        // Covers the Port override branch in GetAmqpTcpEndpoints so the coverage
        // gate for a method we modified stays green.
        var configuration = SslSample("rabbit-a.example.com", "rabbit-b.example.com");
        configuration.Port = 5673;
        using var cts = new CancellationTokenSource();
        var sut = new RabbitMQConnectionFactory(configuration, cts);

        var endpoints = sut.GetAmqpTcpEndpoints().ToArray();

        endpoints[0].Port.ShouldBe(5673);
        endpoints[1].Port.ShouldBe(5673);
    }

    [Fact]
    public void GetAmqpTcpEndpoints_RespectsExplicitServerName_ForAllEndpoints()
    {
        // When the user explicitly sets ServerName (e.g. shared wildcard cert CN),
        // every endpoint should still use that value. This matches the prior behaviour
        // for that specific case; the fix must not regress it.
        var configuration = SslSample("rabbit-a.example.com", "rabbit-b.example.com");
        configuration.SslOption!.ServerName = "cluster.example.com";
        using var cts = new CancellationTokenSource();
        var sut = new RabbitMQConnectionFactory(configuration, cts);

        var endpoints = sut.GetAmqpTcpEndpoints().ToArray();

        endpoints[0].Ssl.ServerName.ShouldBe("cluster.example.com");
        endpoints[1].Ssl.ServerName.ShouldBe("cluster.example.com");
    }
}
