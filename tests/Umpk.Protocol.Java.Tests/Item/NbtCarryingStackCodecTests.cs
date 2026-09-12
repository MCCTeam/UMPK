using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Frame-decode and round-trip coverage for container packets whose item stacks CARRY NBT, on every band between the flattening and the component era.</summary>
/// <remarks>
/// These exist because of a live 1.16.5 failure: every NBT-carrying stack left trailing bytes and faulted the frame (38 trailing on a renamed stone, 106 on a written book). The 477-763 band was decoding its item NBT as an UNNAMED root, which is the 764+ form. Through 1.20.1 the wire uses a NAMED root: type byte, then an empty root-name string, then the body. Protocol 764 is the first that uses an unnamed root.
/// <para>Why nothing caught it before, and what these tests do differently:</para>
/// <list type="number">
/// <item>An EMPTY tag is a single bare TAG_End byte under BOTH root flavors, so every existing
/// empty-stack or plain-stack case decoded identically either way. Every stack here carries real NBT.</item>
/// <item>A pure round-trip (encode then decode with the SAME codec) is self-consistent under either
/// flavor, so it cannot detect a root-framing mismatch at all. Each band below is therefore pinned by a FRAME DECODE against hand-authored expected bytes, not just by a round trip.</item>
/// <item>The corpus conformance suite decodes with <c>PacketCodecContext.Registryless</c>, so any stack
/// carrying a real item id throws there regardless; it structurally cannot cover this. These run against the populated <see cref="ItemTestRegistries"/> context.</item>
/// </list>
/// </remarks>
public class NbtCarryingStackCodecTests
{
    /// <summary>A custom-named item: <c>{display:{Name:"Bob"}}</c>, the live 1.16.5 repro shape.</summary>
    private static NbtCompound CustomName()
    {
        var display = new NbtCompound();
        display.PutString("Name", "Bob");
        var root = new NbtCompound();
        root.Put("display", display);
        return root;
    }

    /// <summary>A written book: title, author and a page list, the 106-trailing-byte live repro.</summary>
    private static NbtCompound WrittenBook()
    {
        var pages = new NbtList(NbtTagType.String);
        pages.Add(new NbtString("{\"text\":\"page one\"}"));
        pages.Add(new NbtString("{\"text\":\"page two\"}"));
        var root = new NbtCompound();
        root.PutString("title", "Field Notes");
        root.PutString("author", "Bob");
        root.Put("pages", pages);
        root.PutInt("generation", 0);
        return root;
    }

    // The literal named-root encoding of CustomName(), authored by hand from the wire contract:
    //   0A                 TAG_Compound, the root type
    //   00 00              the empty ROOT NAME string; this is the pair the unnamed reader mis-consumed
    //   0A 00 07 display   TAG_Compound member "display"
    //   08 00 04 Name      TAG_String member "Name"
    //   00 03 Bob          its value
    //   00                 TAG_End of "display"
    //   00                 TAG_End of the root
    private static readonly byte[] CustomNameNamedRoot =
    [
        0x0A,
        0x00, 0x00,
        0x0A, 0x00, 0x07, .. "display"u8,
        0x08, 0x00, 0x04, .. "Name"u8,
        0x00, 0x03, .. "Bob"u8,
        0x00,
        0x00,
    ];

    // The same tag under the 764+ UNNAMED root: identical except the two root-name bytes are absent.
    private static readonly byte[] CustomNameUnnamedRoot =
    [
        0x0A,
        0x0A, 0x00, 0x07, .. "display"u8,
        0x08, 0x00, 0x04, .. "Name"u8,
        0x00, 0x03, .. "Bob"u8,
        0x00,
        0x00,
    ];

    // A present-id stack carrying CustomName(): present(01), VarInt id 1 (stone), byte count 1, then the NAMED-root tag. This is the 404-763 slot form: present, item id, count, then NBT.
    private static readonly byte[] NamedStoneStack = [0x01, 0x01, 0x01, .. CustomNameNamedRoot];

    private const byte EmptySlot = 0x00;

    private static T DecodeExact<T>(PacketCodec<T> codec, byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        T decoded = codec.Decode(ref reader, ItemTestRegistries.Context);
        Assert.Equal(0, reader.Remaining);
        return decoded;
    }

    private static ItemStack WithNbt(int itemId, int count, NbtCompound tag) =>
        new(ItemTestRegistries.Item(itemId), count,
            DataComponentMap.Empty.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag)));

    private static NbtCompound CarriedNbt(ItemStack stack)
    {
        Assert.True(stack.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy));
        return legacy!.Nbt;
    }

    private static void AssertIsCustomName(ItemStack stack)
    {
        NbtCompound tag = CarriedNbt(stack);
        Assert.True(tag.TryGet("display", out NbtCompound? display));
        Assert.Equal("Bob", display!.GetString("Name"));
    }

    private static void AssertIsWrittenBook(ItemStack stack)
    {
        NbtCompound tag = CarriedNbt(stack);
        Assert.Equal("Field Notes", tag.GetString("title"));
        Assert.Equal("Bob", tag.GetString("author"));
        NbtList pages = Assert.IsType<NbtList>(tag.GetList("pages"));
        Assert.Equal(2, pages.Count);
    }

    // The two flavors differ only by the empty root-name pair, and an unnamed reader pointed at named bytes stops after two bytes (it takes the name length's high byte as a TAG_End member) and abandons the rest. This is why the live 1.16.5 frames had trailing bytes.
    [Fact]
    public void NamedAndUnnamedRoots_DifferOnlyByTheRootName_AndMisreadingLeavesTrailingBytes()
    {
        Assert.Equal(CustomNameNamedRoot, NbtWriter.ToArray(CustomName(), NbtWireFormat.JavaNamedRoot));
        Assert.Equal(CustomNameUnnamedRoot, NbtWriter.ToArray(CustomName(), NbtWireFormat.JavaUnnamedRoot));
        Assert.Equal(CustomNameNamedRoot.Length - 2, CustomNameUnnamedRoot.Length);

        var reader = new PacketReader(CustomNameNamedRoot);
        NbtTag misread = reader.ReadNbt(NbtWireFormat.JavaUnnamedRoot);
        Assert.Empty(Assert.IsType<NbtCompound>(misread));
        Assert.Equal(CustomNameNamedRoot.Length - 2, reader.Remaining);

        // An empty tag, by contrast, is a bare TAG_End byte under both flavors, so empty-stack round trips cannot distinguish the formats.
        Assert.Equal([0x00], NbtWriter.ToArray(NbtEnd.Instance, NbtWireFormat.JavaNamedRoot));
        Assert.Equal([0x00], NbtWriter.ToArray(NbtEnd.Instance, NbtWireFormat.JavaUnnamedRoot));
    }

    // The live repro. 1.16.5 read(): readUnsignedByte containerId, readShort count, count * readItem.
    [Fact]
    public void SetContent_1_16_5_NbtCarryingStack_DecodesExactly_AndReEncodes()
    {
        byte[] wire = [0x01, 0x00, 0x02, .. NamedStoneStack, EmptySlot];
        var decoded = DecodeExact(ContainerCodecs.ContainerSetContentV1_14, wire);

        Assert.Equal(1, decoded.ContainerId);
        Assert.Equal(2, decoded.Items.Count);
        Assert.Equal(ItemTestRegistries.Stone, decoded.Items[0].Item.NetworkId);
        AssertIsCustomName(decoded.Items[0]);
        Assert.True(decoded.Items[1].IsEmpty);

        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetContentV1_14, decoded));
    }

    // 1.16.5 read(): readByte containerId, readShort slot, readItem.
    [Fact]
    public void SetSlot_1_16_5_NbtCarryingStack_DecodesExactly_AndReEncodes()
    {
        byte[] wire = [0x01, 0x00, 0x03, .. NamedStoneStack];
        var decoded = DecodeExact(ContainerCodecs.ContainerSetSlotV1_14, wire);

        Assert.Equal(1, decoded.ContainerId);
        Assert.Equal(3, decoded.Slot);
        AssertIsCustomName(decoded.Item);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlotV1_14, decoded));
    }

    // 1.16.5 write(): writeByte containerId, writeShort slot, writeByte button, writeShort action, writeEnum mode (VarInt), writeItem clicked.
    [Fact]
    public void ContainerClick_1_16_5_NbtCarryingStack_DecodesExactly_AndReEncodes()
    {
        byte[] wire = [0x01, 0x00, 0x05, 0x00, 0x00, 0x07, 0x00, .. NamedStoneStack];
        var decoded = DecodeExact(ContainerCodecs.ContainerClickV1_14, wire);

        Assert.Equal(1, decoded.ContainerId);
        Assert.Equal(5, decoded.Slot);
        Assert.Equal(7, decoded.ActionNumber);
        AssertIsCustomName(decoded.LegacyClickedItem!);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClickV1_14, decoded));
    }

    [Fact]
    public void SetSlot_1_16_5_WrittenBook_RoundTrips_ByteStable()
    {
        var slot = new ClientboundContainerSetSlotPacket(
            ContainerId: 0, StateId: 0, Slot: 8,
            Item: WithNbt(ItemTestRegistries.DiamondSword, 1, WrittenBook()));

        var cycled = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetSlotV1_14, slot);
        AssertIsWrittenBook(cycled.Item);
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.ContainerSetSlotV1_14, slot);

        // The encoded stack must carry the empty root name; without it the server sees a short tag.
        byte[] encoded = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlotV1_14, slot);
        // containerId(1) + slot short(2) + present(1) + id(1) + count(1) = 6, then the tag.
        Assert.Equal(0x0A, encoded[6]);
        Assert.Equal(0x00, encoded[7]);
        Assert.Equal(0x00, encoded[8]);
    }

    // Protocol 755 (1.17): the sync-map click, still using a named root.

    [Fact]
    public void ContainerClick_1_17_NbtCarryingStack_DecodesExactly_AndReEncodes()
    {
        // 1.17 read(): readByte id, readShort slot, readByte button, readVarInt mode, readVarInt changedCount, (readShort slot + readItem)*, readItem carried. No state id.
        byte[] wire = [0x02, 0x00, 0x0A, 0x00, 0x00, 0x01, 0x00, 0x0A, .. NamedStoneStack, EmptySlot];
        var decoded = DecodeExact(ContainerCodecs.ContainerClickV1_17, wire);

        Assert.Equal(2, decoded.ContainerId);
        Assert.Equal(0, decoded.StateId);
        PredictedSlot changed = Assert.Single(decoded.ChangedSlots);
        Assert.Equal(10, changed.Slot);
        AssertIsCustomName(changed.Stack);
        Assert.True(decoded.CarriedItem is { IsEmpty: true });
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClickV1_17, decoded));
    }

    // Protocols 756-763 (1.17.1-1.20.1): state id added, root still named.

    [Fact]
    public void SetSlot_1_17_1_NbtCarryingStack_DecodesExactly_AndReEncodes()
    {
        // 1.17.1 read(): readByte id, readVarInt stateId, readShort slot, readItem.
        byte[] wire = [0x01, 0x05, 0x00, 0x03, .. NamedStoneStack];
        var decoded = DecodeExact(ContainerCodecs.ContainerSetSlotV1_17_1, wire);

        Assert.Equal(5, decoded.StateId);
        Assert.Equal(3, decoded.Slot);
        AssertIsCustomName(decoded.Item);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlotV1_17_1, decoded));
    }

    [Fact]
    public void SetContent_1_17_1_NbtCarryingStack_DecodesExactly_AndReEncodes()
    {
        // 1.17.1: unsigned-byte id, VarInt state id, count-prefixed item collection, carried item.
        byte[] wire = [0x01, 0x05, 0x01, .. NamedStoneStack, EmptySlot];
        var decoded = DecodeExact(ContainerCodecs.ContainerSetContentV1_17_1, wire);

        Assert.Equal(5, decoded.StateId);
        AssertIsCustomName(Assert.Single(decoded.Items));
        Assert.True(decoded.CarriedItem.IsEmpty);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetContentV1_17_1, decoded));
    }

    [Fact]
    public void ContainerClick_1_17_1_NbtCarryingStack_DecodesExactly_AndReEncodes()
    {
        byte[] wire = [0x02, 0x07, 0x00, 0x0A, 0x00, 0x00, 0x01, 0x00, 0x0A, .. NamedStoneStack, EmptySlot];
        var decoded = DecodeExact(ContainerCodecs.ContainerClickV1_17_1, wire);

        Assert.Equal(7, decoded.StateId);
        AssertIsCustomName(Assert.Single(decoded.ChangedSlots).Stack);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClickV1_17_1, decoded));
    }

    [Fact]
    public void SetContent_1_17_1_WrittenBook_RoundTrips_ByteStable()
    {
        var content = new ClientboundContainerSetContentPacket(
            ContainerId: 1, StateId: 2,
            Items: [WithNbt(ItemTestRegistries.DiamondSword, 1, WrittenBook()), ItemStack.Empty],
            CarriedItem: ItemStack.Empty);

        var cycled = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetContentV1_17_1, content);
        AssertIsWrittenBook(cycled.Items[0]);
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.ContainerSetContentV1_17_1, content);
    }

    // Protocols 764/765 (1.20.2-1.20.3): the boundary guard.

    // 1.20.2 dropped the root name, so the SAME logical stack is two bytes shorter here. This guards the split from being "fixed" by moving the whole band to one flavor.
    [Fact]
    public void SetSlot_1_20_2_NbtCarryingStack_UsesUnnamedRoot()
    {
        byte[] unnamedStack = [0x01, 0x01, 0x01, .. CustomNameUnnamedRoot];
        byte[] wire = [0x01, 0x05, 0x00, 0x03, .. unnamedStack];
        var decoded = DecodeExact(ContainerCodecs.ContainerSetSlotV1_20_2, wire);

        AssertIsCustomName(decoded.Item);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlotV1_20_2, decoded));

        // And the two band members really are different codecs: the 756-763 member must not accept the 764 frame cleanly, nor the 764 member the 756-763 frame.
        byte[] namedWire = [0x01, 0x05, 0x00, 0x03, .. NamedStoneStack];
        Assert.Equal(namedWire.Length - 2, wire.Length);
        Assert.NotEqual(
            ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlotV1_17_1, decoded),
            ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlotV1_20_2, decoded));
    }

    // Reading the 1.16.5 frame with the unnamed-root codec under-consumes it and leaves the tag tail.
    [Fact]
    public void SetSlot_1_16_5_Frame_UnderTheOldUnnamedReader_LeavesTrailingBytes()
    {
        byte[] wire = [0x01, 0x00, 0x03, .. NamedStoneStack];

        var reader = new PacketReader(wire);
        ContainerCodecs.ContainerSetSlotV1_20_2.Decode(ref reader, ItemTestRegistries.Context);
        Assert.Equal(CustomNameNamedRoot.Length - 2, reader.Remaining);

        // The corrected member consumes the identical frame exactly.
        var fixedReader = new PacketReader(wire);
        ContainerCodecs.ContainerSetSlotV1_14.Decode(ref fixedReader, ItemTestRegistries.Context);
        Assert.Equal(0, fixedReader.Remaining);
    }
}
