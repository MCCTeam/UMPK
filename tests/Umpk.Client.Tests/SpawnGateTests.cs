using System.IO.Pipelines;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="UmpkClient.Spawned"/> and <see cref="UmpkClient.ConnectAndWaitForSpawnAsync"/>: the signal that closes the spawn race. <see cref="UmpkClient.ConnectAsync"/> still returns when play begins, before the join packet and its teleport arrive.</summary>
public sealed class SpawnGateTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static JavaVersion Version => ScriptedServer.Version;

    /// <summary>The race this whole feature exists to close: the server places the player before the test ever reads <see cref="UmpkClient.Spawned"/>, and the read still observes it.</summary>
    /// <remarks>If the signal were armed lazily - say, subscribed only when <see cref="UmpkClient.Spawned"/> is first read, or only once <see cref="UmpkClient.ConnectAsync"/> returns - a server this fast would win the race: the teleport would already have been applied and published before anything was listening, and the read below would hang forever. It does not, because the subscription that completes <see cref="UmpkClient.Spawned"/> is installed once, in the constructor, and the task it completes is re-armed synchronously inside <see cref="UmpkClient.ConnectAsync"/> before that method's first await - both ahead of any packet this test sends.</remarks>
    [Fact]
    public async Task Spawned_IsArmedBeforeConnectReturns_SoAJoinCannotBeMissed()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = ScriptedServer.BuildClient(server);

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);

        // The join packet and the initial position teleport go out back-to-back, immediately, and client.Spawned is not read anywhere above this line.
        await ScriptedServer.SendAsync(server, Version.Protocol, ProtocolPhase.Play, Join(), ct);
        await ScriptedServer.SendAsync(server, Version.Protocol, ProtocolPhase.Play, Teleport(), ct);

        await connect.WaitAsync(Budget, ct);

        Assert.True(await client.Spawned.WaitAsync(Budget, ct));
    }

    /// <summary>A session that ends before the teleport arrives completes <see cref="UmpkClient.Spawned"/> false.</summary>
    [Fact]
    public async Task Spawned_CompletesFalse_WhenTheSessionEndsFirst()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = ScriptedServer.BuildClient(server);

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await connect.WaitAsync(Budget, ct);

        await ScriptedServer.SendAsync(server, Version.Protocol, ProtocolPhase.Play, Join(), ct);
        await joined.Task.WaitAsync(Budget, ct);

        // The server closes right after the join, without ever sending the teleport.
        await server.DisposeAsync();

        Assert.False(await client.Spawned.WaitAsync(Budget, ct));
    }

    /// <summary>Reconnecting the same <see cref="UmpkClient"/> gets a different <see cref="UmpkClient.Spawned"/> task, freshly incomplete, not the previous connection's already-resolved one.</summary>
    [Fact]
    public async Task Spawned_IsFreshPerConnection()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var factory = new SwitchableConnectionFactory();
        FakeJavaServer serverA = FakeJavaServer.Create();
        factory.Pipe = serverA.ClientPipe;

        await using UmpkClient client = BuildClient(factory);

        Task connectA = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await ScriptedServer.DriveToPlayAsync(serverA, ct);
        await connectA.WaitAsync(Budget, ct);

        Task<bool> firstSpawned = client.Spawned;
        await serverA.DisposeAsync();
        Assert.False(await firstSpawned.WaitAsync(Budget, ct));

        FakeJavaServer serverB = FakeJavaServer.Create();
        factory.Pipe = serverB.ClientPipe;

        Task connectB = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        // Read immediately: ConnectAsync re-arms synchronously, ahead of its own first await, so the new task already exists the instant the call above returns control here.
        Task<bool> secondSpawned = client.Spawned;

        Assert.NotSame(firstSpawned, secondSpawned);
        Assert.False(secondSpawned.IsCompleted);

        await ScriptedServer.DriveToPlayAsync(serverB, ct);
        await connectB.WaitAsync(Budget, ct);

        await ScriptedServer.SendAsync(serverB, Version.Protocol, ProtocolPhase.Play, Join(), ct);
        await ScriptedServer.SendAsync(serverB, Version.Protocol, ProtocolPhase.Play, Teleport(), ct);

        Assert.True(await secondSpawned.WaitAsync(Budget, ct));
    }

    /// <summary>The combined form resolves true once the teleport applies.</summary>
    [Fact]
    public async Task ConnectAndWaitForSpawnAsync_ReturnsTrue_OnceTheTeleportApplies()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = ScriptedServer.BuildClient(server);

        Task<bool> connect = client.ConnectAndWaitForSpawnAsync(new ServerEndpoint("test", 25565), ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await ScriptedServer.SendAsync(server, Version.Protocol, ProtocolPhase.Play, Join(), ct);
        await ScriptedServer.SendAsync(server, Version.Protocol, ProtocolPhase.Play, Teleport(), ct);

        Assert.True(await connect.WaitAsync(Budget, ct));
    }

    /// <summary>The combined form resolves false, not throws, when the session ends before spawning.</summary>
    [Fact]
    public async Task ConnectAndWaitForSpawnAsync_ReturnsFalse_WhenTheServerNeverPlacesUs()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = ScriptedServer.BuildClient(server);

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());

        Task<bool> connect = client.ConnectAndWaitForSpawnAsync(new ServerEndpoint("test", 25565), ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await ScriptedServer.SendAsync(server, Version.Protocol, ProtocolPhase.Play, Join(), ct);
        await joined.Task.WaitAsync(Budget, ct);

        // No teleport ever comes; the server just closes.
        await server.DisposeAsync();

        Assert.False(await connect.WaitAsync(Budget, ct));
    }

    /// <summary><see cref="UmpkClient.ConnectAsync"/>'s own return contract is unchanged: it completes once play begins, before anything play-phase - including the join packet and the teleport it carries - has been sent at all, so there is nothing on the wire it could have raced.</summary>
    [Fact]
    public async Task ConnectAsync_SemanticsUnchanged_ReturnsBeforeTheTeleport()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = ScriptedServer.BuildClient(server);

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await ScriptedServer.DriveToPlayAsync(server, ct);
        await connect.WaitAsync(Budget, ct);

        Assert.False(client.Spawned.IsCompleted);

        // The signal still works normally once play-phase traffic actually arrives.
        await ScriptedServer.SendAsync(server, Version.Protocol, ProtocolPhase.Play, Join(), ct);
        await ScriptedServer.SendAsync(server, Version.Protocol, ProtocolPhase.Play, Teleport(), ct);

        Assert.True(await client.Spawned.WaitAsync(Budget, ct));
    }

    private static UmpkClient BuildClient(IConnectionFactory factory) => new UmpkClientBuilder()
        .UseVersion(Version)
        .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
        .UseConnectionFactory(factory)
        .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
        .ConfigureFeatures(f =>
        {
            f.Physics = false;
            f.Pathfinding = false;
        })
        .Build();

    private static ClientboundLoginPacket Join()
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        return new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null);
    }

    private static ClientboundPlayerPositionPacket Teleport()
    {
        var modern = new PositionMoveRotation(new Vec3d(10.5, 65.0, -12.5), Vec3d.Zero, YRot: 90f, XRot: 0f);
        return new ClientboundPlayerPositionPacket(
            10.5, 65.0, -12.5, Yaw: 90f, Pitch: 0f, RelativeFlags: 0, TeleportId: 7, ModernValues: modern);
    }

    /// <summary>A connection factory whose pipe the test can swap between connects, so the same client can be reconnected against a different fake server without rebuilding it.</summary>
    private sealed class SwitchableConnectionFactory : IConnectionFactory
    {
        public IDuplexPipe Pipe { get; set; } = null!;

        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(Pipe);
    }
}
