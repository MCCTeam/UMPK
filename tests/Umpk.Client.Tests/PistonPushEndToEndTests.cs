using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
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

/// <summary>Checks client-owned piston movement through <see cref="UmpkClient"/> and <see cref="JavaConnection"/> with a scripted peer that sends no movement corrections.</summary>
/// <remarks>
/// <para>Vanilla servers also move players during piston ticks and can correct an incomplete client simulation. These tests isolate the client: one block event must produce the displacement and outgoing movement without a <see cref="PositionCorrected"/> event. The native fixture compares original 26.2 and 26.3 player and piston phases, including packet bytes and ground flags.</para>
/// </remarks>
public sealed class PistonPushEndToEndTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>1.21.11, protocol 774, one of the six protocols the third sweep recorded a FAIL on.</summary>
    private static JavaVersion Version => JavaVersions.V1_21_11;

    private static int Protocol => Version.Version.Protocol;

    /// <summary>Vanilla's full two-tick piston push (see <c>Umpk.Physics.Tests.PistonTests</c>).</summary>
    private const double VanillaFullPush = 0.81000001192092896;

    /// <summary>Tolerance retained by the older displacement-only fixtures; native packet comparisons use exact values.</summary>
    private const double BoxWidthTolerance = 1.5e-8;

    [Theory]
    [InlineData(776, false)]
    [InlineData(776, true)]
    [InlineData(777, false)]
    [InlineData(777, true)]
    public async Task PistonStatesAndMovementBytesMatchNativePlayerPhases(int protocol, bool blockEvent)
    {
        using Stream stream = typeof(PistonPushEndToEndTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.PistonPublication.native.json")!;
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement native = document.RootElement.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("protocol").GetInt32() == protocol && row.GetProperty("block_event").GetBoolean() == blockEvent);
        using var budget = new CancellationTokenSource(Budget);
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks, version);
        var frames = new ConcurrentQueue<InboundFrame>();
        await JoinAsync(client, server, frames, ct, version);
        var piston = new BlockPos(4, 100, 8);
        await BuildFixtureAsync(client, piston, ct);
        await TeleportAsync(client, server, new Vec3d(5.5, 100, 8.5), ct, version);
        await SettleAsync(client, ticks, ct);
        for (int tick = 0; tick < ticks.TickCount; tick++)
            await ReadTickMovementAsync(frames, version, ct);

        int corrections = 0;
        using IDisposable subscription = client.Events.Subscribe<PositionCorrected>(_ => Interlocked.Increment(ref corrections));
        if (blockEvent)
            await SendBlockEventAsync(client, server, piston, 0, ct, version);

        foreach (JsonElement step in native.GetProperty("states").EnumerateArray())
        {
            await ticks.FireAsync(ct);
            List<(Identifier Id, byte[] Payload)> observed = await ReadTickMovementAsync(frames, version, ct);
            JsonElement[] expected = [.. native.GetProperty("packets").EnumerateArray().Where(row => row.GetProperty("tick").GetInt32() == step.GetProperty("tick").GetInt32())];
            Assert.Equal(expected.Length, observed.Count);
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.Equal(NativeMovementId(expected[index].GetProperty("type").GetString()!), observed[index].Id);
                Assert.Equal(Convert.FromHexString(expected[index].GetProperty("payload").GetString()!), observed[index].Payload);
            }

            JsonElement position = step.GetProperty("after_piston");
            Assert.Equal(new Vec3d(position[0].GetDouble(), position[1].GetDouble(), position[2].GetDouble()), client.State.Self.Position);
            Assert.Equal(step.GetProperty("on_ground").GetBoolean(), client.State.Self.OnGround);
        }
        Assert.Equal(0, corrections);
    }

    private static Identifier NativeMovementId(string nativeType) => nativeType switch
    {
        "net.minecraft.network.protocol.game.ServerboundMovePlayerPacket$Pos" => EntityPackets.Serverbound.MovePlayerPos.Id,
        "net.minecraft.network.protocol.game.ServerboundMovePlayerPacket$StatusOnly" => EntityPackets.Serverbound.MovePlayerStatusOnly.Id,
        "net.minecraft.network.protocol.game.ServerboundClientTickEndPacket" => PlayPackets.Serverbound.ClientTickEnd.Id,
        _ => throw new InvalidOperationException($"Unexpected native movement packet: {nativeType}"),
    };

    private static async Task<List<(Identifier Id, byte[] Payload)>> ReadTickMovementAsync(ConcurrentQueue<InboundFrame> frames, JavaVersion version, CancellationToken ct)
    {
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        List<(Identifier Id, byte[] Payload)> packets = [];
        while (true)
        {
            if (!frames.TryDequeue(out InboundFrame frame))
            {
                await Task.Delay(1, ct);
                continue;
            }
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            Identifier id = codec.Type.Id;
            bool tickEnd = id == PlayPackets.Serverbound.ClientTickEnd.Id;
            if (!tickEnd && !id.ToString().StartsWith("minecraft:move_player_", StringComparison.Ordinal))
                continue;

            object decoded = codec.Decode(frame.Payload, PacketCodecContext.Registryless);
            var encoded = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(encoded);
            codec.Encode(ref writer, decoded, PacketCodecContext.Registryless);
            byte[] payload = frame.Payload.ToArray();
            Assert.Equal(payload, encoded.WrittenSpan.ToArray());
            packets.Add((id, payload));
            if (tickEnd)
                return packets;
        }
    }

    [Theory]
    [InlineData(770, false)]
    [InlineData(770, true)]
    [InlineData(774, false)]
    [InlineData(774, true)]
    [InlineData(775, false)]
    [InlineData(775, true)]
    [InlineData(776, false)]
    [InlineData(776, true)]
    [InlineData(777, false)]
    [InlineData(777, true)]
    public async Task PistonDisplacementReachesTheWireInItsNativePlayerPhase(int protocol, bool blockEvent)
    {
        using var budget = new CancellationTokenSource(Budget);
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks, version);
        var frames = new ConcurrentQueue<InboundFrame>();
        await JoinAsync(client, server, frames, ct, version);
        var piston = new BlockPos(4, 64, 8);
        await BuildFixtureAsync(client, piston, ct);
        var start = new Vec3d(piston.X + 1.5, 64, piston.Z + 0.5);
        await TeleportAsync(client, server, start, ct, version);
        await SettleAsync(client, ticks, ct);
        for (int tick = 0; tick < ticks.TickCount; tick++)
            await ReadTickPositionsAsync(frames, version, ct);

        int corrections = 0;
        using IDisposable subscription = client.Events.Subscribe<PositionCorrected>(_ => Interlocked.Increment(ref corrections));
        if (blockEvent)
            await SendBlockEventAsync(client, server, piston, 0, ct, version);

        double[] displacements = [0, 0.31000001192092896, VanillaFullPush, VanillaFullPush];
        double lastReported = start.X;
        for (int tick = 1; tick <= 3; tick++)
        {
            await ticks.FireAsync(ct);
            List<Vec3d> reported = await ReadTickPositionsAsync(frames, version, ct);
            Assert.InRange(reported.Count, 0, 1);
            if (reported.Count == 1)
                lastReported = reported[0].X;

            int visibleStep = protocol == 777 ? tick : tick - 1;
            Assert.Equal(start.X + (blockEvent ? displacements[visibleStep] : 0), lastReported, BoxWidthTolerance);
            Assert.Equal(start.X + (blockEvent ? displacements[tick] : 0), client.State.Self.Position.X, BoxWidthTolerance);
        }
        Assert.Equal(0, corrections);
    }

    private static async Task<List<Vec3d>> ReadTickPositionsAsync(ConcurrentQueue<InboundFrame> frames, JavaVersion version, CancellationToken ct)
    {
        var wire = new Internal.WireIndex(version);
        int tickEnd = wire.ServerboundPlay(PlayPackets.Serverbound.ClientTickEnd.Id);
        int position = wire.ServerboundPlay(EntityPackets.Serverbound.MovePlayerPos.Id);
        int positionRotation = wire.ServerboundPlay(EntityPackets.Serverbound.MovePlayerPosRot.Id);
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        List<Vec3d> positions = [];
        while (true)
        {
            if (!frames.TryDequeue(out InboundFrame frame))
            {
                await Task.Delay(1, ct);
                continue;
            }
            if (frame.WireId == tickEnd)
                return positions;
            if (frame.WireId != position && frame.WireId != positionRotation)
                continue;
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, PacketCodecContext.Registryless);
            var encoded = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(encoded);
            codec.Encode(ref writer, decoded, PacketCodecContext.Registryless);
            Assert.Equal(frame.Payload.ToArray(), encoded.WrittenSpan.ToArray());
            positions.Add(decoded switch
            {
                ServerboundMovePlayerPosPacket value => new Vec3d(value.X, value.Y, value.Z),
                ServerboundMovePlayerPosRotPacket value => new Vec3d(value.X, value.Y, value.Z),
                _ => throw new InvalidOperationException("Unexpected movement packet"),
            });
        }
    }

    /// <summary>The measured fixture: an east-facing piston, the player in the block it extends into, and a single <c>block_event</c>. Two ticks later the player must be 0.81 blocks further east, having been told nothing else by anybody.</summary>
    [Fact]
    public async Task ABlockEventAloneMovesThePlayerTheFullVanillaPush()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks);
        var frames = new ConcurrentQueue<InboundFrame>();

        await JoinAsync(client, server, frames, ct);

        var piston = new BlockPos(4, 64, 8);
        await BuildFixtureAsync(client, piston, ct);

        // Stand in the block the head will extend into.
        var start = new Vec3d(piston.X + 1.5, 64.0, piston.Z + 0.5);
        await TeleportAsync(client, server, start, ct);
        await SettleAsync(client, ticks, ct);
        Assert.Equal(start.X, client.State.Self.Position.X, 9);

        int corrections = 0;
        using IDisposable watch = client.Events.Subscribe<PositionCorrected>(_ => Interlocked.Increment(ref corrections));

        await SendBlockEventAsync(client, server, piston, action: 0, ct);

        for (int i = 0; i < 6; i++)
            await ticks.FireAsync(ct);

        double moved = client.State.Self.Position.X - start.X;
        Assert.Equal(VanillaFullPush, moved, BoxWidthTolerance);
        Assert.Equal(start.Z, client.State.Self.Position.Z, 9);

        // The attribution. Nothing arrived that could have supplied that number.
        Assert.Equal(0, corrections);

        // The value reaches its EFFECT, not merely a field: the client's own movement packet carries it.
        Vec3d last = await LastMovePositionAsync(frames, ticks.TickCount, ct);
        Assert.Equal(start.X + VanillaFullPush, last.X, BoxWidthTolerance);
    }

    /// <summary>The negative control this whole family needs: the SAME fixture, the SAME ticks, and no block event. Without it the test above would pass on a client that drifted east for any reason.</summary>
    [Fact]
    public async Task WithoutTheBlockEventThePlayerDoesNotMove()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks);
        var frames = new ConcurrentQueue<InboundFrame>();

        await JoinAsync(client, server, frames, ct);

        var piston = new BlockPos(4, 64, 8);
        await BuildFixtureAsync(client, piston, ct);

        var start = new Vec3d(piston.X + 1.5, 64.0, piston.Z + 0.5);
        await TeleportAsync(client, server, start, ct);
        await SettleAsync(client, ticks, ct);

        for (int i = 0; i < 6; i++)
            await ticks.FireAsync(ct);

        Assert.Equal(start.X, client.State.Self.Position.X, 9);
    }

    /// <summary>A block event at a position that is NOT a piston must be ignored, so a chest lid or a note block never reaches piston code. A tracker that keyed off the packet's block id instead would build a piston out of a chest.</summary>
    [Fact]
    public async Task ABlockEventOnANonPistonIsIgnored()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks);
        var frames = new ConcurrentQueue<InboundFrame>();

        await JoinAsync(client, server, frames, ct);

        var piston = new BlockPos(4, 64, 8);
        await BuildFixtureAsync(client, piston, ct);

        // Overwrite the piston with plain stone, then fire the identical block event at it.
        int stone = StateOf(client, "minecraft:stone");
        await client.InvokeAsync(
            c =>
            {
                c.State.World.SetBlockStateId(piston, stone);
                return 0;
            },
            ct);

        var start = new Vec3d(piston.X + 1.5, 64.0, piston.Z + 0.5);
        await TeleportAsync(client, server, start, ct);
        await SettleAsync(client, ticks, ct);

        await SendBlockEventAsync(client, server, piston, action: 0, ct);
        for (int i = 0; i < 6; i++)
            await ticks.FireAsync(ct);

        Assert.Equal(start.X, client.State.Self.Position.X, 9);
    }

    /// <summary>The push is spread over two ticks, 0.31 then 0.50. The test asserts each tick separately.</summary>
    [Fact]
    public async Task ThePushArrivesInTwoTicksOfPointThreeOneAndPointFive()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks);
        var frames = new ConcurrentQueue<InboundFrame>();

        await JoinAsync(client, server, frames, ct);

        var piston = new BlockPos(4, 64, 8);
        await BuildFixtureAsync(client, piston, ct);

        var start = new Vec3d(piston.X + 1.5, 64.0, piston.Z + 0.5);
        await TeleportAsync(client, server, start, ct);
        await SettleAsync(client, ticks, ct);

        await SendBlockEventAsync(client, server, piston, action: 0, ct);

        await ticks.FireAsync(ct);
        Assert.Equal(0.31000001192092896, client.State.Self.Position.X - start.X, BoxWidthTolerance);

        await ticks.FireAsync(ct);
        Assert.Equal(VanillaFullPush, client.State.Self.Position.X - start.X, BoxWidthTolerance);
    }

    /// <summary>THE PUSHED BLOCK, which is the half of the model that did not exist. A stone block sits in the piston's way and the player stands TWO blocks from the piston, where the piston's own head can never reach: the only thing that can move the player is the stone the piston carries.</summary>
    /// <remarks>The arithmetic is the same as the head's because the geometry is: the moved block's box comes to rest with its <c>maxX</c> exactly on the player's start plane, so tick one's movement is the player's own half-width (<c>float32(0.6)/2</c>) and tick two is clamped to the remaining delta. Total <c>0.30000001192092896 + 0.01 + 0.49 + 0.01</c>.</remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(350)]
    public async Task APushedBlockMovesThePlayerTheFullVanillaPush(int tickDelayMilliseconds)
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTickSource(TimeSpan.FromMilliseconds(tickDelayMilliseconds));
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks);
        var frames = new ConcurrentQueue<InboundFrame>();

        await JoinAsync(client, server, frames, ct);

        var piston = new BlockPos(4, 64, 8);
        await BuildFixtureAsync(client, piston, ct);
        await PlaceAsync(client, piston.Offset(Direction.East), "minecraft:stone", ct);

        // TWO blocks out, not one: outside the head's reach and inside the pushed block's.
        var start = new Vec3d(piston.X + 2.5, 64.0, piston.Z + 0.5);
        await TeleportAsync(client, server, start, ct);
        await SettleAsync(client, ticks, ct);
        Assert.Equal(start.X, client.State.Self.Position.X, 9);

        int corrections = 0;
        using IDisposable watch = client.Events.Subscribe<PositionCorrected>(_ => Interlocked.Increment(ref corrections));

        await SendBlockEventAsync(client, server, piston, action: 0, ct);

        for (int i = 0; i < 6; i++)
            await ticks.FireAsync(ct);

        double moved = client.State.Self.Position.X - start.X;
        Assert.Equal(VanillaFullPush, moved, BoxWidthTolerance);
        Assert.Equal(start.Z, client.State.Self.Position.Z, 9);
        Assert.Equal(0, corrections);

        Vec3d last = await LastMovePositionAsync(frames, ticks.TickCount, ct);
        Assert.Equal(start.X + VanillaFullPush, last.X, BoxWidthTolerance);
    }

    /// <summary>The discriminator for the test above, and the reason it is not measuring the piston head. IDENTICAL fixture with the pushed block left out: the head extends into empty air and the player, two blocks away, does not move at all.</summary>
    [Fact]
    public async Task WithNoBlockToPushTheHeadCannotReachAPlayerTwoBlocksAway()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks);
        var frames = new ConcurrentQueue<InboundFrame>();

        await JoinAsync(client, server, frames, ct);

        var piston = new BlockPos(4, 64, 8);
        await BuildFixtureAsync(client, piston, ct);

        var start = new Vec3d(piston.X + 2.5, 64.0, piston.Z + 0.5);
        await TeleportAsync(client, server, start, ct);
        await SettleAsync(client, ticks, ct);

        await SendBlockEventAsync(client, server, piston, action: 0, ct);
        for (int i = 0; i < 6; i++)
            await ticks.FireAsync(ct);

        Assert.Equal(start.X, client.State.Self.Position.X, 9);
    }

    /// <summary>The push-reaction data reaching its EFFECT. The same fixture with an ANVIL in the piston's way: its reaction is BLOCK, <c>piston push resolution</c> fails, vanilla moves nothing, and the player must not move even though the anvil has a real collision box that would have pushed it. A client that assumed every block is NORMAL would move the player 0.81 here.</summary>
    [Fact]
    public async Task AnAnvilInTheWayPushesNobody()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        // The anti-vacuous check: this only means anything because the anvil DOES collide.
        Assert.Equal(
            PistonPushReaction.Block,
            ReactionOf(JavaGameData.BlockPushData(Protocol), "minecraft:anvil"));
        Assert.NotEmpty(CollisionOf("minecraft:anvil").ToArray());

        var ticks = new ManualTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = Client(server, ticks);
        var frames = new ConcurrentQueue<InboundFrame>();

        await JoinAsync(client, server, frames, ct);

        var piston = new BlockPos(4, 64, 8);
        await BuildFixtureAsync(client, piston, ct);
        await PlaceAsync(client, piston.Offset(Direction.East), "minecraft:anvil", ct);

        var start = new Vec3d(piston.X + 2.5, 64.0, piston.Z + 0.5);
        await TeleportAsync(client, server, start, ct);
        await SettleAsync(client, ticks, ct);

        await SendBlockEventAsync(client, server, piston, action: 0, ct);
        for (int i = 0; i < 6; i++)
            await ticks.FireAsync(ct);

        Assert.Equal(start.X, client.State.Self.Position.X, 9);
    }

    private static PistonPushReaction ReactionOf(IBlockPushSource push, string name)
    {
        Assert.True(push.TryGet(Identifier.Parse(name), out BlockPushInfo info), name);
        return info.Reaction;
    }

    private static ReadOnlySpan<Umpk.Geometry.Aabb> CollisionOf(string name)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        Assert.True(blocks.TryGet(Identifier.Parse(name), out RegistryEntry<BlockDefinition> entry), name);
        return JavaGameData.BlockShapes(Protocol).GetCollisionShapes(entry.Value.DefaultStateId);
    }

    private static Task PlaceAsync(UmpkClient client, BlockPos position, string block, CancellationToken ct)
    {
        int state = StateOf(client, block);
        return client.InvokeAsync(
            c =>
            {
                c.State.World.SetBlockStateId(position, state);
                return 0;
            },
            ct);
    }

    /// <summary>Floor under the fixture plus an east-facing, unextended piston at <paramref name="piston"/>.</summary>
    private static async Task BuildFixtureAsync(UmpkClient client, BlockPos piston, CancellationToken ct)
    {
        int stone = StateOf(client, "minecraft:stone");
        int pistonState = PistonEast(client);

        await client.InvokeAsync(
            c =>
            {
                c.State.World.LoadColumn(new ChunkPos(piston.X >> 4, piston.Z >> 4));
                for (int x = piston.X - 2; x <= piston.X + 6; x++)
                    for (int z = piston.Z - 2; z <= piston.Z + 2; z++)
                        c.State.World.SetBlockStateId(new BlockPos(x, piston.Y - 1, z), stone);

                c.State.World.SetBlockStateId(piston, pistonState);
                return 0;
            },
            ct);
    }

    /// <summary>Runs enough ticks for the engine to settle on the floor before the measurement starts.</summary>
    private static async Task SettleAsync(UmpkClient client, ManualTickSource ticks, CancellationToken ct)
    {
        for (int i = 0; i < 5; i++)
            await ticks.FireAsync(ct);

        Assert.True(client.State.Self.OnGround);
    }

    /// <summary>Sends a <c>block_event</c> with action and facing fields, using EAST = 5, and waits for the client to have APPLIED it. Waiting on the client's own <see cref="BlockEventOccurred"/> rather than on a delay is what keeps the following ticks deterministic: the frame and the tick are two independent queues onto one session loop.</summary>
    private static async Task SendBlockEventAsync(
        UmpkClient client, FakeJavaServer server, BlockPos position, int action, CancellationToken ct, JavaVersion? version = null)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries((version ?? Version).Version.Protocol).Blocks;
        Assert.True(blocks.TryGet(Identifier.Minecraft("piston"), out RegistryEntry<BlockDefinition> entry));

        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = client.Events.Subscribe<BlockEventOccurred>(_ => applied.TrySetResult());
        await SendAsync(
            server,
            new ClientboundBlockEventPacket(position, (byte)action, (byte)Direction.East, entry.NetworkId),
            ct, version);
        await applied.Task.WaitAsync(Budget, ct);
    }

    private static async Task<Vec3d> LastMovePositionAsync(
        ConcurrentQueue<InboundFrame> frames, long completedTicks, CancellationToken ct)
    {
        // Each tick sends this marker after its movement packet. Observing the final marker proves the collector has received all completed ticks.
        int tickEndWire = new Internal.WireIndex(Version).ServerboundPlay(PlayPackets.Serverbound.ClientTickEnd.Id);
        await WaitForAsync(() => frames.Count(frame => frame.WireId == tickEndWire) >= completedTicks, ct);

        ProtocolDescriptor descriptor = Version.Protocol;
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound, out PhaseRegistry registry));
        int posWire = new Internal.WireIndex(Version).ServerboundPlay(EntityPackets.Serverbound.MovePlayerPos.Id);
        int posRotWire = new Internal.WireIndex(Version).ServerboundPlay(EntityPackets.Serverbound.MovePlayerPosRot.Id);

        Vec3d? last = null;
        foreach (InboundFrame frame in frames)
        {
            if (frame.WireId != posWire && frame.WireId != posRotWire)
                continue;

            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, PacketCodecContext.Registryless);
            last = decoded switch
            {
                ServerboundMovePlayerPosPacket pos => new Vec3d(pos.X, pos.Y, pos.Z),
                ServerboundMovePlayerPosRotPacket posRot => new Vec3d(posRot.X, posRot.Y, posRot.Z),
                _ => last,
            };
        }

        Assert.NotNull(last);
        return last.Value;
    }

    /// <summary>The state id of <c>minecraft:piston[facing=east,extended=false]</c> on this version.</summary>
    private static int PistonEast(UmpkClient client)
    {
        IBlockDataSource data = client.State.World.BlockData;
        Assert.True(data.Blocks.TryGet(Identifier.Minecraft("piston"), out RegistryEntry<BlockDefinition> entry));
        for (int state = entry.Value.MinStateId; state <= entry.Value.MaxStateId; state++)
            if (data.TryGetPropertyValue(state, "facing", out string facing) && facing == "east"
                && data.TryGetPropertyValue(state, "extended", out string extended) && extended == "false")
                return state;

        Assert.Fail("no minecraft:piston[facing=east,extended=false] state on this version");
        return 0;
    }

    private static int StateOf(UmpkClient client, string blockName)
    {
        IBlockDataSource data = client.State.World.BlockData;
        Assert.True(data.Blocks.TryGet(Identifier.Parse(blockName), out RegistryEntry<BlockDefinition> entry));
        return entry.Value.DefaultStateId;
    }

    private static async Task JoinAsync(
        UmpkClient client, FakeJavaServer server, ConcurrentQueue<InboundFrame> frames, CancellationToken ct, JavaVersion? version = null)
    {
        version ??= Version;
        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct, version);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct, version);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, version.Protocol, ct);
        await connect.WaitAsync(Budget, ct);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                    frames.Enqueue(await server.NextFrameAsync(ct).ConfigureAwait(false));

            }
            catch (Exception)
            {
                // The pipe closing at the end of the test is the expected way out.
            }
        }, ct);

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await SendAsync(server, ProtocolPhase.Play, new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null), ct, version);
        await joined.Task.WaitAsync(Budget, ct);
    }

    private static async Task TeleportAsync(
        UmpkClient client, FakeJavaServer server, Vec3d target, CancellationToken ct, JavaVersion? version = null)
    {
        var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
        await SendAsync(server, ProtocolPhase.Play, new ClientboundPlayerPositionPacket(
            target.X, target.Y, target.Z, 0f, 0f, RelativeFlags: 0, TeleportId: 1,
            ModernValues: new PositionMoveRotation(target, Vec3d.Zero, 0f, 0f)), ct, version);
        await corrected.Task.WaitAsync(Budget, ct);
    }

    private static UmpkClient Client(FakeJavaServer server, ITickSource ticks, JavaVersion? version = null) =>
        new UmpkClientBuilder()
            .UseVersion(version ?? Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries((version ?? Version).Version.Protocol))
            .UseTickSource(ticks)
            .ConfigureFeatures(f =>
            {
                f.Terrain = true;
                f.Physics = true;
                f.Pathfinding = false;
            })
            .Build();

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }

    private static Task SendAsync<TPacket>(FakeJavaServer server, TPacket packet, CancellationToken ct, JavaVersion? version = null)
        where TPacket : class, IPacket
        => SendAsync(server, ProtocolPhase.Play, packet, ct, version);

    private static async Task SendAsync<TPacket>(
        FakeJavaServer server, ProtocolPhase phase, TPacket packet, CancellationToken ct, JavaVersion? version = null)
        where TPacket : class, IPacket
    {
        ProtocolDescriptor descriptor = (version ?? Version).Protocol;
        Assert.True(descriptor.TryGetRegistry(phase, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != packet.Type.Id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented, $"{packet.Type.Id} is a marker in {phase}.");
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
            await server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct);
            return;
        }

        throw new Xunit.Sdk.XunitException($"{packet.Type.Id} is not registered clientbound in {phase}.");
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }

    /// <summary>A tick source that waits for the consumer to finish each tick before accepting the next test step.</summary>
    private sealed class ManualTickSource(TimeSpan processingDelay = default) : ITickSource
    {
        private readonly Channel<TickRequest> _ticks = Channel.CreateUnbounded<TickRequest>();
        private long _next;

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public long TickCount => Interlocked.Read(ref _next);

        public async IAsyncEnumerable<long> Ticks(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (TickRequest request in _ticks.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    if (processingDelay > TimeSpan.Zero)
                        await Task.Delay(processingDelay, cancellationToken);

                    yield return request.Number;
                    // UmpkClient requests the next item only after its scheduler invocation finishes.
                    request.Completed.TrySetResult();
                }
                finally
                {
                    request.Completed.TrySetCanceled(cancellationToken);
                }
            }
        }

        public async Task FireAsync(CancellationToken ct)
        {
            var request = new TickRequest(Interlocked.Increment(ref _next));
            await _ticks.Writer.WriteAsync(request, ct);
            await request.Completed.Task.WaitAsync(ct);
        }

        private sealed record TickRequest(long Number)
        {
            public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
