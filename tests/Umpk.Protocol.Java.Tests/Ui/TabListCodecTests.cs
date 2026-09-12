using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>
/// Era tests for <c>minecraft:tab_list</c> (the player-list header/footer), which carries two chat components whose encoding changes across the supported range.
/// <para>Protocols 107-764 read two JSON components. Protocol 765 reads them as network NBT. Thus, the JSON band is 107-764 and the NBT band starts at 765 (1.20.3).</para>
/// <para>Protocols 768 and 769 still use flat click and hover shapes despite carrying network-NBT components. The values below are non-empty and carry a click event because a plain component can decode identically under either interaction framing.</para>
/// </summary>
public class TabListCodecTests
{
    // A header/footer pair rich enough that the framing and the interaction era are both load-bearing.
    private static readonly Component Header = new(
        new TextContent("Welcome"),
        Style.Empty with { ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://example.invalid") });

    private static readonly Component Footer = Component.Text("42 online");

    private static BoundPacketCodec Bind(string codecKey)
    {
        var version = new GameVersion(GameEdition.Java, "test", CodecKeyProtocols.Of(codecKey));
        var builder = new ProtocolDescriptorBuilder(version, new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, 0x01, "minecraft:tab_list");
        ProtocolDescriptor descriptor = builder.Build();
        Assert.True(descriptor.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).TryGetInbound(0x01, out BoundPacketCodec entry));
        return entry;
    }

    private static byte[] Encode(BoundPacketCodec entry, ClientboundTabListPacket packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        entry.Encode(ref writer, packet, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }

    // Every era key in the JSON band must resolve to an implemented codec that produces the exact JSON-string frame.
    [Theory]
    [InlineData("V1_9")]
    [InlineData("V1_9_2")]
    [InlineData("V1_9_4")]
    [InlineData("V1_12")]
    [InlineData("V1_12_2")]
    [InlineData("V1_13")]
    [InlineData("V1_13_2")]
    [InlineData("V1_14")]
    [InlineData("V1_14_4")]
    [InlineData("V1_15")]
    [InlineData("V1_16")]
    [InlineData("V1_16_2")]
    [InlineData("V1_17")]
    [InlineData("V1_17_1")]
    [InlineData("V1_18")]
    [InlineData("V1_19")]
    [InlineData("V1_19_1")]
    [InlineData("V1_19_3")]
    [InlineData("V1_19_4")]
    [InlineData("V1_20")]
    [InlineData("V1_20_2")]
    public void JsonBand_BindsImplementedJsonCodec(string codecKey)
    {
        BoundPacketCodec entry = Bind(codecKey);
        Assert.True(entry.IsImplemented, $"minecraft:tab_list must not be a marker on {codecKey}.");

        var packet = new ClientboundTabListPacket(Header, Footer);
        byte[] actual = Encode(entry, packet);

        Assert.Equal(CodecRoundTrip.Encode(PlayerListCodecs.TabListV1_9, packet), actual);

        var decoded = (ClientboundTabListPacket)entry.Decode(actual, PacketCodecContext.Registryless);
        Assert.Equal("Welcome", decoded.Header.ToPlainText());
        Assert.Equal("42 online", decoded.Footer.ToPlainText());
        Assert.Equal(ClickEventAction.OpenUrl, decoded.Header.Style.ClickEvent!.Action);
        Assert.Equal("https://example.invalid", decoded.Header.Style.ClickEvent!.Value);
    }

    // 1.20.3 moved components to network NBT without moving the interaction era. The NBT band that still writes flat click/hover shapes therefore runs 765-769, including V1_21_2 and V1_21_4.
    [Theory]
    [InlineData("V1_20_3")]
    [InlineData("V1_20_5")]
    [InlineData("V1_21")]
    [InlineData("V1_21_2")]
    [InlineData("V1_21_4")]
    public void NbtLegacyBand_BindsImplementedNbtCodec(string codecKey)
    {
        BoundPacketCodec entry = Bind(codecKey);
        Assert.True(entry.IsImplemented);

        var packet = new ClientboundTabListPacket(Header, Footer);
        Assert.Equal(CodecRoundTrip.Encode(PlayerListCodecs.TabListV1_20_3, packet), Encode(entry, packet));
    }

    // 1.21.5 is where ClickEvent/HoverEvent become interfaces dispatched on "action" and Style renames the fields to click_event/hover_event, so 770 is the first modern-era protocol.
    [Theory]
    [InlineData("V1_21_5")]
    [InlineData("V1_21_6")]
    [InlineData("V26_1")]
    [InlineData("V26_2")]
    public void ModernBand_BindsImplementedModernCodec(string codecKey)
    {
        BoundPacketCodec entry = Bind(codecKey);
        Assert.True(entry.IsImplemented);

        var packet = new ClientboundTabListPacket(Header, Footer);
        Assert.Equal(CodecRoundTrip.Encode(PlayerListCodecs.TabListV1_21_5, packet), Encode(entry, packet));
    }

    // The literal frame: two VarInt-length-prefixed JSON strings and nothing else, so the byte after the first string's payload is the second string's length. A pure literal takes the OBJECT form on this band, which is what vanilla writes: a literal is spelled as {"text":"..."}. The bare string is a READ form accepted by that era, which only becomes the WRITE form at 1.20.3. This pin carries the spelling, which was UMPK's output rather than the server's.
    [Fact]
    public void JsonWireLayout_RawByteFrame()
    {
        var packet = new ClientboundTabListPacket(Component.Text("Hi"), Component.Text("Bye"));

        byte[] expected =
        [
            0x0D, .. "{\"text\":\"Hi\"}"u8,
            0x0E, .. "{\"text\":\"Bye\"}"u8,
        ];

        Assert.Equal(expected, CodecRoundTrip.Encode(PlayerListCodecs.TabListV1_9, packet));

        var decoded = CodecRoundTrip.Decode(PlayerListCodecs.TabListV1_9, expected);
        Assert.Equal("Hi", decoded.Header.ToPlainText());
        Assert.Equal("Bye", decoded.Footer.ToPlainText());
    }

    // A styled component takes the object form, and the click event rides the legacy flat {"action","value"} shape the whole 107-764 band uses. This is the byte-level proof that the band is JSON and legacy-interaction, not NBT and not the 1.21.5 dispatched click shape.
    //
    // The header spelling moved once more, from content-first to STYLE-first, and the new one is again vanilla's rather than this library's: style is serialized before any content key. A literal "h" with an insertion and a run_command click prints {"insertion":"zz","clickEvent":{"action":"run_command","value":"/say hi"},...,"text":"h"} - the click event ahead of the text. See ComponentJsonVanillaShapeTests for the matching shape tests.
    [Fact]
    public void JsonWireLayout_StyledHeader_RawByteFrame()
    {
        var packet = new ClientboundTabListPacket(
            new Component(
                new TextContent("Hi"),
                Style.Empty with { ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://e.invalid") }),
            Component.Text("Bye"));

        const string HeaderJson =
            "{\"clickEvent\":{\"action\":\"open_url\",\"value\":\"https://e.invalid\"},\"text\":\"Hi\"}";

        byte[] expected =
        [
            (byte)HeaderJson.Length, .. System.Text.Encoding.UTF8.GetBytes(HeaderJson),
            0x0E, .. "{\"text\":\"Bye\"}"u8,
        ];

        Assert.Equal(expected, CodecRoundTrip.Encode(PlayerListCodecs.TabListV1_9, packet));
        Assert.Equal(packet, CodecRoundTrip.Decode(PlayerListCodecs.TabListV1_9, expected));
    }

    // The sensitivity guard for the era split. An NBT frame and a JSON frame for the SAME components must not be interchangeable: the JSON codec must not silently accept NBT bytes, and the two encodings must differ. Non-empty values are essential here; an empty component pair can encode to byte sequences that both codecs happen to tolerate.
    [Fact]
    public void JsonAndNbtFrames_AreNotInterchangeable()
    {
        var packet = new ClientboundTabListPacket(Header, Footer);

        byte[] json = CodecRoundTrip.Encode(PlayerListCodecs.TabListV1_9, packet);
        byte[] nbt = CodecRoundTrip.Encode(PlayerListCodecs.TabListV1_20_3, packet);
        Assert.NotEqual(json, nbt);

        AssertNotDecodable(PlayerListCodecs.TabListV1_9, nbt);
        AssertNotDecodable(PlayerListCodecs.TabListV1_20_3, json);
    }

    // A frame written in the wrong era must either throw or leave bytes unread; it must never round-trip as an equivalent encoding.
    private static void AssertNotDecodable(PacketCodec<ClientboundTabListPacket> codec, byte[] frame)
    {
        var reader = new PacketReader(frame);
        try
        {
            codec.Decode(ref reader, PacketCodecContext.Registryless);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            return;
        }

        Assert.NotEqual(0, reader.Remaining);
    }
}
