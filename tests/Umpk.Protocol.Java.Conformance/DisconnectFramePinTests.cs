using System.Buffers;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>
/// Byte-exact pins for the play-phase <c>disconnect</c> payload, resolved through the version catalog rather than a named codec.
/// <para>The pinned reason is deliberately NOT plain text. A bare string encodes identically under the legacy and modern interaction eras, so a pin built from one would pass under either binding. Carrying a click event and a hover event makes the two eras produce different bytes, which is what makes the 770 boundary observable at all. The 765 transport boundary is visible in the first byte: a JSON string is a VarInt length followed by ASCII, network NBT starts with the TAG_Compound id 0x0A.</para>
/// </summary>
public sealed class DisconnectFramePinTests
{
    private static Component Reason { get; } = new(
        new TextContent("kicked"),
        new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://e.invalid/a"),
            HoverEvent = new HoverShowText(Component.Text("why")),
        });

    /// <summary>
    /// 47-764: a VarInt-prefixed JSON string, legacy interaction keys. 0x9001 is VarInt 144, then <c>{"clickEvent":{"action":"open_url","value":"https://e.invalid/a"},</c> <c>"hoverEvent":{"action":"show_text","contents":{"text":"why"}},"text":"kicked"}</c>.
    /// <para>The <c>show_text</c> payload uses an object with a <c>text</c> field. It does not use a bare string. This shape distinguishes the legacy JSON era from later component encodings.</para>
    /// <para>Style keys precede the content key: <c>clickEvent</c> and <c>hoverEvent</c> occur before <c>text</c>. The encoded length remains 144 bytes, so the exact byte comparison also checks key order.</para>
    /// </summary>
    private const string JsonEraHex =
        "90017B22636C69636B4576656E74223A7B22616374696F6E223A226F70656E5F75726C222C2276616C7565223A226874" +
        "7470733A2F2F652E696E76616C69642F61227D2C22686F7665724576656E74223A7B22616374696F6E223A2273686F77" +
        "5F74657874222C22636F6E74656E7473223A7B2274657874223A22776879227D7D2C2274657874223A226B69636B6564" +
        "227D";

    /// <summary>765-769: network NBT (unnamed root TAG_Compound 0x0A), legacy interaction keys. The compound names are <c>clickEvent</c> with a <c>value</c> string and <c>hoverEvent</c> with a <c>contents</c> string.</summary>
    private const string NbtLegacyEraHex =
        "0A0800047465787400066B69636B65640A000A636C69636B4576656E74080006616374696F6E00086F70656E5F75726C" +
        "08000576616C7565001368747470733A2F2F652E696E76616C69642F61000A000A686F7665724576656E740800066163" +
        "74696F6E000973686F775F74657874080008636F6E74656E747300037768790000";

    /// <summary>770+: the same network NBT transport with modern interaction keys, <c>click_event</c> carrying <c>url</c> and <c>hover_event</c> carrying <c>value</c>.</summary>
    private const string NbtModernEraHex =
        "0A0800047465787400066B69636B65640A000B636C69636B5F6576656E74080006616374696F6E00086F70656E5F7572" +
        "6C08000375726C001368747470733A2F2F652E696E76616C69642F61000A000B686F7665725F6576656E740800066163" +
        "74696F6E000973686F775F7465787408000576616C756500037768790000";

    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(578)]
    [InlineData(763)]
    [InlineData(764)]
    public void JsonWireLayout_IsPinned(int protocol) => AssertPin(protocol, JsonEraHex);

    [Theory]
    [InlineData(765)]
    [InlineData(767)]
    [InlineData(769)]
    public void NbtLegacyWireLayout_IsPinned(int protocol) => AssertPin(protocol, NbtLegacyEraHex);

    [Theory]
    [InlineData(770)]
    [InlineData(773)]
    [InlineData(776)]
    public void NbtModernWireLayout_IsPinned(int protocol) => AssertPin(protocol, NbtModernEraHex);

    /// <summary>The neighbour check that makes a boundary real: the three era encodings are pairwise different for this payload, so a pin passing on one era could not also pass on another.</summary>
    [Fact]
    public void TheThreeWireLayouts_ProduceDifferentBytes()
    {
        byte[] json = Encode(764);
        byte[] nbtLegacy = Encode(769);
        byte[] nbtModern = Encode(770);

        Assert.NotEqual(json, nbtLegacy);
        Assert.NotEqual(nbtLegacy, nbtModern);
        Assert.NotEqual(json, nbtModern);
    }

    /// <summary>The configuration and play phases bind equivalent disconnect payloads. Their encoded bytes must match on every protocol that supports both phases.</summary>
    [Theory]
    [InlineData(764)]
    [InlineData(769)]
    [InlineData(776)]
    public void ConfigurationPhase_MatchesPlayPhase(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        Assert.Equal(
            Encode(protocol),
            EncodeThrough(version!, ProtocolPhase.Configuration, new ClientboundConfigDisconnectPacket(Reason)));
    }

    private static void AssertPin(int protocol, string expectedHex)
    {
        byte[] actual = Encode(protocol);
        Assert.Equal(expectedHex, Convert.ToHexString(actual));

        // And the pinned bytes decode back to the same reason through the same bound codec.
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        PhaseRegistry registry = version!.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        BoundPacketCodec bound = Resolve(registry, Identifier.Minecraft("disconnect"), protocol);
        var decoded = Assert.IsType<ClientboundDisconnectPacket>(
            bound.Decode(actual, new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty)));
        Assert.Equal("kicked", Assert.IsType<TextContent>(decoded.Reason.Content).Text);
        Assert.NotNull(decoded.Reason.Style.ClickEvent);
        Assert.NotNull(decoded.Reason.Style.HoverEvent);
    }

    private static byte[] Encode(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return EncodeThrough(version!, ProtocolPhase.Play, new ClientboundDisconnectPacket(Reason));
    }

    private static byte[] EncodeThrough(JavaVersion version, ProtocolPhase phase, object packet)
    {
        PhaseRegistry registry = version.Protocol.GetRegistry(phase, PacketFlow.Clientbound);
        BoundPacketCodec bound = Resolve(registry, Identifier.Minecraft("disconnect"), version.Version.Protocol);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(
            ref writer,
            packet,
            new PacketCodecContext(JavaGameData.Registries(version.Version.Protocol), IConnectionCodecState.Empty));
        return buffer.WrittenSpan.ToArray();
    }

    private static BoundPacketCodec Resolve(PhaseRegistry registry, Identifier id, int protocol)
    {
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec bound));
            Assert.True(bound.IsImplemented, $"{id} is a marker at protocol {protocol}");
            return bound;
        }

        Assert.Fail($"{id} is not registered at protocol {protocol}");
        return null!;
    }
}
