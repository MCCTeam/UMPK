using System.Buffers;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed partial class AttackInputTickPipelineTests
{
    public static IEnumerable<object[]> PrePositionChargeCases()
    {
        foreach (int protocol in new[] { 774, 775, 776, 777 })
            foreach (bool terrain in new[] { false, true })
                foreach (bool physics in new[] { false, true })
                    foreach (bool frozen in new[] { false, true })
                        yield return [protocol, terrain, physics, frozen];
    }

    [Theory]
    [MemberData(nameof(PrePositionChargeCases))]
    public async Task EligiblePlayerTicksChargeTheFirstClickBeforeInitialPosition(int protocol, bool terrain, bool physics, bool frozen)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var ticks = new ObservedTickSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = CreateClient(server, ticks, version, physics);
        await Task.WhenAll(client.ConnectAsync(new ServerEndpoint("test", 25565), ct), ScriptedServer.DriveToPlayAsync(server, version, ct));
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable join = client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
        await SendChargePacket(server, client, version, new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, null), ct);
        await joined.Task.WaitAsync(ct);
        var spear = client.State.Registries!.Items[Identifier.Minecraft("diamond_spear")];
        await SendChargePacket(server, client, version, new ClientboundContainerSetSlotPacket(0, 0, 36, new ItemStack(spear, 1)), ct);
        await SendChargePacket(server, client, version, new ClientboundTickingStatePacket(40f, frozen), ct);
        if (terrain)
            await SendChargeTerrain(server, client, version, ct);
        Assert.Empty(await ChargeBarrier(server, client, version, ct));

        // The native item-change phase resets the first eligible tick; the next five ticks charge a default-speed attack fully.
        for (int tick = 0; tick < 6; tick++)
        {
            await ticks.FireAsync(ct);
            Assert.Empty(await ReadChargeThroughTickEnd(server, client, version, ct));
        }

        if (!terrain)
            await SendChargeTerrain(server, client, version, ct);
        var position = new Vec3d(2.5, 65, 2.5);
        var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable correction = client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
        await SendChargePacket(server, client, version, new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 1, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)), ct);
        await corrected.Task.WaitAsync(ct);
        Assert.Empty(await ChargeBarrier(server, client, version, ct));
        Assert.Equal(frozen, client.State.WorldTicks.IsFrozen);

        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task mining = client.Actions.Interaction.DigBlockAsync(new BlockPos(1, 65, 1), Direction.North, caller.Token);
        try
        {
            await client.InvokeAsync(_ => true, ct);
            await ticks.FireAsync(ct);
            List<object> actions = await ReadChargeThroughTickEnd(server, client, version, ct);
            if (terrain)
            {
                Assert.Equal(protocol == 777 ? 1 : 2, actions.Count);
                var stab = Assert.IsType<ServerboundPlayerActionPacket>(actions[0]);
                Assert.Equal(protocol == 777 ? 8 : 7, stab.Action);
                Assert.Equal(default, stab.Position);
                Assert.Equal((byte)Direction.Down, stab.Direction);
                Assert.Equal(0, stab.Sequence);
                if (protocol != 777)
                    Assert.IsType<ServerboundSwingPacket>(actions[1]);
            }
            else
                Assert.Empty(actions);
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(ct));
        }
    }

    private static async Task SendChargeTerrain(FakeJavaServer server, UmpkClient client, JavaVersion version, CancellationToken ct)
    {
        int protocol = version.Version.Protocol;
        var empty = new ChunkPalettedContainerWire(0, 0, null, []);
        var sections = Enumerable.Range(0, client.State.World.Dimension.SectionCount).Select(_ => new ChunkSectionWire(0, protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
        var body = new ModernChunkWireBody(0, 0, protocol >= 775, [], sections, [], new byte[7]);
        byte[] bytes = ChunkCodecs.EncodeStructural(body);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "level_chunk_with_light");
        var context = new PacketCodecContext(client.State.Registries!, IConnectionCodecState.Empty);
        var chunk = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, context));
        Assert.True(chunk.FullChunk);
        Assert.Equal(0, chunk.ChunkX);
        Assert.Equal(0, chunk.ChunkZ);
        Assert.Equal(sections.Length, chunk.Column.SectionCount);
        Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(chunk)));
        await SendChargePacket(server, client, version, chunk, ct);
        int bedrock = client.State.Registries!.Blocks[Identifier.Minecraft("bedrock")].Value.DefaultStateId;
        await SendChargePacket(server, client, version, new ClientboundBlockUpdatePacket(new BlockPos(1, 65, 1), bedrock), ct);
        await SendChargePacket(server, client, version, new ClientboundBlockUpdatePacket(new BlockPos(2, 64, 2), bedrock), ct);
    }

    private static async Task SendChargePacket(FakeJavaServer server, UmpkClient client, JavaVersion version, IPacket packet, CancellationToken ct)
    {
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == packet.Type.Id);
        Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
        var context = new PacketCodecContext(client.State.Registries!, IConnectionCodecState.Empty);
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        codec.Encode(ref writer, packet, context);
        object decoded = codec.Decode(bytes.WrittenSpan, context);
        if (packet is ClientboundContainerSetSlotPacket slot)
            Assert.Equal(slot.Item, Assert.IsType<ClientboundContainerSetSlotPacket>(decoded).Item);
        else if (packet is ClientboundTickingStatePacket ticking)
            Assert.Equal(ticking, Assert.IsType<ClientboundTickingStatePacket>(decoded));
        else if (packet is ClientboundPlayerPositionPacket position)
        {
            var observed = Assert.IsType<ClientboundPlayerPositionPacket>(decoded);
            Assert.Equal(position.ModernValues, observed.ModernValues);
            Assert.Equal(position.TeleportId, observed.TeleportId);
        }
        else if (packet is ClientboundBlockUpdatePacket block)
            Assert.Equal(block, Assert.IsType<ClientboundBlockUpdatePacket>(decoded));
        var repeated = new ArrayBufferWriter<byte>();
        writer = new PacketWriter(repeated);
        codec.Encode(ref writer, decoded, context);
        Assert.Equal(bytes.WrittenSpan.ToArray(), repeated.WrittenSpan.ToArray());
        await server.SendFrameAsync(pair.WireId, bytes.WrittenSpan.ToArray(), ct);
    }

    private static async Task<List<object>> ChargeBarrier(FakeJavaServer server, UmpkClient client, JavaVersion version, CancellationToken ct)
    {
        await SendChargePacket(server, client, version, new ClientboundPlayKeepAlivePacket(9481), ct);
        List<object> actions = [];
        while (true)
        {
            object packet = await ReadChargePacket(server, client, version, ct);
            if (packet is ServerboundPlayKeepAlivePacket { Id: 9481 })
                return actions;
            if (packet is ServerboundPlayerActionPacket or ServerboundSwingPacket)
                actions.Add(packet);
        }
    }

    private static async Task<List<object>> ReadChargeThroughTickEnd(FakeJavaServer server, UmpkClient client, JavaVersion version, CancellationToken ct)
    {
        List<object> actions = [];
        while (true)
        {
            object packet = await ReadChargePacket(server, client, version, ct);
            if (packet is IPacket end && end.Type.Id == PlayPackets.Serverbound.ClientTickEnd.Id)
                return actions;
            if (packet is ServerboundPlayerActionPacket or ServerboundSwingPacket)
                actions.Add(packet);
        }
    }

    private static async Task<object> ReadChargePacket(FakeJavaServer server, UmpkClient client, JavaVersion version, CancellationToken ct)
    {
        InboundFrame frame = await server.NextFrameAsync(ct);
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
        var context = new PacketCodecContext(client.State.Registries!, IConnectionCodecState.Empty);
        object packet = codec.Decode(frame.Payload, context);
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        codec.Encode(ref writer, packet, context);
        Assert.Equal(frame.Payload.ToArray(), bytes.WrittenSpan.ToArray());
        return packet;
    }
}
