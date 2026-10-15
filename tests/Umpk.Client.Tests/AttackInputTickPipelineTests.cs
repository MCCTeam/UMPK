using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class AttackInputTickPipelineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionPlayerPhaseChargesTheNextClickWithOrWithoutPhysics(bool physics)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        var ticks = new ObservedTickSource();
        JavaVersion version = ScriptedServer.Version;
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, physics);
        await JoinAsync(client, server, version, "diamond_spear", ct);
        BlockPos target = new(1, 65, 1);
        for (int tick = 0; tick < 6; tick++)
        {
            await ticks.FireAsync(ct);
            Assert.Empty(await ReadActionsThroughTickEndAsync(server, version, ct));
        }

        using var held = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task mining = client.Actions.Interaction.DigBlockAsync(target, Direction.North, held.Token);
        try
        {
            await client.InvokeAsync(_ => true, ct);
            await ticks.FireAsync(ct);
            ServerboundPlayerActionPacket stab = Assert.Single(await ReadActionsThroughTickEndAsync(server, version, ct));
            Assert.Equal(7, stab.Action);
            Assert.Equal(default, stab.Position);
            Assert.Equal(0, stab.Sequence);
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            held.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(ct));
        }
    }

    [Theory]
    [InlineData(776)]
    [InlineData(777)]
    public async Task TeleportStopsDestructionOnlyAtTheNativeBoundaryEvenWhilePiercing(int protocol)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, physics: false);
        await JoinAsync(client, server, version, "stone", ct);
        BlockPos target = new(1, 65, 1);
        using var held = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task mining = client.Actions.Interaction.DigBlockAsync(target, Direction.North, held.Token);
        try
        {
            await client.InvokeAsync(_ => true, ct);
            await ticks.FireAsync(ct);
            Assert.Equal(0, Assert.Single(await ReadActionsThroughTickEndAsync(server, version, ct)).Action);
            await client.InvokeAsync(current =>
            {
                Assert.True(current.State.Registries!.Items.TryGet(Identifier.Minecraft("diamond_spear"), out RegistryEntry<ItemDefinition> spear));
                current.State.Inventory.SetSlot(0, 36, new ItemStack(spear, 1));
                return true;
            }, ct);
            var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
            var position = new Vec3d(2.5, 65, 2.5);
            await ScriptedServer.SendAsync(server, version.Protocol, ProtocolPhase.Play,
                new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 2,
                    new PositionMoveRotation(position, Vec3d.Zero, 0, 0)), ct);
            await corrected.Task.WaitAsync(ct);
            await ticks.FireAsync(ct);
            List<ServerboundPlayerActionPacket> actions = await ReadActionsThroughTickEndAsync(server, version, ct);
            if (protocol == 777)
            {
                ServerboundPlayerActionPacket abort = Assert.Single(actions);
                Assert.Equal(2, abort.Action);
                Assert.Equal(target, abort.Position);
                Assert.Equal((byte)Direction.Down, abort.Direction);
                Assert.Equal(0, abort.Sequence);
            }
            else
                Assert.Empty(actions);
        }
        finally
        {
            held.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(ct));
        }
    }

    private static UmpkClient CreateClient(FakeJavaServer server, ITickSource ticks, JavaVersion version, bool physics)
    {
        return new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "ChargeTester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
            .UseTickSource(ticks)
            .ConfigureFeatures(features =>
            {
                features.Physics = physics;
                features.Pathfinding = false;
            })
            .Build();
    }

    private static async Task JoinAsync(UmpkClient client, FakeJavaServer server, JavaVersion version, string item, CancellationToken ct)
    {
        await Task.WhenAll(client.ConnectAsync(new ServerEndpoint("test", 25565), ct), ScriptedServer.DriveToPlayAsync(server, version, ct));

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable joinSubscription = client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
        await ScriptedServer.SendAsync(server, version.Protocol, ProtocolPhase.Play,
            new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, null), ct);
        await joined.Task.WaitAsync(ct);
        var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable positionSubscription = client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
        var position = new Vec3d(2.5, 65, 2.5);
        await ScriptedServer.SendAsync(server, version.Protocol, ProtocolPhase.Play,
            new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 1,
                new PositionMoveRotation(position, Vec3d.Zero, 0, 0)), ct);
        await corrected.Task.WaitAsync(ct);

        BlockPos target = new(1, 65, 1);
        await client.InvokeAsync(current =>
        {
            RegistryAccess registries = current.State.Registries!;
            Assert.True(registries.Items.TryGet(Identifier.Minecraft(item), out RegistryEntry<ItemDefinition> spear));
            Assert.True(registries.Blocks.TryGetValue(Identifier.Minecraft("bedrock"), out BlockDefinition? bedrock));
            current.State.World.SetBlockStateId(target, bedrock.DefaultStateId);
            current.State.World.SetBlockStateId(new BlockPos(2, 64, 2), bedrock.DefaultStateId);
            current.State.Inventory.SetSlot(0, 36, new ItemStack(spear, 1));
            return true;
        }, ct);
    }

    private static async Task<List<ServerboundPlayerActionPacket>> ReadActionsThroughTickEndAsync(FakeJavaServer server, JavaVersion version, CancellationToken ct)
    {
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        var context = new PacketCodecContext(JavaGameData.Registries(version.Version.Protocol), IConnectionCodecState.Empty);
        List<ServerboundPlayerActionPacket> actions = [];
        while (true)
        {
            InboundFrame frame = await server.NextFrameAsync(ct);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            if (codec.Type.Id == PlayPackets.Serverbound.ClientTickEnd.Id)
                return actions;
            if (codec.Type.Id == EntityPackets.Serverbound.PlayerAction.Id)
                actions.Add(Assert.IsType<ServerboundPlayerActionPacket>(codec.Decode(frame.Payload, context)));
        }
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTickSource : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public async Task FireAsync(CancellationToken ct)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completed, ct);
            await completed.Task.WaitAsync(ct);
        }

        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0;
            await foreach (TaskCompletionSource completed in _ticks.Reader.ReadAllAsync(ct))
            {
                yield return ++sequence;
                completed.TrySetResult();
            }
        }
    }
}
