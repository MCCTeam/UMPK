using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>End to end, at the connection: an item component UMPK does not model costs the PACKET and the session keeps running. Everything here is real - real 1.21.5 codec registration, real <see cref="DescriptorFrameCodecBinding"/>, real length-delimited frames over a real <see cref="JavaConnection"/> read loop.</summary>
/// <remarks>
/// <para>These run with the DEFAULT <see cref="DecodeFailureMode.FailConnection"/> policy on purpose. The recovery is deliberately not routed through that policy: the policy governs framing strictness, and an unmodeled component is provably not a framing fault, since the offending id came out of the era's own component table. The companion assertion is <see cref="RealFramingFault_StillClosesTheConnection"/>: a fault that IS about framing still closes the session under the same options, so the recovery cannot be hiding one.</para>
/// </remarks>
public class UnmodeledComponentSessionSurvivalTests
{
    private const int Protocol1_21_5 = 770;
    private const int SetSlotWireId = 0x14; // minecraft:container_set_slot on 770
    private const int MapIdWireId = 37;     // minecraft:map_id on 770
    // minecraft:weapon is present on 770 and remains unmodeled by UMPK.
    private const int WeaponWireId = 26;

    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    [Fact]
    public async Task UnmodeledComponent_DropsOnlyThatPacket_AndTheSessionSurvives()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, DefaultishOptions());
        conn.BindCodec(RealBinding(), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Play);
        conn.Start();

        // Frame 1: a diamond sword carrying minecraft:weapon, which UMPK does not model. Its payload has no length prefix, so the rest of this frame is unreadable and the packet must be abandoned.
        await FramingTests.WriteRawFrameAsync(
            pair.Right.Output,
            Frame(SetSlot(slot: 3, itemId: ItemTestRegistries.DiamondSword, (WeaponWireId, 0))));

        // Frame 2: an ordinary filled map. If the session died on frame 1 this never arrives.
        await FramingTests.WriteRawFrameAsync(
            pair.Right.Output,
            Frame(SetSlot(slot: 9, itemId: ItemTestRegistries.FilledMap, (MapIdWireId, 4242))));

        InboundItem item = await conn.ReceiveAsync(Ct());
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(item.Packet);

        // The surviving packet is frame 2: frame 1 was dropped, not delivered and not fatal.
        Assert.Equal(9, packet.Slot);
        Assert.True(packet.Item.Components.TryGet(
            Game.Items.DataComponents.MapId, out Game.Items.Components.MapIdComponent? mapId));
        Assert.Equal(4242, mapId!.Id);

        // And the connection keeps serving: a third frame written afterwards still arrives.
        await FramingTests.WriteRawFrameAsync(
            pair.Right.Output,
            Frame(SetSlot(slot: 11, itemId: ItemTestRegistries.Stone)));
        InboundItem after = await conn.ReceiveAsync(Ct());
        Assert.Equal(11, Assert.IsType<ClientboundContainerSetSlotPacket>(after.Packet).Slot);
    }

    [Fact]
    public async Task ManyUnmodeledComponents_NeverEscalateToADisconnect()
    {
        // The recovery must be repeatable: a chest full of unmodeled stacks produces a stream of dropped packets, never a close. A once-per-connection Warning plus a counter is the observability.
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, DefaultishOptions());
        conn.BindCodec(RealBinding(), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Play);
        conn.Start();

        for (int i = 0; i < 25; i++)
            await FramingTests.WriteRawFrameAsync(
                pair.Right.Output,
                Frame(SetSlot(slot: i, itemId: ItemTestRegistries.DiamondSword, (WeaponWireId, i))));

        await FramingTests.WriteRawFrameAsync(
            pair.Right.Output,
            Frame(SetSlot(slot: 40, itemId: ItemTestRegistries.FilledMap, (MapIdWireId, 1))));

        InboundItem item = await conn.ReceiveAsync(Ct());
        Assert.Equal(40, Assert.IsType<ClientboundContainerSetSlotPacket>(item.Packet).Slot);
    }

    /// <summary>The guard. A component id outside the era's table is a framing fault, and framing faults must still kill the connection under the same options that let an unmodeled component through. If this ever starts passing as "survived", the recovery has widened into a catch-all and is masking real desyncs.</summary>
    [Fact]
    public async Task RealFramingFault_StillClosesTheConnection()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, DefaultishOptions());
        conn.BindCodec(RealBinding(), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Play);
        conn.Start();

        // 770 declares 96 components (0..95); 96 is one past the end, so the reader is provably lost.
        await FramingTests.WriteRawFrameAsync(
            pair.Right.Output,
            Frame(SetSlot(slot: 1, itemId: ItemTestRegistries.DiamondSword, (96, 0))));

        var closed = await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await conn.ReceiveAsync(Ct());

        });

        Assert.Equal(CloseReason.ProtocolViolation, closed.Reason);
    }

    /// <summary>A truncated component payload is an ordinary decode fault and stays fatal too.</summary>
    [Fact]
    public async Task TruncatedComponentPayload_StillClosesTheConnection()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, DefaultishOptions());
        conn.BindCodec(RealBinding(), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Play);
        conn.Start();

        // Announces one map_id component and then simply ends.
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(0);   // container id
        w.WriteVarInt(1);   // state id
        w.WriteShort(5);    // slot
        w.WriteVarInt(1);   // count
        w.WriteVarInt(ItemTestRegistries.FilledMap);
        w.WriteVarInt(1);   // added
        w.WriteVarInt(0);   // removed
        w.WriteVarInt(MapIdWireId);

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, Frame(buffer.WrittenSpan.ToArray()));

        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await conn.ReceiveAsync(Ct());

        });
    }

    // Helpers.

    // The stock options a client uses. DecodeFailurePolicy is left at its FailConnection default on purpose, so these prove the recovery does not depend on relaxing it.
    private static JavaConnectionOptions DefaultishOptions() => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Throw,
        ReadIdleTimeout = TimeSpan.Zero,
    };

    private static DescriptorFrameCodecBinding RealBinding()
    {
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "1.21.5", Protocol1_21_5), new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, SetSlotWireId, "minecraft:container_set_slot");
        return new DescriptorFrameCodecBinding(builder.Build(), ItemTestRegistries.Context);
    }

    /// <summary>Prefixes a packet body with its wire-id VarInt to make the frame content.</summary>
    private static byte[] Frame(byte[] body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(SetSlotWireId);
        w.WriteBytes(body);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>A 770 container_set_slot body: VarInt container id, VarInt state id, short slot, then the modern component stack. Each component is its wire id followed by an INLINE VarInt payload with no length prefix, which is exactly why an unmodeled one cannot be skipped.</summary>
    private static byte[] SetSlot(int slot, int itemId, params (int WireId, int Value)[] components)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(0);            // container id (player inventory)
        w.WriteVarInt(slot + 100);   // state id, distinct per frame
        w.WriteShort((short)slot);
        w.WriteVarInt(1);            // count
        w.WriteVarInt(itemId);
        w.WriteVarInt(components.Length);
        w.WriteVarInt(0);            // removed
        foreach ((int wireId, int value) in components)
        {
            w.WriteVarInt(wireId);
            w.WriteVarInt(value);
        }

        return buffer.WrittenSpan.ToArray();
    }
}
