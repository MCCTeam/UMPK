using System.Buffers;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>
/// Byte-exact era pins for the INBOUND signed <c>minecraft:player_chat</c> across the modern band, resolved through the binding table rather than by naming a codec, so a misbound or unbound era fails here.
/// <para>Every expected frame below is hand-built field by field from the wire contract, never by running the encoder. Every value is non-empty and distinct so a dropped or reordered field changes the bytes. The 1.20.4 form contains UUID, index, nullable signature, packed signed body, nullable unsigned content, filter mask, and bound chat type. The 1.21.5 form adds a leading <c>globalIndex</c> VarInt; 26.2 retains that layout.</para>
/// </summary>
public sealed class PlayerChatCodecTests
{
    private const string Identifier = "minecraft:player_chat";

    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");

    /// <summary>Every protocol in the modern inbound player-chat band.</summary>
    public static TheoryData<int> ModernBand =>
        [764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776];

    private static byte[] Sig(byte fill)
    {
        var s = new byte[256];
        Array.Fill(s, fill);
        return s;
    }

    /// <summary>The registrar must bind a real codec, not a marker, on every protocol from 1.20.2 to 26.2. <see cref="BoundCodec.At"/> asserts <c>IsImplemented</c>, so a marker fails here directly.</summary>
    [Theory]
    [MemberData(nameof(ModernBand))]
    public void PlayerChat_IsImplemented_AcrossTheModernBand(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, Identifier);
        Assert.True(bound.IsImplemented);
    }

    // 764 (1.20.2): unchanged from 761-763. JSON string components, no globalIndex.

    /// <summary>1.20.2 kept the 1.19.3 body verbatim; only the component encoding era matters, and components stay JSON strings through 764 (network NBT arrives at 765, exactly as system_chat and disguised_chat already model in this repo).</summary>
    [Fact]
    public void PlayerChat_764_JsonWireLayout_DecodesFrameExact_AndReEncodes()
    {
        byte[] frame = BuildFrame(hasGlobalIndex: false, WriteJsonComponent);

        BoundPacketCodec bound = BoundCodec.At(764, PacketFlow.Clientbound, Identifier);
        var decoded = (ClientboundPlayerChatPacket)bound.DecodeFrame(frame);

        AssertCommonFields(decoded, expectedGlobalIndex: 0);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    // 765-769 (1.20.3 - 1.21.4): network NBT components, legacy interactions, no globalIndex.

    /// <summary>765 moved every component on this packet to network NBT. At 766 the bound chat type changed to a holder, but a registry-reference holder is the same single VarInt on the wire, so the byte layout is unchanged across 765-769 and one codec covers the span.</summary>
    [Theory]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(769)]
    public void PlayerChat_765_To_769_NbtWireLayout_DecodesFrameExact_AndReEncodes(int protocol)
    {
        byte[] frame = BuildFrame(hasGlobalIndex: false, WriteNbtLegacyComponent);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, Identifier);
        var decoded = (ClientboundPlayerChatPacket)bound.DecodeFrame(frame);

        AssertCommonFields(decoded, expectedGlobalIndex: 0);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    // 770-776 (1.21.5 - 26.2): leading globalIndex VarInt, modern-interaction NBT components.

    /// <summary>1.21.5 prefixed the packet with a <c>globalIndex</c> VarInt and moved components to the modern interaction era. The layout remains byte-identical through 26.2, so one codec covers 770-776. A codec bound one era low would read the globalIndex as the first UUID byte and desync the whole frame.</summary>
    [Theory]
    [InlineData(770)]
    [InlineData(773)]
    [InlineData(775)]
    [InlineData(776)]
    public void PlayerChat_770_To_776_ModernWireLayout_DecodesFrameExact_AndReEncodes(int protocol)
    {
        byte[] frame = BuildFrame(hasGlobalIndex: true, WriteModernComponent);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, Identifier);
        var decoded = (ClientboundPlayerChatPacket)bound.DecodeFrame(frame);

        AssertCommonFields(decoded, expectedGlobalIndex: 42);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    /// <summary>The 770+ frame must NOT decode under the 765-769 era: the leading globalIndex shifts every following field. This pins the era boundary itself, which a per-codec test cannot see.</summary>
    [Fact]
    public void PlayerChat_ModernFrame_IsNotFrameExact_UnderThePreviousWireLayout()
    {
        byte[] modernFrame = BuildFrame(hasGlobalIndex: true, WriteModernComponent);
        BoundPacketCodec previousEra = BoundCodec.At(769, PacketFlow.Clientbound, Identifier);

        Assert.ThrowsAny<Exception>(() => previousEra.DecodeFrame(modernFrame));
    }

    /// <summary>A partially-filtered mask (type 2) is followed by a bitset encoded as a VarInt-length long array. Reading the type and stopping leaves those longs in the buffer and desyncs every following field, so the mask words must survive a round-trip.</summary>
    [Fact]
    public void PlayerChat_PartiallyFilteredMask_RoundTripsFrameExact()
    {
        byte[] frame = BuildFrame(hasGlobalIndex: true, WriteModernComponent, filterType: 2, filterWords: [0x0000_0000_0000_0A5AL]);

        BoundPacketCodec bound = BoundCodec.At(776, PacketFlow.Clientbound, Identifier);
        var decoded = (ClientboundPlayerChatPacket)bound.DecodeFrame(frame);

        Assert.Equal(2, decoded.FilterType);
        Assert.Equal([0x0000_0000_0000_0A5AL], decoded.FilterBits);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    // Frame construction follows the wire order field by field.

    private delegate void ComponentWriter(ref PacketWriter w, Component c);

    // In protocols 47-764, serialization starts from an object, so a literal is {"text":"..."} and never a bare string. Passing the literal form explicitly is what keeps this frame a VANILLA frame rather than a copy of whatever the encoder under test happens to produce.
    private static void WriteJsonComponent(ref PacketWriter w, Component c) =>
        w.WriteString(ComponentJson.ToJsonString(c, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object), 262144);

    private static void WriteNbtLegacyComponent(ref PacketWriter w, Component c) =>
        w.WriteComponent(c, ComponentWireEra.Legacy, NbtWireFormat.JavaRootTagOrString);

    private static void WriteModernComponent(ref PacketWriter w, Component c) =>
        w.WriteComponent(c, ComponentWireEra.Modern, NbtWireFormat.JavaRootTagOrString);

    private static byte[] BuildFrame(
        bool hasGlobalIndex,
        ComponentWriter component,
        int filterType = 0,
        long[]? filterWords = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);

        // 1.21.5+: int globalIndex.
        if (hasGlobalIndex)
            w.WriteVarInt(42);

        // UUID sender; VarInt index; nullable MessageSignature (fixed 256 bytes).
        w.WriteUuid(Sender);
        w.WriteVarInt(7);
        w.WriteBool(true);
        w.WriteBytes(Sig(0x5C));

        // Packed body: a string capped at 256 characters, an epoch-millis long, a salt long, and a collection of packed signatures.
        w.WriteString("hello from the modern band", 256);
        w.WriteLong(1_700_000_000_000L);
        w.WriteLong(0x0123_4567_89AB_CDEFL);
        w.WriteVarInt(2);
        // Packed signature: VarInt id+1; 0 means a full 256-byte signature follows.
        w.WriteVarInt(0);
        w.WriteBytes(Sig(0x77));
        w.WriteVarInt(4); // a cached id (3), no bytes

        // Nullable unsignedContent.
        w.WriteBool(true);
        component(ref w, Component.Text("hello from the modern band (server override)"));

        // FilterMask: varint enum type, plus a BitSet (varint-length long array) when PARTIALLY_FILTERED.
        w.WriteVarInt(filterType);
        if (filterType == 2)
        {
            long[] words = filterWords ?? [];
            w.WriteVarInt(words.Length);
            foreach (long word in words)
                w.WriteLong(word);

        }

        // ChatType bound: varint chat type (a Holder registry reference from 766), name component, optional target component.
        w.WriteVarInt(3);
        component(ref w, Component.Text("Notch"));
        w.WriteBool(true);
        component(ref w, Component.Text("Steve"));

        return buffer.WrittenSpan.ToArray();
    }

    private static void AssertCommonFields(ClientboundPlayerChatPacket p, int expectedGlobalIndex)
    {
        Assert.Equal(expectedGlobalIndex, p.GlobalIndex);
        Assert.Equal(Sender, p.Sender);
        Assert.Equal(7, p.Index);
        Assert.Equal(256, p.Signature!.Length);
        Assert.Equal(0x5C, p.Signature[0]);
        Assert.Equal("hello from the modern band", p.SignedContent);
        Assert.Equal(1_700_000_000_000L, p.TimestampMillis);
        Assert.Equal(0x0123_4567_89AB_CDEFL, p.Salt);
        // The frame carries one full inline signature and one signature-cache reference (id 3).
        Assert.Equal(2, p.LastSeen.Count);
        Assert.Equal(PackedMessageSignature.FullSignatureId, p.LastSeen[0].Id);
        Assert.Equal(256, p.LastSeen[0].FullSignature!.Length);
        Assert.Equal(0x77, p.LastSeen[0].FullSignature![0]);
        Assert.Equal(3, p.LastSeen[1].Id);
        Assert.Null(p.LastSeen[1].FullSignature);
        Assert.Equal("hello from the modern band (server override)", p.UnsignedContent!.ToPlainText());
        Assert.Equal(0, p.FilterType);
        Assert.Equal(3, p.ChatTypeId);
        Assert.Equal("Notch", p.SenderName.ToPlainText());
        Assert.Equal("Steve", p.TargetName!.ToPlainText());
    }
}
