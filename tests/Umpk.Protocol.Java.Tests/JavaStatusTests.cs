using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Protocol.Java.Tests;

public class JavaStatusTests
{
    [Fact]
    public async Task QueryAsync_AgainstScriptedServer_ReturnsJsonAndLatency()
    {
        const string json = "{\"version\":{\"name\":\"1.21.5\",\"protocol\":770},\"players\":{\"max\":20,\"online\":3}}";
        using var server = new ScriptedStatusServer(json);

        var status = await JavaStatus.QueryAsync(
            new ServerEndpoint("127.0.0.1", server.Port),
            TcpConnectionFactory.Shared,
            new JavaStatusOptions { Timeout = TimeSpan.FromSeconds(10) },
            Ct());

        Assert.Equal(json, status.Json);
        Assert.True(status.Latency >= TimeSpan.Zero);
    }

    [Fact]
    public async Task QueryAsync_ConvenienceOverload_ReturnsTheStatus()
    {
        // The scripted server's ephemeral port is never the default Java port, so this exercises the explicit-port path, which now skips the resolver entirely by design (see QueryAsync_WithAnExplicitPort_DoesNotConsultTheResolver below).
        const string json = "{\"description\":\"hi\"}";
        using var server = new ScriptedStatusServer(json);

        var status = await JavaStatus.QueryAsync(
            "127.0.0.1", server.Port,
            new JavaStatusOptions { Timeout = TimeSpan.FromSeconds(10) },
            Ct(),
            resolver: DnsSrvResolver.Passthrough,
            factory: TcpConnectionFactory.Shared);

        Assert.Equal(json, status.Json);
    }

    [Fact]
    public async Task QueryAsync_AgainstScriptedServer_SurfacesTheStructuredFields()
    {
        const string json = "{\"version\":{\"name\":\"1.21.5\",\"protocol\":770},\"players\":{\"max\":20,\"online\":3}}";
        using var server = new ScriptedStatusServer(json);

        ServerStatus status = await JavaStatus.QueryAsync(
            new ServerEndpoint("127.0.0.1", server.Port),
            TcpConnectionFactory.Shared,
            new JavaStatusOptions { Timeout = TimeSpan.FromSeconds(10) },
            Ct());

        Assert.Equal("1.21.5", status.VersionName);
        Assert.Equal(770, status.Protocol);
        Assert.Equal(3, status.OnlinePlayers);
        Assert.Equal(20, status.MaxPlayers);
    }

    [Fact]
    public async Task QueryAsync_WithAnExplicitPort_DoesNotConsultTheResolver()
    {
        // Status ping and connect share the same server-name resolver, which only looks up
        // _minecraft._tcp when the port is 25565. An explicit non-default port must never reach it;
        // the recording resolver here is armed with an endpoint that would make this test fail loudly
        // if it were ever consulted.
        const string json = "{\"description\":\"hi\"}";
        using var server = new ScriptedStatusServer(json);
        var resolver = new RecordingResolver(new ServerEndpoint("should-never-be-dialed.invalid", 1));

        ServerStatus status = await JavaStatus.QueryAsync(
            "127.0.0.1", server.Port,
            new JavaStatusOptions { Timeout = TimeSpan.FromSeconds(10) },
            Ct(),
            resolver: resolver,
            factory: TcpConnectionFactory.Shared);

        Assert.Equal(0, resolver.InvocationCount);
        Assert.Equal(json, status.Json);
    }

    [Fact]
    public async Task QueryAsync_WithTheDefaultPort_ConsultsTheResolver_AndPingsWhatItReturns()
    {
        const string json = "{\"description\":\"resolved\"}";
        using var server = new ScriptedStatusServer(json);
        var resolved = new ServerEndpoint("127.0.0.1", server.Port);
        var resolver = new RecordingResolver(resolved);

        ServerStatus status = await JavaStatus.QueryAsync(
            "unused.invalid", ServerEndpoint.DefaultJavaPort,
            new JavaStatusOptions { Timeout = TimeSpan.FromSeconds(10) },
            Ct(),
            resolver: resolver,
            factory: TcpConnectionFactory.Shared);

        Assert.Equal(1, resolver.InvocationCount);
        Assert.Equal(new ServerEndpoint("unused.invalid", ServerEndpoint.DefaultJavaPort), resolver.LastRequested);
        Assert.Equal(json, status.Json);
    }

    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    private sealed class RecordingResolver(ServerEndpoint target) : IServerAddressResolver
    {
        public int InvocationCount { get; private set; }

        public ServerEndpoint? LastRequested { get; private set; }

        public ValueTask<ServerEndpoint> ResolveAsync(ServerEndpoint endpoint, CancellationToken ct)
        {
            InvocationCount++;
            LastRequested = endpoint;
            return ValueTask.FromResult(target);
        }
    }
}
