using System.Collections.Concurrent;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.IntegrationTests;

[Collection(LiveServerCollection.Name)]
public sealed class MovementParityLiveTests
{
    private const string Username = "UmpkMoveTest";
    private readonly ITestOutputHelper _output;

    public MovementParityLiveTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string, int> Versions => new()
    {
        { "1.21.5", 770 },
        { "1.21.11", 774 },
    };

    [NightlyTheory]
    [MemberData(nameof(Versions))]
    public async Task HighLevelNavigationMovesAcrossAControlledVanillaCourse(string versionName, int protocol)
    {
        string serverDir = IntegrationConfig.ServerDir(versionName);
        Assert.True(
            Directory.Exists(serverDir),
            $"Server directory for {versionName} not provisioned ({serverDir}). Set UMPK_SERVER_ROOT.");
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version) && version is not null);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = timeout.Token;
        await using LocalServer server = await LocalServer.StartAsync(
            serverDir, JavaRuntimes.ForProtocol(protocol), TimeSpan.FromMinutes(3), ct);
        var logs = new CapturingLoggerFactory();
        await using UmpkClient client = Build(version!, protocol, logs);

        int inputFrames = 0;
        int movementFrames = 0;
        client.PacketFrameObserved += observation =>
        {
            if (observation.Flow != PacketFlow.Serverbound || observation.Phase != ProtocolPhase.Play)
                return;

            if (observation.DecodedPacket is ServerboundPlayerInputPacket)
                Interlocked.Increment(ref inputFrames);
            if (observation.DecodedPacket is ServerboundMovePlayerPosPacket
                or ServerboundMovePlayerPosRotPacket
                or ServerboundMovePlayerRotPacket
                or ServerboundMovePlayerStatusPacket
                or ServerboundMovePlayerStatusOnlyPacket)
                Interlocked.Increment(ref movementFrames);
        };

        await client.ConnectAsync(new ServerEndpoint("127.0.0.1", LocalServer.Port), ct);
        Assert.True(await client.Spawned.WaitAsync(ct));
        Assert.True(
            await WaitUntilAsync(() => client.State.IsLocalChunkLoaded, TimeSpan.FromSeconds(30), ct),
            $"initial spawn column never became usable; pos={client.State.Self.Position}, "
                + $"status={client.Status}; server log:\n{server.LogText}");

        await server.SendConsoleAsync("forceload add 0 0", ct);
        await server.SendConsoleAsync("fill -2 99 -2 10 99 2 minecraft:stone", ct);
        await server.SendConsoleAsync($"tp {Username} 0.5 100 0.5", ct);
        Assert.True(
            await WaitUntilAsync(
                () => Math.Abs(client.State.Self.Position.X - 0.5) < 0.1
                    && Math.Abs(client.State.Self.Position.Y - 100) < 0.2
                    && client.State.IsLocalChunkLoaded
                    && client.State.World.GetBlock(new BlockPos(0, 99, 0)).Block.Id
                        == Identifier.Minecraft("stone")
                    && client.State.World.GetBlock(new BlockPos(5, 99, 0)).Block.Id
                        == Identifier.Minecraft("stone"),
                TimeSpan.FromSeconds(30),
                ct),
            $"teleport/chunk readiness did not converge: pos={client.State.Self.Position}, "
                + $"spawned={client.State.Self.HasSpawned}, localChunk={client.State.IsLocalChunkLoaded}, "
                + $"status={client.Status}; server log:\n{server.LogText}");

        Vec3d start = client.State.Self.Position;
        await client.Actions.Movement.SetRotationAsync(0, 0, ct);
        await client.Actions.Movement.SetSneakingAsync(true, ct);
        await client.Actions.Movement.SetSneakingAsync(false, ct);
        await client.Navigation.MoveToAsync(new Vec3d(3.5, 100, 0.5), ct);
        Vec3d end = client.State.Self.Position;

        Assert.Equal(ClientStatus.Playing, client.Status);
        Assert.True(end.X - start.X >= 2.0, $"movement made no positive course progress: {start} -> {end}");
        Assert.True(Volatile.Read(ref inputFrames) > 0, "no held-input publication was observed");
        Assert.True(Volatile.Read(ref movementFrames) > 0, "no ordinary movement report was observed");
        _output.WriteLine(
            $"[{versionName}/{protocol}] {start} -> {end}; input={inputFrames}, movement={movementFrames}");
    }

    [NightlyFact]
    public async Task HighLevelNavigationSurvivesAnActualVelocityBackendSwitch()
    {
        const int protocol = 774;
        const int secondBackendPort = 25601;
        const int secondBackendRconPort = 25602;
        string? velocityJar = Environment.GetEnvironmentVariable("UMPK_VELOCITY_JAR");
        Assert.True(
            !string.IsNullOrWhiteSpace(velocityJar) && File.Exists(velocityJar),
            "Set UMPK_VELOCITY_JAR to an actual local Velocity jar; missing proxy infrastructure is not a pass.");

        string serverDir = IntegrationConfig.ServerDir("1.21.11");
        Assert.True(Directory.Exists(serverDir), $"Server directory is not provisioned: {serverDir}");
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version) && version is not null);
        string java = JavaRuntimes.ForProtocol(protocol);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        CancellationToken ct = timeout.Token;
        await using LocalServer backend = await LocalServer.StartAsync(
            serverDir, java, TimeSpan.FromMinutes(3), ct);
        await using LocalServer backend2 = await LocalServer.StartAsync(
            serverDir, java, TimeSpan.FromMinutes(3), ct, secondBackendPort, secondBackendRconPort);
        await using LocalVelocity proxy = await LocalVelocity.StartAsync(
            velocityJar!,
            java,
            new Dictionary<string, int>
            {
                ["backend"] = backend.ServerPort,
                ["backend2"] = backend2.ServerPort,
            },
            ct);

        var logs = new CapturingLoggerFactory();
        await using UmpkClient client = Build(version!, protocol, logs);
        var phases = new ConcurrentQueue<ProtocolPhase>();
        client.Events.Subscribe<PhaseChanged>(change => phases.Enqueue(change.Phase));

        await client.ConnectAsync(new ServerEndpoint("127.0.0.1", LocalVelocity.Port), ct);
        Assert.True(await client.Spawned.WaitAsync(ct));
        Assert.True(await WaitUntilAsync(() => client.State.IsLocalChunkLoaded, TimeSpan.FromSeconds(30), ct));
        (Vec3d firstStart, Vec3d firstEnd) = await RunCourseAsync(backend, client, ct);

        int phasesBeforeSwitch = phases.Count;
        await client.Actions.Chat.SendCommandAsync("/server backend2", ct);
        Assert.True(
            await WaitUntilAsync(
                () => phases.Skip(phasesBeforeSwitch).Contains(ProtocolPhase.Configuration)
                    && phases.Skip(phasesBeforeSwitch).Contains(ProtocolPhase.Play)
                    && client.State.Self.HasSpawned
                    && client.State.HasWorld
                    && backend2.LogText.Contains(Username, StringComparison.Ordinal),
                TimeSpan.FromSeconds(45),
                ct),
            $"Velocity switch did not complete. phases=[{string.Join(',', phases)}]\n"
                + $"proxy:\n{proxy.LogText}\nbackend2:\n{backend2.LogText}");

        Assert.True(await WaitUntilAsync(() => client.State.IsLocalChunkLoaded, TimeSpan.FromSeconds(30), ct));
        (Vec3d secondStart, Vec3d secondEnd) = await RunCourseAsync(backend2, client, ct);

        Assert.True(firstEnd.X - firstStart.X >= 2.0, $"primary backend course did not progress: {firstStart} -> {firstEnd}");
        Assert.True(secondEnd.X - secondStart.X >= 2.0, $"second backend course did not progress: {secondStart} -> {secondEnd}");
        Assert.Contains("backend2", proxy.LogText, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"Velocity workdir: {proxy.WorkDirectory}");
        _output.WriteLine($"Primary: {firstStart} -> {firstEnd}; second: {secondStart} -> {secondEnd}");
        _output.WriteLine(proxy.LogText);
    }

    private static async Task<(Vec3d Start, Vec3d End)> RunCourseAsync(
        LocalServer server,
        UmpkClient client,
        CancellationToken ct)
    {
        await server.SendConsoleAsync("forceload add 0 0", ct);
        await server.SendConsoleAsync("fill -2 99 -2 10 99 2 minecraft:stone", ct);
        await server.SendConsoleAsync($"tp {Username} 0.5 100 0.5", ct);
        Assert.True(await WaitUntilAsync(
            () => Math.Abs(client.State.Self.Position.X - 0.5) < 0.1
                && Math.Abs(client.State.Self.Position.Y - 100) < 0.2
                && client.State.IsLocalChunkLoaded
                && client.State.World.GetBlock(new BlockPos(0, 99, 0)).Block.Id == Identifier.Minecraft("stone")
                && client.State.World.GetBlock(new BlockPos(5, 99, 0)).Block.Id == Identifier.Minecraft("stone"),
            TimeSpan.FromSeconds(30),
            ct));

        Vec3d start = client.State.Self.Position;
        await client.Actions.Movement.SetRotationAsync(0, 0, ct);
        await client.Navigation.MoveToAsync(new Vec3d(3.5, 100, 0.5), ct);
        return (start, client.State.Self.Position);
    }

    private static UmpkClient Build(JavaVersion version, int protocol, CapturingLoggerFactory logs) =>
        new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), Username))
            .UseStaticRegistries(JavaGameData.Registries(protocol))
            .UseLoggerFactory(logs)
            .Build();

    private static async Task<bool> WaitUntilAsync(
        Func<bool> condition, TimeSpan window, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(50, ct);
        }

        return condition();
    }
}
