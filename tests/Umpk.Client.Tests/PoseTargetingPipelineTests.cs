using System.IO.Pipelines;
using Umpk.Client.Events;
using Umpk.Client.Snapshots;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Fixtures;
using Umpk.TestKit.Server;
using Umpk.TestKit.Time;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class PoseTargetingPipelineTests
{
    [Theory]
    [InlineData("1.8", 47, false, true)]
    [InlineData("1.13.2", 404, false, true)]
    [InlineData("1.14.4", 498, false, true)]
    [InlineData("1.14.4", 498, true, true)]
    [InlineData("1.21.2", 768, false, true)]
    [InlineData("1.21.2", 768, true, true)]
    [InlineData("26.3", 777, false, true)]
    [InlineData("26.3", 777, true, true)]
    [InlineData("26.3", 777, false, false)]
    public async Task ViewRayAndSelfSnapshotFollowThePhysicsPose(string release, int protocol, bool crawl, bool physics)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new TestTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "PoseTester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(protocol))
            .UseTickSource(ticks)
            .ConfigureFeatures(features =>
            {
                features.Physics = physics;
                features.Pathfinding = false;
            })
            .Build();
        await Task.WhenAll(client.ConnectAsync(new ServerEndpoint("test", 25565), ct), ScriptedServer.DriveToPlayAsync(server, version, ct));
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable join = client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
        await ScriptedServer.SendAsync(server, version.Protocol, ProtocolPhase.Play,
            new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, new LegacyLoginFields(0, 2, "flat")), ct);
        await joined.Task.WaitAsync(ct);
        var position = new Vec3d(8.5, 64, 8.5);
        var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable correction = client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
        await ScriptedServer.SendAsync(server, version.Protocol, ProtocolPhase.Play,
            new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 1,
                new PositionMoveRotation(position, Vec3d.Zero, 0, 0)), ct);
        await corrected.Task.WaitAsync(ct);
        await client.InvokeAsync(current =>
        {
            current.State.EntityMembership.LoadColumn(new ChunkPos(0, 0));
            int stone = current.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId;
            for (int x = 7; x <= 9; x++)
                for (int z = 7; z <= 11; z++)
                    current.State.World.SetBlockStateId(new BlockPos(x, 63, z), stone);
            current.State.World.SetBlockStateId(new BlockPos(8, crawl ? 64 : 65, 11), stone);
            return true;
        }, ct);
        SelfSnapshot standing = await client.Snapshots.SelfAsync(ct);
        double standingHeight = NativePlayerDimensions.ForVersion(release).First(row => row.Pose == EntityPose.Standing).EyeHeight;
        Assert.Equal(position.Add(0, standingHeight, 0), standing.EyePosition);

        await client.InvokeAsync(current =>
        {
            current.State.Self.Sneaking = !crawl;
            if (crawl)
                current.State.World.SetBlockStateId(new BlockPos(8, 65, 8), current.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId);
            return true;
        }, ct);
        long before = await client.InvokeAsync(current => current.State.SessionTick, ct);
        ticks.Advance();
        while (!await client.InvokeAsync(current => current.State.SessionTick > before, ct))
            await Task.Delay(1, ct);

        EntityPose pose = !physics ? EntityPose.Standing : crawl ? EntityPose.Swimming : EntityPose.Crouching;
        double expectedHeight = NativePlayerDimensions.ForVersion(release).First(row => row.Pose == pose).EyeHeight;
        SelfSnapshot observed = await client.Snapshots.SelfAsync(ct);
        Vec3d expectedEye = observed.Position.Add(0, expectedHeight, 0);
        Assert.Equal(expectedHeight, await client.InvokeAsync(current => current.SelfEyeHeight, ct));
        Assert.Equal(expectedEye, await client.InvokeAsync(current => current.SelfEyePosition, ct));
        Assert.Equal(expectedEye, observed.EyePosition);
        Assert.Equal(position.Add(0, standingHeight, 0), standing.EyePosition);

        BlockHitResult? hit = await client.Snapshots.RaycastViewAsync(4.5, ct: ct);
        Assert.NotNull(hit);
        Assert.Equal(new BlockPos(8, crawl ? 64 : 65, 11), hit.Value.BlockPos);
        Assert.Equal(expectedEye.Y, hit.Value.Point.Y);
        Assert.Equal(Direction.North, hit.Value.Face);

        var destination = new Vec3d(10.5, 64, 8.5);
        var destinationTarget = new BlockPos(10, crawl ? 64 : 65, 11);
        await client.InvokeAsync(current =>
        {
            int stone = current.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId;
            for (int x = 10; x <= 11; x++)
                for (int z = 7; z <= 11; z++)
                    current.State.World.SetBlockStateId(new BlockPos(x, 63, z), stone);
            current.State.World.SetBlockStateId(destinationTarget, stone);
            if (crawl)
                current.State.World.SetBlockStateId(new BlockPos(10, 65, 8), stone);
            return true;
        }, ct);
        var moved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable movedSubscription = client.Events.Subscribe<PositionCorrected>(_ => moved.TrySetResult());
        long correctionTick = await client.InvokeAsync(current => current.State.SessionTick, ct);
        await ScriptedServer.SendAsync(server, version.Protocol, ProtocolPhase.Play,
            new ClientboundPlayerPositionPacket(destination.X, destination.Y, destination.Z, 0, 0, 0, 2,
                new PositionMoveRotation(destination, Vec3d.Zero, 0, 0)), ct);
        await moved.Task.WaitAsync(ct);

        SelfSnapshot afterCorrection = await client.Snapshots.SelfAsync(ct);
        Assert.Equal(correctionTick, await client.InvokeAsync(current => current.State.SessionTick, ct));
        Assert.Equal(destination, afterCorrection.Position);
        Assert.Equal(expectedHeight, afterCorrection.EyeHeight);
        Assert.Equal(expectedEye, observed.EyePosition);
        BlockHitResult? afterHit = await client.Snapshots.RaycastViewAsync(4.5, ct: ct);
        Assert.NotNull(afterHit);
        Assert.Equal(destinationTarget, afterHit.Value.BlockPos);
        Assert.Equal(destination.Y + expectedHeight, afterHit.Value.Point.Y);
        Assert.Equal(Direction.North, afterHit.Value.Face);
    }

    [Theory]
    [InlineData("1.8", EntityPose.Crouching)]
    [InlineData("1.14.4", EntityPose.Crouching)]
    [InlineData("26.3", EntityPose.Swimming)]
    public void SnapshotCopiesKeepTheirCapturedHeightAndFollowNewFeet(string version, EntityPose pose)
    {
        double nativeHeight = NativePlayerDimensions.ForVersion(version).First(row => row.Pose == pose).EyeHeight;
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.Position = new Vec3d(8, 64, 8);
        SelfSnapshot snapshot = SelfSnapshot.Project(state) with { EyeHeight = nativeHeight };
        SelfSnapshot moved = snapshot with { Position = new Vec3d(-10, 128, 12) };

        Assert.Equal(state.Self.Position.Add(0, nativeHeight, 0), snapshot.EyePosition);
        Assert.Equal(new Vec3d(-10, 128 + nativeHeight, 12), moved.EyePosition);
        Assert.Equal(nativeHeight, moved.EyeHeight);
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }
}
