using Umpk.Game.Entities;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary><c>minecraft:set_entity_data</c> across the 1.20.2-1.21.1 band (protocols 764-767).</summary>
/// <remarks>
/// <para>On protocol 764, reading the JSON custom-name string with a network-NBT decoder interprets its 21-byte length as an invalid NBT tag id and ends the connection.</para>
/// <para>Two axes move inside this band and they do not move together:</para>
/// <list type="bullet">
/// <item><b>NBT root framing</b> loses the root name at 1.20.2.</item>
/// <item><b>Chat components</b> go from GSON strings to network NBT one release later, at 1.20.3.</item>
/// </list>
/// <para>Protocol 764 is therefore a hybrid era. The serializer table changes separately: protocols through 765 have 28 entries with villager data at id 18; protocol 766 adds three entries and moves villager data to id 19 through protocol 769.</para>
/// <para>The literal frames below preserve the bytes before decoding, independently of the codecs under test. Frame-length assertions and cross-era rejections supplement the round trips.</para>
/// </remarks>
public sealed class EntityDataTypeIdTransitionTests
{
    private const string Id = "minecraft:set_entity_data";

    // Literal pre-decode frames; see the class remarks.

    /// <summary>
    /// Protocol 764 (1.20.2), containing a named zombie with disabled AI.
    /// <code>
    /// e9 02                          entity id 361 02 06 01                       field 2 (custom name), serializer 6 OPTIONAL_COMPONENT, present 15                             VarInt 21: the JSON string's LENGTH, misread as an NBT tag id 7b 22 ... 7d                   {"text":"TestZombie"} 0f 00 01                       field 15 (mob flags), serializer 0 BYTE, 1 = NoAI 09 03 41 a0 00 00              field 9 (health), serializer 3 FLOAT, 20.0 ff                             terminator
    /// </code>
    /// </summary>
    private static readonly byte[] NamedZombie764 =
    [
        0xe9, 0x02,
        0x02, 0x06, 0x01, 0x15,
        0x7b, 0x22, 0x74, 0x65, 0x78, 0x74, 0x22, 0x3a, 0x22, 0x54,
        0x65, 0x73, 0x74, 0x5a, 0x6f, 0x6d, 0x62, 0x69, 0x65, 0x22, 0x7d,
        0x0f, 0x00, 0x01,
        0x09, 0x03, 0x41, 0xa0, 0x00, 0x00,
        0xff,
    ];

    /// <summary>Protocol 765 (1.20.4), the same summon on the next protocol. The custom name is network NBT here: <c>08</c> TAG_String, <c>00 0a</c> length 10, then the bare name. No JSON, no length 21.</summary>
    private static readonly byte[] NamedZombie765 =
    [
        0xfa, 0x02,
        0x02, 0x06, 0x01,
        0x08, 0x00, 0x0a, 0x54, 0x65, 0x73, 0x74, 0x5a, 0x6f, 0x6d, 0x62, 0x69, 0x65,
        0x0f, 0x00, 0x01,
        0x09, 0x03, 0x41, 0xa0, 0x00, 0x00,
        0xff,
    ];

    /// <summary>Protocol 764 (1.20.2) villager, which pins the serializer TABLE independently of the component question. Captured after a summon with <c>VillagerData:{profession:"minecraft:farmer",level:3,type:"minecraft:plains"}</c>: <c>12 12 02 05 03</c> is field 18, serializer id <b>18</b>, (type 2 plains, profession 5 farmer, level 3) - the exact values summoned. Under the V1_21_5 table id 18 is PARTICLES.</summary>
    private static readonly byte[] Villager764 =
    [
        0x9a, 0x03,
        0x0f, 0x00, 0x01,
        0x09, 0x03, 0x41, 0xa0, 0x00, 0x00,
        0x12, 0x12, 0x02, 0x05, 0x03,
        0xff,
    ];

    /// <summary>Protocol 765 (1.20.4) villager, same summon: serializer id 18 there too.</summary>
    private static readonly byte[] Villager765 =
    [
        0xfb, 0x02,
        0x0f, 0x00, 0x01,
        0x09, 0x03, 0x41, 0xa0, 0x00, 0x00,
        0x12, 0x12, 0x02, 0x05, 0x03,
        0xff,
    ];

    // Helpers

    private static BoundPacketCodec Meta(int protocol) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, Id);

    private static ClientboundSetEntityDataPacket Decode(int protocol, byte[] frame) =>
        Assert.IsType<ClientboundSetEntityDataPacket>(Meta(protocol).DecodeFrame(frame));

    private static MetadataValue Field(ClientboundSetEntityDataPacket p, int index)
    {
        foreach (EntityDataEntry e in p.Metadata.Entries)
            if (e.Index == index)
                return e.Value;

        Assert.Fail($"no metadata field at index {index}");
        return default;
    }

    private static int SerializerIdOf(ClientboundSetEntityDataPacket p, int index)
    {
        foreach (EntityDataEntry e in p.Metadata.Entries)
            if (e.Index == index)
                return e.WireSerializerId;

        Assert.Fail($"no metadata field at index {index}");
        return -1;
    }

    /// <summary>Asserts a frame does NOT survive a codec: it either faults or re-encodes differently.</summary>
    private static void AssertRejects(int protocol, byte[] frame, string because)
    {
        BoundPacketCodec bound = Meta(protocol);
        object decoded;
        try
        {
            decoded = bound.DecodeFrame(frame);
        }
        catch (Exception)
        {
            return; // faulted, which is the honest outcome
        }

        Assert.False(frame.SequenceEqual(bound.Encode(decoded)), because);
    }

    // The captured session-killing frame decodes on 764, and every value is one the summon set.

    [Fact]
    public void TheCapturedNamedZombieFrame_Decodes_On764()
    {
        ClientboundSetEntityDataPacket p = Decode(764, NamedZombie764);

        Assert.Equal(361, p.EntityId);
        Assert.Equal("TestZombie", Field(p, 2).AsOptionalComponent()!.ToPlainText());
        Assert.Equal(1, Field(p, 15).AsByte());
        Assert.Equal(20.0f, Field(p, 9).AsFloat());
        Assert.Empty(p.Metadata.RawTail);
    }

    /// <summary>
    /// Re-encoding the frame is byte-exact, which is stronger than a value fixpoint.
    /// <list type="bullet">
    /// <item>The server wrote <c>{"text":"TestZombie"}</c> (21 bytes, the 0x15 length prefix at
    /// index 5). The object form is the required encoding for this era.</item>
    /// <item>A bare JSON string is semantically equivalent but byte-inexact. The
    /// <c>ComponentJsonLiteralForm</c> boundary at 1.20.3 preserves the object form here.</item>
    /// <item>A value round trip cannot distinguish the two legal JSON spellings, so this assertion
    /// compares the complete encoded frame.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void TheCapturedNamedZombieFrame_IsAValueFixpoint_On764()
    {
        ClientboundSetEntityDataPacket first = Decode(764, NamedZombie764);
        byte[] reencoded = Meta(764).Encode(first);
        ClientboundSetEntityDataPacket second = Decode(764, reencoded);

        Assert.Equal("TestZombie", Field(second, 2).AsOptionalComponent()!.ToPlainText());
        Assert.Equal(reencoded, Meta(764).Encode(second));

        // Still the JSON era: the byte after the present-flag is a VarInt LENGTH, not an NBT tag id. The object-form JSON string has the 0x15 length prefix, not the bare-string form's 0x0c.
        Assert.Equal(0x15, reencoded[5]);

        // The whole frame must remain byte-exact.
        Assert.Equal(NamedZombie764, reencoded);
    }

    /// <summary>Protocol 764 must read the custom name as a JSON string. The network-NBT codec instead reads the 0x15 length prefix as an invalid tag id.</summary>
    [Fact]
    public void On764_ACustomNameIsAJsonString_NotNetworkNbt()
    {
        ClientboundSetEntityDataPacket p = Decode(764, NamedZombie764);

        // Serializer 6 is OPTIONAL_COMPONENT in the 28-entry table, and the value that follows the present-flag is a length-prefixed JSON string whose length byte is exactly the 21 that the network-NBT decoding would treat as a tag type.
        Assert.Equal(6, SerializerIdOf(p, 2));
        Assert.Equal(0x15, NamedZombie764[5]);
        Assert.Equal("TestZombie", Field(p, 2).AsOptionalComponent()!.ToPlainText());
    }

    [Fact]
    public void On765_TheSameCustomNameIsNetworkNbt()
    {
        ClientboundSetEntityDataPacket p = Decode(765, NamedZombie765);

        Assert.Equal(378, p.EntityId);
        Assert.Equal(6, SerializerIdOf(p, 2));
        Assert.Equal("TestZombie", Field(p, 2).AsOptionalComponent()!.ToPlainText());
        Assert.Equal(1, Field(p, 15).AsByte());
        Assert.Equal(20.0f, Field(p, 9).AsFloat());
        Assert.Equal(NamedZombie765, Meta(765).Encode(p));
    }

    // The serializer table, pinned by a captured villager on both 764 and 765.

    [Theory]
    [InlineData(764)]
    [InlineData(765)]
    public void VillagerData_IsSerializerId18_On764And765(int protocol)
    {
        byte[] frame = protocol == 764 ? Villager764 : Villager765;
        ClientboundSetEntityDataPacket p = Decode(protocol, frame);

        Assert.Equal(18, SerializerIdOf(p, 18));
        VillagerData vd = Field(p, 18).AsVillagerData();
        Assert.Equal(2, vd.Type);        // minecraft:plains
        Assert.Equal(5, vd.Profession);  // minecraft:farmer
        Assert.Equal(3, vd.Level);
        Assert.Equal(frame, Meta(protocol).Encode(p));
    }

    /// <summary>766/767 carry the 31-entry table, where VILLAGER_DATA moved to id 19 and id 18 became PARTICLES. A 28-entry-table villager frame does not fault there, because PARTICLES is raw-tailed and the remainder is preserved verbatim (so it still round-trips byte-exactly - which is precisely why a round-trip assertion cannot see a table misbinding). What must differ is the STRUCTURE: on the 28-entry protocols field 18 decodes as VillagerData; on the 31-entry protocols it cannot, and falls into the raw tail instead.</summary>
    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    public void TheOldTablesVillagerFrame_DoesNotDecodeStructurally_UnderThe31EntryTable(int protocol)
    {
        ClientboundSetEntityDataPacket p = Decode(protocol, Villager764);

        Assert.NotEmpty(p.Metadata.RawTail);
        Assert.DoesNotContain(p.Metadata.Entries, e => e.Index == 18);

        // The two fields that precede it are era-neutral and still decode, so this is a table divergence at id 18 exactly, not a wholesale failure to read the frame.
        Assert.Equal(1, Field(p, 15).AsByte());
        Assert.Equal(20.0f, Field(p, 9).AsFloat());
    }

    /// <summary>The 28-entry protocols decode the very same frame structurally, with an empty tail.</summary>
    [Theory]
    [InlineData(764)]
    [InlineData(765)]
    public void TheSameVillagerFrame_DecodesStructurally_UnderThe28EntryTable(int protocol)
    {
        ClientboundSetEntityDataPacket p = Decode(protocol, protocol == 764 ? Villager764 : Villager765);

        Assert.Empty(p.Metadata.RawTail);
        Assert.Contains(p.Metadata.Entries, e => e.Index == 18);
    }

    // Cross-era rejection. The two component forms must not be interchangeable.

    [Fact]
    public void TheJsonComponentFrame_IsRejected_ByEveryNeighbouringProtocol()
    {
        // 763 is the JSON era but with the pre-1.20.2 NAMED-root NBT, and 765+ are the NBT-component era. Only 764 accepts this frame byte-for-byte.
        AssertRejects(765, NamedZombie764, "765 reads components as NBT and must not accept the JSON form");
        AssertRejects(766, NamedZombie764, "766 reads components as NBT and must not accept the JSON form");
        AssertRejects(767, NamedZombie764, "767 reads components as NBT and must not accept the JSON form");
    }

    [Fact]
    public void TheNbtComponentFrame_IsRejected_By764() =>
        AssertRejects(764, NamedZombie765, "764 reads components as JSON and must not accept the NBT form");

    // Frame LENGTH. A rebinding to a neighbouring era moves these even where the bytes happen to
    //    be mutually acceptable. Built from a metadata list this codebase can construct on any era.

    /// <summary>A STYLED component, deliberately. An UNstyled literal is useless as a length pin here, and that near-miss is worth recording: <c>Component.Text("TestZombie")</c> encodes to the bare JSON string <c>"TestZombie"</c> (1 VarInt length + 12) and to NBT TAG_String (1 type + 2 length + 10), which are both exactly 13 bytes. The two eras would come out to the SAME frame length and the pin would silently prove nothing. One style field forces the JSON object form and separates them.</summary>
    private static ClientboundSetEntityDataPacket CanonicalStyled() =>
        new(1, new EntityMetadataList(
            [
                new EntityDataEntry(
                    2,
                    MetadataValue.OptionalComponent(
                        new Umpk.Text.Component(
                            new Umpk.Text.TextContent("TestZombie"),
                            new Umpk.Text.Style { Bold = true })),
                    6),
            ],
            []));

    /// <summary>
    /// Both counts are DERIVED from the wire forms below and then confirmed by the run, not read off a failing assertion. Envelope common to every era: 1 entity id + 1 field index + 1 serializer id + 1 present flag + 1 terminator = 5 bytes.
    /// <list type="bullet">
    /// <item>JSON era: <c>{"text":"TestZombie","bold":true}</c> is 33 characters, plus a 1-byte VarInt
    /// length = 34. Frame = 39.</item>
    /// <item>NBT era: TAG_Compound(1) + <c>text</c> as TAG_String (1 type + 2 name length + 4 name +
    /// 2 value length + 10 value = 19) + <c>bold</c> as TAG_Byte (1 type + 2 name length + 4 name + 1 value = 8) + TAG_End(1) = 29. Frame = 34.</item>
    /// </list>
    /// A rebinding of any protocol in this band to a neighbouring era moves this.
    /// </summary>
    [Theory]
    [InlineData(763, 39)]  // named-root NBT era, JSON components
    [InlineData(764, 39)]  // unnamed-root NBT era, JSON components
    [InlineData(765, 34)]  // NBT components
    [InlineData(766, 34)]
    [InlineData(767, 34)]
    [InlineData(768, 34)]
    public void CanonicalStyledName_HasTheWireLayoutsFrameLength(int protocol, int expectedLength) =>
        Assert.Equal(expectedLength, Meta(protocol).Encode(CanonicalStyled()).Length);

    // The whole band is bound, and no protocol in it is a marker.

    [Theory]
    [InlineData(763)]
    [InlineData(764)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    public void SetEntityData_IsBound_AcrossTheBand(int protocol) =>
        Assert.True(Meta(protocol).IsImplemented);
}
