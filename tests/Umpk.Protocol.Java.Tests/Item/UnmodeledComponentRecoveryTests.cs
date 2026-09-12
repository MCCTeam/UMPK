using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>A filled map must decode, and a component UMPK does not model must cost a PACKET rather than the SESSION.</summary>
/// <remarks>
/// <c>minecraft:map_id</c> is listed in every 1.20.5+ component table and must decode without being treated as a framing fault.
/// <para>Every frame here is composed at the FIELD level and pushed through the codec the registrar actually binds for that protocol number (<see cref="BoundCodec"/> -> <see cref="BoundPacketCodec.Decode"/>, the dispatcher's entry point), never through the codec under test. Component payloads carry realistic non-empty values, and each case asserts the re-encode is byte-identical: an empty stack or an empty patch decodes the same under every era table and would pin nothing.</para>
/// </remarks>
public class UnmodeledComponentRecoveryTests
{
    private const string ContainerSetSlot = "minecraft:container_set_slot";

    /// <summary>The <c>minecraft:map_id</c> wire id per era, which is its index in that era's registration order. These constants are the byte-level pin: a table that reordered would decode a different component here and the round-trip assertions would fail.</summary>
    public static TheoryData<int, int> MapIdWireIds => new()
    {
        { 766, 26 },
        { 767, 26 },
        { 770, 37 },
        { 776, 46 },
    };

    /// <summary>A filled map decodes rather than dropping the connection, on every era that has the component. The stack carries a realistic pair: the saved-map id plus the pending post-processing a freshly crafted zoomed-out map still owes.</summary>
    [Theory]
    [MemberData(nameof(MapIdWireIds))]
    public void FilledMap_DecodesOnEveryComponentWireLayout(int protocol, int mapIdWireId)
    {
        // map_post_processing sits directly after map_decorations, which sits directly after map_id.
        int mapPostProcessingWireId = mapIdWireId + 2;

        byte[] frame = SetSlotFrame(
            protocol,
            containerId: 0,
            stateId: 41,
            slot: 17,
            count: 1,
            itemId: ItemTestRegistries.FilledMap,
            components:
            [
                (mapIdWireId, static (ref PacketWriter w) => w.WriteVarInt(1234)),
                (mapPostProcessingWireId, static (ref PacketWriter w) => w.WriteVarInt(1)), // SCALE
            ]);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, ContainerSetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.Equal(17, packet.Slot);
        Assert.Equal(41, packet.StateId);
        Assert.Equal(1, packet.Item.Count);
        Assert.Equal(Identifier.Minecraft("filled_map"), packet.Item.Item.Id);

        Assert.True(packet.Item.Components.TryGet(DataComponents.MapId, out MapIdComponent? mapId));
        Assert.Equal(1234, mapId!.Id);

        Assert.True(packet.Item.Components.TryGet(
            DataComponents.MapPostProcessing, out MapPostProcessingComponent? post));
        Assert.Equal(MapPostProcessingKind.Scale, post!.Kind);

        // Frame-exact both ways: the decoded stack re-encodes to the original bytes.
        Assert.Equal(frame, bound.Encode(packet));
    }

    /// <summary>The map family's third component is a network NBT tag, not a scalar: vanilla registers <c>map_decorations</c> uses its persistent representation on the network, which is one NBT tag. A real map with a marker on it carries all three.</summary>
    [Theory]
    [MemberData(nameof(MapIdWireIds))]
    public void FilledMap_WithDecorations_RoundTripsTheNbtPayload(int protocol, int mapIdWireId)
    {
        var decorations = new Nbt.NbtCompound
        {
            ["frame-42"] = new Nbt.NbtCompound
            {
                ["type"] = new Nbt.NbtString("minecraft:frame"),
                ["x"] = new Nbt.NbtDouble(128.5),
                ["z"] = new Nbt.NbtDouble(-64.25),
                ["rotation"] = new Nbt.NbtFloat(180f),
            },
        };

        byte[] frame = SetSlotFrame(
            protocol,
            containerId: 0,
            stateId: 3,
            slot: 8,
            count: 1,
            itemId: ItemTestRegistries.FilledMap,
            components:
            [
                (mapIdWireId, static (ref PacketWriter w) => w.WriteVarInt(7)),
                (mapIdWireId + 1, (ref PacketWriter w) =>
                    w.WriteNbt(decorations, ItemCodecPrimitivesNetworkNbt)),
            ]);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, ContainerSetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.True(packet.Item.Components.TryGet(
            DataComponents.MapDecorations, out NbtPayloadComponent? decoded));
        var compound = Assert.IsType<Nbt.NbtCompound>(decoded!.Tag);
        var entry = Assert.IsType<Nbt.NbtCompound>(compound["frame-42"]);
        Assert.Equal("minecraft:frame", Assert.IsType<Nbt.NbtString>(entry["type"]).Value);
        Assert.Equal(128.5, Assert.IsType<Nbt.NbtDouble>(entry["x"]).Value);

        Assert.Equal(frame, bound.Encode(packet));
    }

    /// <summary>The recovery's shape on the COMPACT patch: a component the era declares but UMPK does not model raises <see cref="UnmodeledItemComponentException"/> (the packet-scoped fault), carrying both the component identity and the packet identity, and NOT <see cref="ProtocolViolationException"/>. That distinction is the whole fix: the read loop keys off the type.</summary>
    [Fact]
    public void CompactPatch_UnmodeledComponent_RaisesThePacketScopedFault()
    {
        // minecraft:weapon on 1.21.5 (wire id 26) is a real, still-unmodeled component with a composite payload. Its bytes here are deliberate garbage: the point is that the fault fires on the id lookup, before any payload is read. (This probe named minecraft:equippable, wire id 28, until that one got a codec; it had to move to a component no era types.)
        const int weaponWireId = 26;
        byte[] frame = SetSlotFrame(
            770,
            containerId: 0,
            stateId: 1,
            slot: 0,
            count: 1,
            itemId: ItemTestRegistries.DiamondSword,
            components: [(weaponWireId, static (ref PacketWriter w) => w.WriteVarInt(0))]);

        BoundPacketCodec bound = BoundCodec.At(770, PacketFlow.Clientbound, ContainerSetSlot);

        var ex = Assert.Throws<UnmodeledItemComponentException>(() => bound.DecodeFrame(frame));
        Assert.Equal(weaponWireId, ex.ComponentWireId);
        Assert.Equal(Identifier.Minecraft("weapon"), ex.ComponentId);
        Assert.Equal(0, ex.WireId); // BoundCodec registers at wire id 0.
        Assert.IsNotType<ProtocolViolationException>(ex);
    }

    /// <summary>THE guard the recovery must never breach. A component id OUTSIDE the era's table is a framing fault, not a modeling gap, so it stays a fatal <see cref="ProtocolViolationException"/> and never reaches the packet-scoped recovery. Without this split, a misaligned reader landing on a large VarInt would be silently swallowed as "some component we do not model yet".</summary>
    [Fact]
    public void CompactPatch_OutOfRangeComponentId_StaysFatal()
    {
        // 770 declares 96 components (0..95), so 96 is one past the end.
        byte[] frame = SetSlotFrame(
            770,
            containerId: 0,
            stateId: 1,
            slot: 0,
            count: 1,
            itemId: ItemTestRegistries.DiamondSword,
            components: [(96, static (ref PacketWriter w) => w.WriteVarInt(0))]);

        BoundPacketCodec bound = BoundCodec.At(770, PacketFlow.Clientbound, ContainerSetSlot);

        Assert.Throws<ProtocolViolationException>(() => bound.DecodeFrame(frame));
    }

    /// <summary>The second guard: an ordinary decode fault still fails loudly. A truncated component payload has nothing to do with an unmodeled component, so it must keep raising <see cref="ProtocolViolationException"/> and keep killing the connection. If the recovery were a catch-all around decode errors instead of one typed fault, this test would see the packet quietly dropped and a real framing bug would go unnoticed.</summary>
    [Fact]
    public void TruncatedPayload_ForAModeledComponent_StaysFatal()
    {
        // A well-formed header claiming one map_id component, then the frame simply ends. map_id IS modeled now, so this exercises the ordinary overrun path, not the modeling path.
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(0);   // container id
        w.WriteVarInt(1);   // state id
        w.WriteShort(0);    // slot
        w.WriteVarInt(1);   // count
        w.WriteVarInt(ItemTestRegistries.FilledMap);
        w.WriteVarInt(1);   // added
        w.WriteVarInt(0);   // removed
        w.WriteVarInt(37);  // map_id on 770
        // ... and no payload at all.

        BoundPacketCodec bound = BoundCodec.At(770, PacketFlow.Clientbound, ContainerSetSlot);

        Assert.Throws<ProtocolViolationException>(() => bound.DecodeFrame(buffer.WrittenSpan.ToArray()));
    }

    /// <summary>Trailing garbage after a valid packet also stays fatal. This is the frame-exactness check in <see cref="BoundPacketCodec.Decode"/>, and it is the check that would catch a genuine framing regression, so the recovery must not weaken it.</summary>
    [Fact]
    public void TrailingBytes_AfterAValidFilledMap_StayFatal()
    {
        byte[] frame = SetSlotFrame(
            770,
            containerId: 0,
            stateId: 1,
            slot: 0,
            count: 1,
            itemId: ItemTestRegistries.FilledMap,
            components: [(37, static (ref PacketWriter w) => w.WriteVarInt(99))]);

        byte[] withGarbage = [.. frame, 0xEE, 0xEE];

        BoundPacketCodec bound = BoundCodec.At(770, PacketFlow.Clientbound, ContainerSetSlot);

        Assert.Throws<ProtocolViolationException>(() => bound.DecodeFrame(withGarbage));
    }

    /// <summary>A REMOVAL of an unmodeled component is lossless and must not fault at all. A removal entry carries only the wire id and no payload, so nothing can desync: the era's placeholder key re-encodes to the same wire id. Removal therefore needs a different path from addition.</summary>
    [Fact]
    public void RemovalOfAnUnmodeledComponent_RoundTripsWithoutFaulting()
    {
        const int weaponWireId = 26; // unmodeled on 770 (see CompactPatch_... for why not equippable)
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(0);
        w.WriteVarInt(5);
        w.WriteShort(2);
        w.WriteVarInt(1);
        w.WriteVarInt(ItemTestRegistries.DiamondSword);
        w.WriteVarInt(0);                 // added
        w.WriteVarInt(1);                 // removed
        w.WriteVarInt(weaponWireId);      // ... this one
        byte[] frame = buffer.WrittenSpan.ToArray();

        BoundPacketCodec bound = BoundCodec.At(770, PacketFlow.Clientbound, ContainerSetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.Equal(frame, bound.Encode(packet));
    }

    /// <summary>A DELIMITED patch is length-prefixed, so an unmodeled component is fully recoverable there: its payload is captured verbatim and re-emitted byte-for-byte. No packet is lost on this path, which is why the recovery is scoped to the compact form alone.</summary>
    [Fact]
    public void DelimitedPatch_CapturesAnUnmodeledPayloadVerbatim()
    {
        const int weaponWireId = 26;
        byte[] payload = [0x07, 0x2A, 0x01, 0xFF, 0x10];

        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(1);                        // count
        w.WriteVarInt(ItemTestRegistries.DiamondSword);
        w.WriteVarInt(1);                        // added
        w.WriteVarInt(0);                        // removed
        w.WriteVarInt(weaponWireId);
        w.WriteByteArray(payload);               // length-prefixed
        byte[] bytes = buffer.WrittenSpan.ToArray();

        var reader = new PacketReader(bytes);
        ItemStack stack = ItemStackCodecs.ReadDelimitedStack(
            ref reader, ItemTestRegistries.Context, ItemStackCodecs.ComponentsV1_21_5);
        Assert.Equal(0, reader.Remaining);

        DataComponentEntry entry = Assert.Single(stack.Components.Patch);
        var unmodeled = Assert.IsType<UnmodeledComponent>(entry.Value);
        Assert.Equal(Identifier.Minecraft("weapon"), unmodeled.ComponentId);
        Assert.Equal(payload, unmodeled.Payload.ToArray());

        var out1 = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(out1);
        ItemStackCodecs.WriteDelimitedStack(
            ref writer, stack, ItemTestRegistries.Context, ItemStackCodecs.ComponentsV1_21_5);
        Assert.Equal(bytes, out1.WrittenSpan.ToArray());
    }

    // Helpers.

    private const Nbt.NbtWireFormat ItemCodecPrimitivesNetworkNbt = Nbt.NbtWireFormat.JavaUnnamedRoot;

    private delegate void PayloadWriter(ref PacketWriter writer);

    /// <summary>Composes a container_set_slot frame at the field level. 766/767 write a SIGNED BYTE container id; 770/776 write a VarInt. Everything after that is shared: VarInt state id, short slot, then the modern component stack (VarInt count, VarInt item holder id, VarInt added, VarInt removed, then each component as its wire id followed by an inline payload with no length prefix).</summary>
    private static byte[] SetSlotFrame(
        int protocol,
        int containerId,
        int stateId,
        int slot,
        int count,
        int itemId,
        (int WireId, PayloadWriter Write)[] components)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);

        if (protocol < 770)
            w.WriteByte((byte)(sbyte)containerId);

        else
            w.WriteVarInt(containerId);

        w.WriteVarInt(stateId);
        w.WriteShort((short)slot);

        w.WriteVarInt(count);
        w.WriteVarInt(itemId);
        w.WriteVarInt(components.Length);
        w.WriteVarInt(0); // removed
        foreach ((int wireId, PayloadWriter write) in components)
        {
            w.WriteVarInt(wireId);
            write(ref w);
        }

        return buffer.WrittenSpan.ToArray();
    }
}
