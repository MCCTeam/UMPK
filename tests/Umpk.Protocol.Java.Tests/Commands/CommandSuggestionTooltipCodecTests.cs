using System.Text;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Commands;

/// <summary>The command-suggestion TOOLTIP era boundary. The suggestions frame keeps one framing from 1.13 on (transaction id, range start, range length, then a counted list of text plus optional tooltip), and exactly one field inside it changes era: the tooltip component. It is a JSON STRING from 393 through 764 and network NBT from 765 on.</summary>
/// <remarks>
/// <para>Reading a JSON tooltip with the network-NBT codec treats the string's VarInt length prefix as an NBT tag type. This raises <c>ProtocolViolationException</c>; the default <c>DecodeFailurePolicy.FailConnection</c> then closes the connection.</para>
/// <para>Protocols 1.14.4 through 1.16.5 use a presence boolean followed by the JSON string form. Protocol 1.20.4 is the first to use the nullable trusted-component form, and 1.21.5 keeps that observable framing.</para>
/// <para>Every frame here is hand-built byte by byte. A round trip cannot detect a consistently wrong codec. The cross-era rejection pair pins the boundary, and the frame-length assertion proves the two codecs are not interchangeable.</para>
/// </remarks>
public sealed class CommandSuggestionTooltipCodecTests
{
    private const string Identifier = "minecraft:command_suggestions";

    /// <summary>Every protocol whose suggestion tooltip is a JSON-string component.</summary>
    public static readonly int[] JsonBand =
    [
        393, 401, 404, 477, 480, 485, 490, 498, 573, 575, 578, 735, 736, 751, 753, 754,
        755, 756, 757, 758, 759, 760, 761, 762, 763, 764,
    ];

    /// <summary>Every protocol whose suggestion tooltip is a network-NBT component.</summary>
    public static readonly int[] NbtBand = [765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776];

    public static TheoryData<int> JsonProtocols => Spread(JsonBand);

    public static TheoryData<int> NbtProtocols => Spread(NbtBand);

    [Theory]
    [MemberData(nameof(JsonProtocols))]
    public void JsonBand_BindsTheJsonTooltipCodec(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, Identifier);
        Assert.Contains("CommandSuggestionsV1_13", bound.CodecIdentity, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NbtProtocols))]
    public void NbtBand_BindsTheNbtTooltipCodec(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, Identifier);
        Assert.Contains("CommandSuggestionsV1_20_3", bound.CodecIdentity, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(JsonProtocols))]
    public void JsonBand_DecodesAJsonTooltipFrame(int protocol)
    {
        var p = (ClientboundCommandSuggestionsPacket)BoundCodec
            .At(protocol, PacketFlow.Clientbound, Identifier)
            .DecodeFrame(JsonTooltipFrame());

        AssertSample(p);
    }

    [Theory]
    [MemberData(nameof(NbtProtocols))]
    public void NbtBand_DecodesAnNbtTooltipFrame(int protocol)
    {
        var p = (ClientboundCommandSuggestionsPacket)BoundCodec
            .At(protocol, PacketFlow.Clientbound, Identifier)
            .DecodeFrame(NbtTooltipFrame());

        AssertSample(p);
    }

    /// <summary>Cross-era rejection at the boundary itself: the last JSON protocol cannot read the NBT frame and the first NBT protocol cannot read the JSON frame. Without this pair the band tests above would still pass with the boundary one version out in either direction.</summary>
    [Theory]
    [InlineData(393)]
    [InlineData(754)]
    [InlineData(764)]
    public void JsonBand_RejectsAnNbtTooltipFrame(int protocol) =>
        Assert.ThrowsAny<Exception>(() => BoundCodec
            .At(protocol, PacketFlow.Clientbound, Identifier)
            .DecodeFrame(NbtTooltipFrame()));

    [Theory]
    [InlineData(765)]
    [InlineData(775)]
    public void NbtBand_RejectsAJsonTooltipFrame(int protocol) =>
        Assert.ThrowsAny<Exception>(() => BoundCodec
            .At(protocol, PacketFlow.Clientbound, Identifier)
            .DecodeFrame(JsonTooltipFrame()));

    /// <summary>The two codecs write DIFFERENT byte counts for the same packet, so neither can stand in for the other. A round-trip test cannot see this; only comparing the encoded frames can.</summary>
    [Fact]
    public void TheTwoWireLayouts_EncodeTheSamePacketToDifferentBytes()
    {
        var packet = new ClientboundCommandSuggestionsPacket(
            7, 6, 0, [new CommandSuggestion("@a", Component.Text("Nearest player"))]);

        byte[] json = BoundCodec.At(764, PacketFlow.Clientbound, Identifier).Encode(packet);
        byte[] nbt = BoundCodec.At(765, PacketFlow.Clientbound, Identifier).Encode(packet);

        Assert.NotEqual(json.Length, nbt.Length);

        // The tooltip starts right after: tx, start, length, count, "@a" (len + 2 bytes), present-flag.
        const int TooltipOffset = 4 + 3 + 1;

        // JSON string: a VarInt length, then the '{' of the serialized object form.
        Assert.Equal((byte)'{', json[TooltipOffset + 1]);

        // Network NBT: a bare root tag id with no root name. A plain text component serializes as TAG_String (8) under the root-tag-or-string network form, never a byte that could be confused with a JSON length prefix here.
        Assert.Equal(0x08, nbt[TooltipOffset]);
    }

    /// <summary>A protocol 404 command-completion entry. The tooltip is a 51-byte JSON string whose length prefix is 0x33, which is invalid as an NBT tag type when decoded with the later codec.</summary>
    [Theory]
    [InlineData(404)]
    [InlineData(754)]
    [InlineData(764)]
    public void CapturedLiveJsonFrame_Decodes(int protocol)
    {
        byte[] frame = Cat(
            [0x01],                             // transaction id 1
            [0x06],                             // range start 6
            [0x00],                             // range length 0
            [0x01],                             // one suggestion
            [0x02, 0x40, 0x61],                 // "@a"
            [0x01, 0x33],                       // tooltip present, JSON string of 51 bytes
            Encoding.UTF8.GetBytes("{\"translate\":\"argument.entity.selector.allPlayers\"}"));

        var p = (ClientboundCommandSuggestionsPacket)BoundCodec
            .At(protocol, PacketFlow.Clientbound, Identifier)
            .DecodeFrame(frame);

        Assert.Equal(1, p.TransactionId);
        Assert.Equal(6, p.RangeStart);
        Assert.Equal(0, p.RangeLength);
        Assert.Equal("@a", Assert.Single(p.Suggestions).Text);
        Assert.NotNull(p.Suggestions[0].Tooltip);
    }

    private static void AssertSample(ClientboundCommandSuggestionsPacket p)
    {
        Assert.Equal(7, p.TransactionId);
        Assert.Equal(6, p.RangeStart);
        Assert.Equal(0, p.RangeLength);
        Assert.Equal(2, p.Suggestions.Count);
        Assert.Equal("@a", p.Suggestions[0].Text);
        Assert.Equal("Nearest player", p.Suggestions[0].Tooltip!.ToPlainText());
        Assert.Equal("Steve", p.Suggestions[1].Text);
        Assert.Null(p.Suggestions[1].Tooltip);
    }

    // tx 7, start 6, length 0, two entries: "@a" with a tooltip, "Steve" without.
    private static byte[] JsonTooltipFrame() => Cat(
        [0x07, 0x06, 0x00, 0x02],
        Str("@a"),
        [0x01],
        Str("{\"text\":\"Nearest player\"}"),
        Str("Steve"),
        [0x00]);

    private static byte[] NbtTooltipFrame() => Cat(
        [0x07, 0x06, 0x00, 0x02],
        Str("@a"),
        [0x01],
        NbtTextComponent("Nearest player"),
        Str("Steve"),
        [0x00]);

    /// <summary>A network-NBT root compound holding one string field <c>text</c>: tag type, then the named TAG_String, then TAG_End. The root carries no name, as required by the 1.20.3+ network form.</summary>
    private static byte[] NbtTextComponent(string text)
    {
        byte[] key = Encoding.UTF8.GetBytes("text");
        byte[] value = Encoding.UTF8.GetBytes(text);
        var bytes = new List<byte> { 0x0A, 0x08, (byte)(key.Length >> 8), (byte)key.Length };
        bytes.AddRange(key);
        bytes.Add((byte)(value.Length >> 8));
        bytes.Add((byte)value.Length);
        bytes.AddRange(value);
        bytes.Add(0x00);
        return [.. bytes];
    }

    private static byte[] Str(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        return Cat(VarInt(utf8.Length), utf8);
    }

    private static byte[] VarInt(int value)
    {
        var bytes = new List<byte>();
        uint v = (uint)value;
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            bytes.Add(v != 0 ? (byte)(b | 0x80) : b);
        }
        while (v != 0);
        return [.. bytes];
    }

    private static byte[] Cat(params byte[][] parts)
    {
        var bytes = new List<byte>();
        foreach (byte[] part in parts)
            bytes.AddRange(part);

        return [.. bytes];
    }

    private static TheoryData<int> Spread(int[] protocols)
    {
        var data = new TheoryData<int>();
        foreach (int protocol in protocols)
            data.Add(protocol);

        return data;
    }
}
