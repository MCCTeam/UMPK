using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="ServerVersionNegotiator.DetectAsync"/> turns a status ping into a resolved <see cref="JavaVersion"/> (or a typed <see cref="VersionNegotiationFailure"/>), and <see cref="UmpkClientBuilder.BuildForAsync"/> is the auto-version path that uses it: with <see cref="UmpkClientBuilder.UseVersion"/> already called it does no I/O; without it, it pings and throws <see cref="VersionResolutionException"/> when nobody can supply a version.</summary>
public sealed class ServerVersionNegotiationTests
{
    [Fact]
    public async Task DetectAsync_AgainstAServerThatAnswers_ResolvesTheVersion()
    {
        const string json = "{\"version\":{\"name\":\"1.21.5\",\"protocol\":770}}";
        using var server = new ScriptedStatusServer(json);
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", server.Port), ct: Ct());

        Assert.True(negotiation.Succeeded);
        Assert.Same(JavaVersions.V1_21_5, negotiation.Version);
        Assert.Equal(770, negotiation.Status?.Protocol);
        Assert.Equal(VersionNegotiationFailure.None, negotiation.Failure);
    }

    [Fact]
    public async Task DetectAsync_PingFails_ReportsStatusPingFailed_AndKeepsTheFault()
    {
        ushort closedPort = ClaimAndCloseAPort();
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", closedPort), ct: Ct());

        Assert.False(negotiation.Succeeded);
        Assert.Null(negotiation.Version);
        Assert.Equal(VersionNegotiationFailure.StatusPingFailed, negotiation.Failure);
        Assert.IsType<SocketException>(negotiation.Fault);
    }

    [Fact]
    public async Task DetectAsync_NoProtocolField_FallsBackToHighest()
    {
        const string json = "{\"description\":\"hi\"}";
        using var server = new ScriptedStatusServer(json);
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", server.Port), ct: Ct());

        // No protocol field at all (legacy proxy) now defaults to highest per policy "default to highest supported protocol". Keeps the status but resolves a version.
        Assert.True(negotiation.Succeeded);
        Assert.Equal(VersionNegotiationFailure.None, negotiation.Failure);
        Assert.NotNull(negotiation.Status);
        Assert.Same(JavaVersions.V26_2, negotiation.Version);
    }

    [Fact]
    public async Task DetectAsync_UnsupportedProtocol_FallsBackToClosestSupported()
    {
        // "future" has no version tokens, so mining fails and numeric closest wins. 999999 is far above ceiling 776 → closest is highest (26.2 / 776).
        const string json = "{\"version\":{\"name\":\"future\",\"protocol\":999999}}";
        using var server = new ScriptedStatusServer(json);
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", server.Port), ct: Ct());

        Assert.True(negotiation.Succeeded);
        Assert.Equal(VersionNegotiationFailure.None, negotiation.Failure);
        Assert.Equal(999999, negotiation.Status?.Protocol);
        Assert.Same(JavaVersions.V26_2, negotiation.Version);
    }

    [Fact]
    public async Task DetectAsync_VelocityMinusOne_MinesHighestFromVersionName()
    {
        // DonutSMP: Velocity 1.7.2-26.2 (Protocol: -1) → should mine 26.2 / 776.
        const string json = "{\"version\":{\"name\":\"Velocity 1.7.2-26.2\",\"protocol\":-1}}";
        using var server = new ScriptedStatusServer(json);
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", server.Port), ct: Ct());

        Assert.True(negotiation.Succeeded);
        Assert.Same(JavaVersions.V26_2, negotiation.Version);
        Assert.Equal(-1, negotiation.Status?.Protocol);
    }

    [Fact]
    public async Task DetectAsync_VelocityMinusOne_WithoutVersionName_FallsBackToHighest()
    {
        const string json = "{\"version\":{\"name\":\"Velocity\",\"protocol\":-1}}";
        using var server = new ScriptedStatusServer(json);
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", server.Port), ct: Ct());

        Assert.True(negotiation.Succeeded);
        Assert.Same(JavaVersions.V26_2, negotiation.Version);
    }

    [Fact]
    public async Task DetectAsync_NoProtocol_MinesHighestFromVersionName()
    {
        const string json = "{\"version\":{\"name\":\"Paper 1.7.2-26.2\"}}";
        using var server = new ScriptedStatusServer(json);
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", server.Port), ct: Ct());

        Assert.True(negotiation.Succeeded);
        Assert.Same(JavaVersions.V26_2, negotiation.Version);
        Assert.Null(negotiation.Status?.Protocol);
        Assert.Equal(VersionNegotiationFailure.None, negotiation.Failure);
    }

    [Fact]
    public async Task DetectAsync_NoProtocol_SingleToken_FallsBackToHighest()
    {
        // Single token "1.7.2" is not enough for mining (>=2 required), so fallback to highest
        const string json = "{\"version\":{\"name\":\"1.7.2\"}}";
        using var server = new ScriptedStatusServer(json);
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", server.Port), ct: Ct());

        Assert.True(negotiation.Succeeded);
        Assert.Same(JavaVersions.V26_2, negotiation.Version);
        Assert.Equal(VersionNegotiationFailure.None, negotiation.Failure);
    }

    [Fact]
    public async Task DetectAsync_AncientProtocol_FallsBackToClosestFloor()
    {
        // 1.7.2 is protocol 4, below UMPK floor 47 → closest is 47 (1.8)
        const string json = "{\"version\":{\"name\":\"1.7.2\",\"protocol\":4}}";
        using var server = new ScriptedStatusServer(json);
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", server.Port), ct: Ct());

        Assert.True(negotiation.Succeeded);
        Assert.Same(JavaVersions.V1_8, negotiation.Version);
    }

    [Fact]
    public async Task DetectAsync_FutureProtocol_FallsBackToHighest()
    {
        const string json = "{\"version\":{\"name\":\"1.99\",\"protocol\":9999}}";
        using var server = new ScriptedStatusServer(json);
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", server.Port), ct: Ct());

        Assert.True(negotiation.Succeeded);
        Assert.Same(JavaVersions.V26_2, negotiation.Version);
    }

    [Fact]
    public async Task DetectAsync_SnapshotProtocol_NormalizesToRelease()
    {
        const string json = "{\"version\":{\"name\":\"26.1-rc-2\",\"protocol\":1073742126}}"; // 0x4000012E
        using var server = new ScriptedStatusServer(json);
        var negotiator = new ServerVersionNegotiator();

        VersionNegotiation negotiation = await negotiator.DetectAsync(
            new ServerEndpoint("127.0.0.1", server.Port), ct: Ct());

        Assert.True(negotiation.Succeeded);
        Assert.Same(JavaVersions.V26_1, negotiation.Version);
    }

    [Fact]
    public async Task BuildForAsync_WithNoVersion_PingsAndBuildsForTheServersProtocol()
    {
        const string json = "{\"version\":{\"name\":\"1.21.5\",\"protocol\":770}}";
        using var server = new ScriptedStatusServer(json);

        await using UmpkClient client = await new UmpkClientBuilder()
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .BuildForAsync(new ServerEndpoint("127.0.0.1", server.Port), Ct());

        // The negotiated protocol is observable on the built client without ever connecting.
        Assert.Equal(770, client.Capabilities.Protocol);
    }

    [Fact]
    public async Task BuildForAsync_WithUseVersion_DoesNotPing()
    {
        var factory = new RecordingConnectionFactory();

        await using UmpkClient client = await new UmpkClientBuilder()
            .UseVersion(JavaVersions.V1_21_5)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(factory)
            .BuildForAsync(new ServerEndpoint("127.0.0.1", 25565), Ct());

        Assert.Equal(0, factory.ConnectCount);
        Assert.Equal(770, client.Capabilities.Protocol);
    }

    [Fact]
    public async Task BuildForAsync_WhenDetectionFails_ThrowsVersionResolutionException_CarryingTheDiagnostic()
    {
        ushort closedPort = ClaimAndCloseAPort();

        VersionResolutionException ex = await Assert.ThrowsAsync<VersionResolutionException>(() =>
            new UmpkClientBuilder()
                .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
                .BuildForAsync(new ServerEndpoint("127.0.0.1", closedPort), Ct()));

        Assert.Equal("127.0.0.1", ex.Host);
        Assert.Equal(closedPort, ex.Port);
        Assert.Equal(VersionNegotiationFailure.StatusPingFailed, ex.Failure);
        Assert.Null(ex.Protocol);
    }

    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    /// <summary>Binds an ephemeral loopback port and immediately releases it, so nothing answers there.</summary>
    private static ushort ClaimAndCloseAPort()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return (ushort)((IPEndPoint)listener.LocalEndPoint!).Port;
    }

    private sealed class RecordingConnectionFactory : IConnectionFactory
    {
        public int ConnectCount { get; private set; }

        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
        {
            ConnectCount++;
            throw new InvalidOperationException("BuildForAsync must not connect when a version was already set.");
        }
    }
}
