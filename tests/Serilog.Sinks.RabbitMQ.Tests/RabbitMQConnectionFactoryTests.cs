using System.Net.Security;
using System.Reflection;
using System.Security.Authentication;
using Serilog.Sinks.RabbitMQ.Tests.TestHelpers;

namespace Serilog.Sinks.RabbitMQ.Tests;

[Collection("SelfLog")]
public class RabbitMQConnectionFactoryTests
{
    private static readonly FieldInfo ConnectionField =
        typeof(RabbitMQConnectionFactory).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("RabbitMQConnectionFactory._connection field not found.");

    private static RabbitMQClientConfiguration SslSample(params string[] hostnames) => new()
    {
        Hostnames = hostnames.ToList(),
        Username = "guest",
        Password = "guest",
        SslOption = new SslOption { Enabled = true },
    };

    private static RabbitMQClientConfiguration PlainSample(params string[] hostnames) => new()
    {
        Hostnames = hostnames.Length == 0 ? ["localhost"] : hostnames.ToList(),
        Username = "guest",
        Password = "guest",
    };

    private static RabbitMQConnectionFactory Build(RabbitMQClientConfiguration configuration) =>
        new(configuration, new CancellationTokenSource());

    private static void SetCachedConnection(RabbitMQConnectionFactory sut, IConnection? connection) =>
        ConnectionField.SetValue(sut, connection);

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

    [Fact]
    public void GetConnectionFactory_EnablesAutomaticRecovery_ByDefault()
    {
        var sut = Build(PlainSample());

        var factory = sut.GetConnectionFactory();

        factory.AutomaticRecoveryEnabled.ShouldBeTrue();
        factory.NetworkRecoveryInterval.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GetConnectionFactory_AppliesCredentials_WhenMinimalConfig()
    {
        var sut = Build(PlainSample());

        var factory = sut.GetConnectionFactory();

        factory.UserName.ShouldBe("guest");
        factory.Password.ShouldBe("guest");

        // RabbitMQ.Client uses -1 as the "unset" sentinel for Port and resolves it to
        // the protocol-default (5672) at connect time. Asserting -1 here proves the
        // `Port > 0` branch in GetConnectionFactory did not fire for the minimal config.
        factory.Port.ShouldBe(-1);
    }

    [Fact]
    public void GetConnectionFactory_SetsClientProvidedName_WhenConfigured()
    {
        var configuration = PlainSample();
        configuration.ClientProvidedName = "audit-sink";
        var sut = Build(configuration);

        sut.GetConnectionFactory().ClientProvidedName.ShouldBe("audit-sink");
    }

    [Fact]
    public void GetConnectionFactory_SetsRequestedHeartbeat_WhenPositive()
    {
        var configuration = PlainSample();
        configuration.Heartbeat = 45000;
        var sut = Build(configuration);

        sut.GetConnectionFactory().RequestedHeartbeat.ShouldBe(TimeSpan.FromMilliseconds(45000));
    }

    [Fact]
    public void GetConnectionFactory_SetsVirtualHost_WhenProvided()
    {
        var configuration = PlainSample();
        configuration.VHost = "/tenant-a";
        var sut = Build(configuration);

        sut.GetConnectionFactory().VirtualHost.ShouldBe("/tenant-a");
    }

    [Fact]
    public void GetConnectionFactory_AppliesPort_WhenPositive()
    {
        var configuration = PlainSample();
        configuration.Port = 5673;
        var sut = Build(configuration);

        sut.GetConnectionFactory().Port.ShouldBe(5673);
    }

    [Fact]
    public void GetConnectionFactory_SetsHostName_ForSingleHostname()
    {
        var sut = Build(PlainSample("rabbit-a.example.com"));

        sut.GetConnectionFactory().HostName.ShouldBe("rabbit-a.example.com");
    }

    [Fact]
    public void GetConnectionFactory_LeavesHostNameUnset_ForMultipleHostnames()
    {
        // Multi-host cluster: HostName is not populated — the endpoint list drives the
        // connection attempts. Asserting the default (localhost) proves the branch that
        // sets it for the single-host case did not fire.
        var sut = Build(PlainSample("rabbit-a.example.com", "rabbit-b.example.com"));

        sut.GetConnectionFactory().HostName.ShouldBe("localhost");
    }

    [Fact]
    public void GetConnectionFactory_WiresSslOption_WhenConfigured()
    {
        var configuration = SslSample("rabbit-a.example.com");
        configuration.SslOption!.Version = SslProtocols.Tls12;
        configuration.SslOption.AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch;
        var sut = Build(configuration);

        var factory = sut.GetConnectionFactory();

        factory.Ssl.Enabled.ShouldBeTrue();
        factory.Ssl.Version.ShouldBe(SslProtocols.Tls12);
        factory.Ssl.AcceptablePolicyErrors.ShouldBe(SslPolicyErrors.RemoteCertificateNameMismatch);
    }

    [Fact]
    public void GetConnectionFactory_SelectsExternalMechanism_WhenSslCertPathProvided()
    {
        var configuration = SslSample("rabbit-a.example.com");
        configuration.SslOption!.CertPath = "/etc/ssl/client.pem";
        var sut = Build(configuration);

        var mechanism = sut.GetConnectionFactory().AuthMechanisms.ShouldHaveSingleItem();
        mechanism.ShouldBeOfType<ExternalMechanismFactory>();
    }

    [Fact]
    public void GetConnectionFactory_UsesDefaultAuthMechanisms_WhenSslHasNoCertPath()
    {
        var configuration = SslSample("rabbit-a.example.com");
        var sut = Build(configuration);

        // No ExternalMechanismFactory override — the client-library default chain applies.
        sut.GetConnectionFactory().AuthMechanisms.ShouldNotContain(m => m is ExternalMechanismFactory);
    }

    [Fact]
    public async Task GetConnectionAsync_ReturnsCachedConnection_WhenAlreadyOpened()
    {
        // Fast-path: when _connection is already set, GetConnectionAsync short-circuits
        // before taking the semaphore. Covers the pre-lock return in GetConnectionAsync
        // without requiring a live broker.
        var sut = Build(PlainSample());
        var cached = Substitute.For<IConnection>();
        SetCachedConnection(sut, cached);

        var result = await sut.GetConnectionAsync();

        result.ShouldBeSameAs(cached);
    }

    [Fact]
    public async Task CloseAsync_CallsConnectionCloseAsync_WhenConnectionExists()
    {
        // Non-network-observable assertion: CloseAsync forwards to the cached
        // IConnection.CloseAsync. Intentionally does NOT assert anything about the
        // semaphore acquire/release pattern — the current 10 ms-timeout/never-released
        // code is tracked separately as an open bug (#284 was auto-closed without a
        // fix). Pinning the current semaphore behaviour here would make the fix harder
        // to land.
        var sut = Build(PlainSample());
        var connection = Substitute.For<IConnection>();
        SetCachedConnection(sut, connection);

        await sut.CloseAsync();

        await connection.Received(1).CloseAsync();
    }

    [Fact]
    public async Task CloseAsync_DoesNotThrow_WhenNoConnectionCached()
    {
        var sut = Build(PlainSample());

        await Should.NotThrowAsync(() => sut.CloseAsync());
    }

    [Fact]
    public async Task DisposeAsync_ClosesAndDisposesConnection_WhenCached()
    {
        var sut = Build(PlainSample());
        var connection = Substitute.For<IConnection>();
        SetCachedConnection(sut, connection);

        await sut.DisposeAsync();

        await connection.Received(1).CloseAsync();
        connection.Received(1).Dispose();
    }

    [Fact]
    public async Task DisposeAsync_Noop_WhenNoConnection()
    {
        var sut = Build(PlainSample());

        await Should.NotThrowAsync(() => sut.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task DisposeAsync_SwallowsConnectionException_AndLogsToSelfLog()
    {
        // Connection.CloseAsync throws on shutdown — DisposeAsync must not propagate,
        // must still complete (disposing the semaphore in finally), and must report
        // the swallowed exception to SelfLog.
        using var selfLog = new SelfLogScope(out var selfLogBuilder);

        var sut = Build(PlainSample());
        var connection = Substitute.For<IConnection>();
        connection.When(c => c.CloseAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("close-boom"));
        SetCachedConnection(sut, connection);

        await Should.NotThrowAsync(() => sut.DisposeAsync().AsTask());

        selfLogBuilder.ToString().ShouldContain("close-boom");
    }
}
