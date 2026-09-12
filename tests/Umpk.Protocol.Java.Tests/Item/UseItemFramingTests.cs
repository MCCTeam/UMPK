using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>The serverbound <c>use_item</c> era boundaries. The packet grows in exactly two steps: it is the hand alone from 1.9 to 1.18.2 (107-758), it gains the block-action sequence VarInt at 1.19 (759), and it gains the yRot/xRot floats at 1.21 (767).</summary>
/// <remarks>
/// <para>Applying the four-field 1.21 form to protocols 477-758 leaves nine trailing bytes, while applying it to protocols 759-763 leaves eight. Frame-exact readers reject either mismatch and close the connection.</para>
/// <para>Through 1.18.2, the packet carries only a hand VarInt. Protocols 1.19, 1.20.4, and 1.20.6 carry a hand VarInt and a sequence VarInt. Protocols 1.21, 1.21.2, and 26.2 add yaw and pitch floats. Protocols 759-766 carry exactly a hand and a sequence; the preceding band carries no sequence.</para>
/// <para>A round trip cannot detect a consistently wrong codec. The length assertions expose the framing mismatch, and the cross-era rejection pair pins each boundary to the exact protocol.</para>
/// </remarks>
public sealed class UseItemFramingTests
{
    private const string Identifier = "minecraft:use_item";

    /// <summary>Every protocol whose use_item is the hand VarInt and nothing else.</summary>
    public static readonly int[] HandOnlyBand =
    [
        107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404, 477, 480, 485, 490, 498,
        573, 575, 578, 735, 736, 751, 753, 754, 755, 756, 757, 758,
    ];

    /// <summary>Every protocol whose use_item is hand + sequence, with no rotation floats.</summary>
    public static readonly int[] SequenceBand = [759, 760, 761, 762, 763, 764, 765, 766];

    /// <summary>Every protocol whose use_item is hand + sequence + yRot + xRot.</summary>
    public static readonly int[] RotationBand = [767, 768, 769, 770, 771, 772, 773, 774, 775, 776];

    /// <summary>The three bands together, read by <c>AllProtocolTableCoverageTests</c>.</summary>
    public static IReadOnlyList<int> Protocols() => [.. HandOnlyBand, .. SequenceBand, .. RotationBand];

    public static TheoryData<int> HandOnlyProtocols => Spread(HandOnlyBand);

    public static TheoryData<int> SequenceProtocols => Spread(SequenceBand);

    public static TheoryData<int> RotationProtocols => Spread(RotationBand);

    /// <summary>The packet every assertion below sends; the values are chosen so each field is visible.</summary>
    private static ServerboundUseItemPacket Sample => new(Hand: 1, Sequence: 55, YRot: 12.5f, XRot: -30.0f);

    // One frame-length assertion per band exposes mismatched field widths.

    /// <summary>One byte on the whole 1.9-1.18.2 band: the hand VarInt.</summary>
    [Theory]
    [MemberData(nameof(HandOnlyProtocols))]
    public void HandOnlyBand_EncodesExactlyTheHandVarInt(int protocol)
    {
        byte[] frame = BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier).Encode(Sample);

        Assert.Equal([0x01], frame);
    }

    /// <summary>Two bytes on 1.19-1.20.6: the hand VarInt then the sequence VarInt, and nothing after it.</summary>
    [Theory]
    [MemberData(nameof(SequenceProtocols))]
    public void SequenceBand_EncodesTheHandAndSequenceOnly(int protocol)
    {
        byte[] frame = BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier).Encode(Sample);

        Assert.Equal([0x01, 0x37], frame);
    }

    /// <summary>Ten bytes from 1.21 on: hand, sequence, then the two big-endian floats. 12.5f is 0x41480000 and -30.0f is 0xC1F00000, so the exact bytes pin the ORDER (yRot before xRot) as well as the width.</summary>
    [Theory]
    [MemberData(nameof(RotationProtocols))]
    public void RotationBand_EncodesTheHandSequenceAndBothRotations(int protocol)
    {
        byte[] frame = BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier).Encode(Sample);

        Assert.Equal(
            [0x01, 0x37, 0x41, 0x48, 0x00, 0x00, 0xC1, 0xF0, 0x00, 0x00],
            frame);
    }

    /// <summary>Against the hand-only band the modern frame is nine bytes too long, and against the sequence band it is eight bytes too long. If either boundary moves, one of these differences changes.</summary>
    [Fact]
    public void TheThreeWireLayouts_DifferByTheByteCountsTheServersReported()
    {
        int handOnly = BoundCodec.At(754, PacketFlow.Serverbound, Identifier).Encode(Sample).Length;
        int sequence = BoundCodec.At(762, PacketFlow.Serverbound, Identifier).Encode(Sample).Length;
        int rotation = BoundCodec.At(776, PacketFlow.Serverbound, Identifier).Encode(Sample).Length;

        Assert.Equal(9, rotation - handOnly);
        Assert.Equal(8, rotation - sequence);
    }

    // Cross-era rejections in both directions at every boundary.

    /// <summary>A hand-only protocol must not accept a frame carrying a sequence or rotations. Decoding is frame-exact, so trailing bytes cause a protocol violation.</summary>
    [Theory]
    [InlineData(107)]
    [InlineData(477)]
    [InlineData(754)]
    [InlineData(758)]
    public void HandOnlyBand_RejectsALaterWireLayoutFrame(int protocol)
    {
        Assert.ThrowsAny<Exception>(() => BoundCodec
            .At(protocol, PacketFlow.Serverbound, Identifier)
            .DecodeFrame(SequenceFrame()));

        Assert.ThrowsAny<Exception>(() => BoundCodec
            .At(protocol, PacketFlow.Serverbound, Identifier)
            .DecodeFrame(RotationFrame()));
    }

    /// <summary>The sequence band must reject BOTH neighbours: the shorter hand-only frame underruns its sequence VarInt and the longer 1.21 frame leaves the two floats behind. 759 and 766 are the boundary protocols themselves, so this is what stops the era starting one version out in either direction.</summary>
    [Theory]
    [InlineData(759)]
    [InlineData(763)]
    [InlineData(766)]
    public void SequenceBand_RejectsBothNeighbouringWireLayoutFrames(int protocol)
    {
        Assert.ThrowsAny<Exception>(() => BoundCodec
            .At(protocol, PacketFlow.Serverbound, Identifier)
            .DecodeFrame(HandOnlyFrame()));

        Assert.ThrowsAny<Exception>(() => BoundCodec
            .At(protocol, PacketFlow.Serverbound, Identifier)
            .DecodeFrame(RotationFrame()));
    }

    /// <summary>The 1.21 band must reject both shorter forms; each underruns a float it needs.</summary>
    [Theory]
    [InlineData(767)]
    [InlineData(776)]
    public void RotationBand_RejectsBothEarlierWireLayoutFrames(int protocol)
    {
        Assert.ThrowsAny<Exception>(() => BoundCodec
            .At(protocol, PacketFlow.Serverbound, Identifier)
            .DecodeFrame(HandOnlyFrame()));

        Assert.ThrowsAny<Exception>(() => BoundCodec
            .At(protocol, PacketFlow.Serverbound, Identifier)
            .DecodeFrame(SequenceFrame()));
    }

    // What each band actually decodes, so a rejection is not the only thing proven.

    [Theory]
    [MemberData(nameof(HandOnlyProtocols))]
    public void HandOnlyBand_DecodesTheHandAndLeavesTheRestAtZero(int protocol)
    {
        var p = (ServerboundUseItemPacket)BoundCodec
            .At(protocol, PacketFlow.Serverbound, Identifier)
            .DecodeFrame(HandOnlyFrame());

        Assert.Equal(1, p.Hand);
        Assert.Equal(0, p.Sequence);
        Assert.Equal(0f, p.YRot);
        Assert.Equal(0f, p.XRot);
    }

    [Theory]
    [MemberData(nameof(SequenceProtocols))]
    public void SequenceBand_DecodesTheHandAndSequence(int protocol)
    {
        var p = (ServerboundUseItemPacket)BoundCodec
            .At(protocol, PacketFlow.Serverbound, Identifier)
            .DecodeFrame(SequenceFrame());

        Assert.Equal(1, p.Hand);
        Assert.Equal(55, p.Sequence);
        Assert.Equal(0f, p.YRot);
        Assert.Equal(0f, p.XRot);
    }

    [Theory]
    [MemberData(nameof(RotationProtocols))]
    public void RotationBand_DecodesEveryField(int protocol)
    {
        var p = (ServerboundUseItemPacket)BoundCodec
            .At(protocol, PacketFlow.Serverbound, Identifier)
            .DecodeFrame(RotationFrame());

        Assert.Equal(Sample, p);
    }

    // The binding itself, so a future edit cannot move a band silently.

    [Theory]
    [MemberData(nameof(HandOnlyProtocols))]
    public void HandOnlyBand_BindsTheHandOnlyCodec(int protocol) =>
        Assert.Equal(
            "UseItemCodecs.UseItemV1_9",
            BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier).CodecIdentity);

    [Theory]
    [MemberData(nameof(SequenceProtocols))]
    public void SequenceBand_BindsTheHandAndSequenceCodec(int protocol) =>
        Assert.Equal(
            "UseItemCodecs.UseItemV1_19",
            BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier).CodecIdentity);

    [Theory]
    [MemberData(nameof(RotationProtocols))]
    public void RotationBand_BindsTheModernCodec(int protocol) =>
        Assert.Equal(
            "UseItemCodecs.UseItemModern",
            BoundCodec.At(protocol, PacketFlow.Serverbound, Identifier).CodecIdentity);

    /// <summary>The sibling packet has the same two era changes and is bound in the same file. use_item_on is hand + block-hit through 1.18.2, gains the sequence at 1.19 and the world-border bool (inside the block-hit-result fields, before the sequence) at 1.21.2. These assertions pin its field widths independently.</summary>
    [Theory]
    [InlineData(477, 23)]
    [InlineData(754, 23)]
    [InlineData(758, 23)]
    [InlineData(759, 24)]
    [InlineData(763, 24)]
    [InlineData(767, 24)]
    [InlineData(768, 25)]
    [InlineData(776, 25)]
    public void UseItemOn_TheSiblingPacket_KeepsItsOwnWireLayoutWidths(int protocol, int expectedLength)
    {
        var place = new ServerboundUseItemOnPacket(
            Hand: 0, new BlockPos(10, 64, -20), Face: 1, CursorX: 0.5f, CursorY: 0.25f, CursorZ: 0.75f,
            Inside: false, WorldBorderHit: false, Sequence: 99);

        byte[] frame = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:use_item_on").Encode(place);

        Assert.Equal(expectedLength, frame.Length);
    }

    private static byte[] HandOnlyFrame() => [0x01];

    private static byte[] SequenceFrame() => [0x01, 0x37];

    private static byte[] RotationFrame() =>
        [0x01, 0x37, 0x41, 0x48, 0x00, 0x00, 0xC1, 0xF0, 0x00, 0x00];

    private static TheoryData<int> Spread(int[] protocols)
    {
        var data = new TheoryData<int>();
        foreach (int protocol in protocols)
            data.Add(protocol);

        return data;
    }
}
