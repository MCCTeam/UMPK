using System.Buffers;
using Umpk.Game.Entities;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>The component INTERACTION dialect carried by <c>set_entity_data</c> metadata, per protocol.</summary>
/// <remarks>
/// <para>Protocols 765-769 encode component metadata with the legacy component dialect. Protocol 770 and later use the modern dialect for both component and optional-component metadata.</para>
/// <para>The 1.21.4 form names the style fields <c>clickEvent</c> and <c>hoverEvent</c>. A click event stores its value in <c>value</c>, and the entity hover event nests its payload under <c>contents</c> as <c>type</c> (the entity type) plus <c>id</c> (the UUID). The 1.21.5 form renames the style fields to <c>click_event</c> and <c>hover_event</c>, dispatches the click event on <c>action</c> with a per-action value field, and inlines the show-entity payload beside <c>action</c> as <c>id</c> (the entity type) plus <c>uuid</c>.</para>
/// <para>This is a MIS-DECODE with a narrow blast radius plus a broad re-encode fidelity loss, and the two are separated deliberately below. Framing is safe either way, because a network-NBT component is self-describing, so no frame desynchronizes. What is actually lost on decode is the hovered entity's UUID: the modern reader looks for <c>uuid</c> and the 765-769 wire spells it <c>id</c>, so the value silently became <c>Guid.Empty</c>. On encode the whole interaction goes out under field names a 765-769 client does not know, so a click or hover written by UMPK-as-server or replayed through the proxy was dropped by the receiver.</para>
/// <para>A round trip cannot see any of this: encode and decode through the same wrong era agree with each other. The two assertions that can are a frame-LENGTH pin and a cross-era rejection, and both are below. The codec-identity fixture cannot see it either - the members kept their names and the all-zero probe never reaches a component - so this file is the only guard against a silent revert.</para>
/// </remarks>
public sealed class EntityDataComponentCodecTests
{
    private const string Id = "minecraft:set_entity_data";

    /// <summary>OPTIONAL_COMPONENT's serializer id, which is 6 on every table from V1_19_4 to V26_1.</summary>
    private const int OptionalComponentSerializerId = 6;

    private static readonly Guid ProbeUuid = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    /// <summary>One literal carrying both era-sensitive constructs: a click event and a show-entity hover. A bare literal would be useless here, because plain text encodes identically under both eras.</summary>
    private static Component Probe { get; } = new(
        new TextContent("e"),
        new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "u"),
            HoverEvent = new HoverShowEntity("minecraft:pig", ProbeUuid, null),
        });

    /// <summary>
    /// The 765-769 frame, hand-built from the field names 1.21.4 <c>Style</c> / entity-hover tooltip uses, NOT from this library's encoder.
    /// <code>
    /// 01                      entity id 1 02                      field index 2 06                      serializer 6, OPTIONAL_COMPONENT 01                      present 0A                      TAG_Compound, unnamed root
    ///   08 0004 text     0001 e
    ///   0A 000A clickEvent
    ///     08 0006 action 0008 open_url
    ///     08 0005 value  0001 u
    ///     00
    ///   0A 000A hoverEvent
    ///     08 0006 action 000B show_entity
    ///     0A 0008 contents
    ///       08 0004 type 000D minecraft:pig
    ///       0B 0002 id   00000004 6ba7b810 9dad11d1 80b400c0 4fd430c8
    ///       00
    ///     00
    ///   00
    /// FF                      metadata terminator
    /// </code>
    /// </summary>
    private const string LegacyFrameHex =
        "010206010A080004746578740001650A000A636C69636B4576656E74080006616374696F6E00086F70656E5F75726C08" +
        "000576616C7565000175000A000A686F7665724576656E74080006616374696F6E000B73686F775F656E746974790A00" +
        "08636F6E74656E747308000474797065000D6D696E6563726166743A7069670B00026964000000046BA7B8109DAD11D1" +
        "80B400C04FD430C8000000FF";

    /// <summary>The 770+ frame, same component, spelled the way 1.21.5 <c>Style</c> and the corresponding entity hover event spells them: <c>click_event</c> carrying <c>url</c>, and <c>hover_event</c> carrying an inline <c>id</c> (entity type) and <c>uuid</c>.</summary>
    private const string ModernFrameHex =
        "010206010A080004746578740001650A000B636C69636B5F6576656E74080006616374696F6E00086F70656E5F75726C" +
        "08000375726C000175000A000B686F7665725F6576656E74080006616374696F6E000B73686F775F656E746974790800" +
        "026964000D6D696E6563726166743A7069670B000475756964000000046BA7B8109DAD11D180B400C04FD430C80000FF";

    // Frame LENGTH, which a round trip cannot establish.

    /// <summary>
    /// The encoded width of the same component on each protocol, derived from the vanilla field names rather than read off a failing assertion. Envelope common to every protocol here: entity id (1) + field index (1) + serializer id (1) + present flag (1) + terminator (1) = 5.
    /// <list type="bullet">
    /// <item>Legacy NBT body 151 = root TAG_Compound (1) + <c>text</c> (10) + <c>clickEvent</c> header
    /// (13) + <c>action</c> (19) + <c>value</c> (11) + end (1) + <c>hoverEvent</c> header (13) + <c>action</c> (22) + <c>contents</c> header (11) + <c>type</c> (22) + <c>id</c> int-array (25) + end (1) + end (1) + end (1). Frame 156.</item>
    /// <item>Modern NBT body 139: <c>click_event</c> is one byte longer as a name but its value key is
    /// <c>url</c> rather than <c>value</c> (-2), and the show-entity payload loses the whole <c>contents</c> wrapper and the <c>type</c> key while gaining <c>uuid</c>. Frame 144.</item>
    /// </list>
    /// A rebinding of any protocol in this band to the neighbouring era moves this by 12 bytes.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="expectedLength">The frame width for the probe component.</param>
    [Theory]
    [InlineData(765, 156)]
    [InlineData(766, 156)]
    [InlineData(767, 156)]
    [InlineData(768, 156)]
    [InlineData(769, 156)]
    [InlineData(770, 144)]
    [InlineData(771, 144)]
    [InlineData(772, 144)]
    [InlineData(773, 144)]
    [InlineData(774, 144)]
    [InlineData(775, 144)]
    [InlineData(776, 144)]
    public void ProbeComponent_HasTheWireLayoutsFrameLength(int protocol, int expectedLength) =>
        Assert.Equal(expectedLength, Encode(protocol, ProbePacket()).Length);

    /// <summary>And the bytes themselves, against the hand-built vanilla-spelled frames. This is the pin that says WHICH era, not merely that two eras differ.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="expectedHex">The era's frame.</param>
    [Theory]
    [InlineData(765, LegacyFrameHex)]
    [InlineData(767, LegacyFrameHex)]
    [InlineData(769, LegacyFrameHex)]
    [InlineData(770, ModernFrameHex)]
    [InlineData(774, ModernFrameHex)]
    [InlineData(776, ModernFrameHex)]
    public void ProbeComponent_EncodesToTheWireLayoutsVanillaSpelling(int protocol, string expectedHex) =>
        Assert.Equal(Convert.FromHexString(expectedHex), Encode(protocol, ProbePacket()));

    // Cross-era rejection. The neighbouring era's frame must not survive.

    /// <summary>Each protocol recovers BOTH halves of the hovered entity from its own era's frame. The UUID is the load-bearing half: it is the one value the wrong era silently dropped, because legacy spells it <c>id</c> and modern spells it <c>uuid</c>.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="frameHex">That protocol's frame.</param>
    [Theory]
    [InlineData(765, LegacyFrameHex)]
    [InlineData(766, LegacyFrameHex)]
    [InlineData(767, LegacyFrameHex)]
    [InlineData(768, LegacyFrameHex)]
    [InlineData(769, LegacyFrameHex)]
    [InlineData(770, ModernFrameHex)]
    [InlineData(773, ModernFrameHex)]
    [InlineData(776, ModernFrameHex)]
    public void OwnWireLayoutFrame_RecoversTheHoveredEntityTypeAndUuid(int protocol, string frameHex)
    {
        HoverShowEntity hover = HoverOf(Decode(protocol, Convert.FromHexString(frameHex)));

        Assert.Equal("minecraft:pig", hover.EntityType);
        Assert.Equal(ProbeUuid, hover.Id);
        Assert.Equal(ClickEventAction.OpenUrl, ClickOf(Decode(protocol, Convert.FromHexString(frameHex))).Action);
    }

    /// <summary>The rejection. A network-NBT component is self-describing, so the neighbouring era's frame does not FAULT; it decodes to something wrong and quietly. That is exactly why this has to be asserted as a value divergence rather than as a throw, and exactly why a round trip is blind to it: the wrong era agrees with itself.</summary>
    /// <param name="protocol">The protocol whose codec is fed the other era's frame.</param>
    /// <param name="foreignFrameHex">The other era's frame.</param>
    [Theory]
    [InlineData(769, ModernFrameHex)]
    [InlineData(770, LegacyFrameHex)]
    public void ForeignWireLayoutFrame_LosesTheHoveredEntity(int protocol, string foreignFrameHex)
    {
        HoverShowEntity hover = HoverOf(Decode(protocol, Convert.FromHexString(foreignFrameHex)));

        // The UUID is gone under the wrong era's key, and the entity type falls back to the model's default because neither `type` nor `id` is where that era expects it.
        Assert.NotEqual(ProbeUuid, hover.Id);
        Assert.Equal(Guid.Empty, hover.Id);
    }

    /// <summary>And the re-encode makes the same statement in bytes: feeding a protocol the other era's frame and letting it write the result back does not reproduce the input. This is the fidelity half, and it is the half a proxy or a UMPK-hosted server puts on the wire.</summary>
    /// <param name="protocol">The protocol whose codec is fed the other era's frame.</param>
    /// <param name="foreignFrameHex">The other era's frame.</param>
    [Theory]
    [InlineData(769, ModernFrameHex)]
    [InlineData(770, LegacyFrameHex)]
    public void ForeignWireLayoutFrame_DoesNotReEncodeToItself(int protocol, string foreignFrameHex)
    {
        byte[] foreign = Convert.FromHexString(foreignFrameHex);
        Assert.NotEqual(foreign, Encode(protocol, Decode(protocol, foreign)));
    }

    // The dialect on the wire agrees with the dataset, per protocol.

    /// <summary>The style field spelling IS the era, verbatim, so reading it out of the frame is a direct statement about the bound codec rather than an inference. The expected value per protocol is stated here rather than derived from the codec under test; the same boundary is stated independently by the dataset and pinned by <c>Umpk.Data.Java.Tests.Generated.ComponentBindingAgreementTests.DeclaredEraBoundaryIs770</c>, so dataset and binding edits cannot drift apart silently.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="expected">The interaction dialect that protocol must put on the wire.</param>
    [Theory]
    [InlineData(765, ComponentWireEra.Legacy)]
    [InlineData(766, ComponentWireEra.Legacy)]
    [InlineData(767, ComponentWireEra.Legacy)]
    [InlineData(768, ComponentWireEra.Legacy)]
    [InlineData(769, ComponentWireEra.Legacy)]
    [InlineData(770, ComponentWireEra.Modern)]
    [InlineData(771, ComponentWireEra.Modern)]
    [InlineData(772, ComponentWireEra.Modern)]
    [InlineData(773, ComponentWireEra.Modern)]
    [InlineData(774, ComponentWireEra.Modern)]
    [InlineData(775, ComponentWireEra.Modern)]
    [InlineData(776, ComponentWireEra.Modern)]
    public void BoundCodec_SpellsTheStyleFieldsItsWireLayoutsWay(int protocol, ComponentWireEra expected)
    {
        byte[] frame = Encode(protocol, ProbePacket());
        bool modernOnWire = Contains(frame, "click_event");
        bool legacyOnWire = Contains(frame, "clickEvent");

        Assert.True(
            modernOnWire ^ legacyOnWire,
            $"protocol {protocol}: the frame must carry exactly one of the two style spellings.");

        Assert.Equal(expected, modernOnWire ? ComponentWireEra.Modern : ComponentWireEra.Legacy);
    }

    // Helpers.

    private static ClientboundSetEntityDataPacket ProbePacket() =>
        new(1, new EntityMetadataList(
            [new EntityDataEntry(2, MetadataValue.OptionalComponent(Probe), OptionalComponentSerializerId)],
            []));

    private static ClientboundSetEntityDataPacket Decode(int protocol, byte[] frame) =>
        (ClientboundSetEntityDataPacket)BoundCodec.At(protocol, PacketFlow.Clientbound, Id)
            .Decode(frame, PacketCodecContext.Registryless);

    private static byte[] Encode(int protocol, ClientboundSetEntityDataPacket packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        BoundCodec.At(protocol, PacketFlow.Clientbound, Id)
            .Encode(ref writer, packet, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }

    private static Component ComponentOf(ClientboundSetEntityDataPacket packet) =>
        packet.Metadata.Entries.Single(e => e.Index == 2).Value.AsOptionalComponent()!;

    private static HoverShowEntity HoverOf(ClientboundSetEntityDataPacket packet) =>
        Assert.IsType<HoverShowEntity>(ComponentOf(packet).Style.HoverEvent);

    private static ClickEvent ClickOf(ClientboundSetEntityDataPacket packet) =>
        ComponentOf(packet).Style.ClickEvent!;

    private static bool Contains(byte[] frame, string marker) =>
        frame.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(marker).AsSpan()) >= 0;
}
