using System.Text;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>The three serverbound signed-chat generations, pinned by FRAME LENGTH and by cross-era rejection rather than by round trip.</summary>
/// <remarks>
/// <para>A round trip through <c>minecraft:chat</c> cannot see a misbinding: encode and decode go through the same codec, so a wrong era can agree with itself. What separates the three generations is their wire shape, so the assertions here check the exact byte length of a known frame at every protocol in the band, and a literal frame from one era being refused by the codecs of the others.</para>
/// <para>The three wire generations are:</para>
/// <list type="bullet">
/// <item><description>
/// v1, protocol 759: a string capped at 256 characters, an epoch-millis long, a salt long, a length-prefixed signature, then the preview boolean. No acknowledgement of any kind.
/// </description></item>
/// <item><description>
/// v2, protocol 760: the v1 fields plus a trailing last-seen update. Its wire order is a string capped at 256 characters, epoch-millis long, salt long, length-prefixed signature, preview boolean, a counted list of UUID-and-signature entries, then one optional entry of the same shape.
/// </description></item>
/// <item><description>
/// v3, protocols 761-777: message, instant, salt, a NULLABLE FIXED 256-byte signature, then the offset/bitset acknowledgement window. The 20-bit bitset is 3 bytes; 770 appends a checksum byte.
/// </description></item>
/// </list>
/// <para>The per-protocol length matrix pins the one-byte checksum difference between adjacent v3 bands.</para>
/// </remarks>
public sealed class SignedChatFramingTests
{
    private const string ChatId = "minecraft:chat";

    /// <summary>v1 lives alone on 759.</summary>
    private const int V1 = 759;

    /// <summary>v2 lives alone on 760.</summary>
    private const int V2 = 760;

    /// <summary>Every protocol carrying the v3 body WITHOUT the acknowledgement checksum byte.</summary>
    private static readonly int[] V3NoChecksum = [761, 762, 763, 764, 765, 766, 767, 768, 769];

    /// <summary>Every protocol carrying the v3 body WITH the acknowledgement checksum byte.</summary>
    private static readonly int[] V3Checksum = [770, 771, 772, 773, 774, 775, 776, 777];

    private static readonly Guid Alex = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    /// <summary>A hand-built v1 frame: "hi", timestamp, salt, a 4-byte signature, preview false.</summary>
    private static byte[] V1Frame() =>
        Concat(Str("hi"), I64(0x0102030405060708L), I64(0x1112131415161718L), VarInt(4), [1, 2, 3, 4], [0]);

    /// <summary>A hand-built v2 frame: the v1 frame plus a one-entry last-seen list and a present last-received entry. Both are deliberately NON-EMPTY, because an empty collection and an absent optional encode identically under a right and a wrong framing.</summary>
    private static byte[] V2Frame() =>
        Concat(
            V1Frame(),
            VarInt(1), Uuid(Alex), VarInt(4), [9, 9, 9, 9],
            [1], Uuid(Alex), VarInt(2), [7, 7]);

    /// <summary>A hand-built v3 frame: a present 256-byte signature, offset 5, a non-zero bitset.</summary>
    private static byte[] V3Frame(bool checksum)
    {
        var signature = new byte[256];
        for (int i = 0; i < signature.Length; i++)
            signature[i] = (byte)i;

        byte[] frame = Concat(
            Str("hi"), I64(0x0102030405060708L), I64(0x1112131415161718L),
            [1], signature, VarInt(5), [0xAB, 0xCD, 0x0F]);

        return checksum ? Concat(frame, [0x42]) : frame;
    }

    // 3 (string) + 8 + 8 + 1 (varint length) + 4 (signature) + 1 (preview) = 25.
    private const int V1FrameBytes = 25;

    // 25 + 1 + 16 + 1 + 4 (one last-seen entry) + 1 + 16 + 1 + 2 (present last-received) = 67.
    private const int V2FrameBytes = 67;

    // 3 + 8 + 8 + 1 (present flag) + 256 + 1 (varint offset) + 3 (bitset) = 280.
    private const int V3FrameBytes = 280;

    /// <summary>The literal frame each era's bound codec must accept and re-emit byte for byte, with the LENGTH stated outright. A neighbouring era's codec cannot produce these lengths from these values.</summary>
    [Fact]
    public void EachGeneration_DecodesItsOwnLiteralFrame_AtTheStatedLength()
    {
        byte[] v1 = V1Frame();
        Assert.Equal(V1FrameBytes, v1.Length);
        var p1 = (ServerboundSignedChatPacket)Sb(V1).DecodeFrame(v1);
        Assert.Equal("hi", p1.Message);
        Assert.Equal(0x0102030405060708L, p1.TimestampMillis);
        Assert.Equal(0x1112131415161718L, p1.Salt);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, p1.Signature);
        Assert.Empty(p1.LegacyLastSeen);
        Assert.Null(p1.LegacyLastReceived);
        Assert.Equal(v1, Sb(V1).Encode(p1));

        byte[] v2 = V2Frame();
        Assert.Equal(V2FrameBytes, v2.Length);
        var p2 = (ServerboundSignedChatPacket)Sb(V2).DecodeFrame(v2);
        LastSeenMessageEntry seen = Assert.Single(p2.LegacyLastSeen);
        Assert.Equal(Alex, seen.ProfileId);
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, seen.Signature);
        Assert.NotNull(p2.LegacyLastReceived);
        Assert.Equal(new byte[] { 7, 7 }, p2.LegacyLastReceived!.Signature);
        Assert.Equal(v2, Sb(V2).Encode(p2));

        byte[] v3 = V3Frame(checksum: false);
        Assert.Equal(V3FrameBytes, v3.Length);
        var p3 = (ServerboundSignedChatPacket)Sb(V3NoChecksum[0]).DecodeFrame(v3);
        Assert.Equal(5, p3.LastSeen.Offset);
        Assert.Equal(new byte[] { 0xAB, 0xCD, 0x0F }, p3.LastSeen.Acknowledged);
        Assert.Equal(0, p3.LastSeen.Checksum);
        Assert.Equal(v3, Sb(V3NoChecksum[0]).Encode(p3));

        byte[] v3c = V3Frame(checksum: true);
        Assert.Equal(V3FrameBytes + 1, v3c.Length);
        var p3c = (ServerboundSignedChatPacket)Sb(V3Checksum[0]).DecodeFrame(v3c);
        Assert.Equal(0x42, p3c.LastSeen.Checksum);
        Assert.Equal(v3c, Sb(V3Checksum[0]).Encode(p3c));
    }

    /// <summary>The length matrix over every protocol in the signing band, encoding ONE packet value. The whole band is walked because a misbinding is a per-protocol property: a codec test in isolation passes happily while the timeline selects the wrong era.</summary>
    [Theory]
    [MemberData(nameof(SigningBand))]
    public void EveryProtocolInTheBand_EncodesItsWireLayoutLength(int protocol, int expectedLength)
    {
        var signature = new byte[256];
        var packet = new ServerboundSignedChatPacket(
            "hi", 0x0102030405060708L, 0x1112131415161718L, signature,
            new LastSeenMessagesUpdate(5, [0xAB, 0xCD, 0x0F], 0x42));

        Assert.Equal(expectedLength, Sb(protocol).Encode(packet).Length);
    }

    /// <summary>The band, each protocol paired with the byte length its era must produce for the value above. v1 and v2 length-prefix the signature, so 256 bytes cost a two-byte VarInt; v3 writes the fixed 256 bytes raw behind a one-byte present flag.</summary>
    public static TheoryData<int, int> SigningBand
    {
        get
        {
            var data = new TheoryData<int, int>();

            // v1: 3 + 8 + 8 + 2 (VarInt 256) + 256 + 1 = 278.
            data.Add(V1, 278);

            // v2: v1 plus an empty last-seen collection (VarInt 0) and an absent optional = 278 + 2.
            data.Add(V2, 280);

            foreach (int protocol in V3NoChecksum)
                data.Add(protocol, V3FrameBytes);

            foreach (int protocol in V3Checksum)
                data.Add(protocol, V3FrameBytes + 1);

            return data;
        }
    }

    /// <summary>Cross-era rejection. Each literal frame is offered to the codecs of the other generations and must not survive: either the decode faults, or it re-encodes to different bytes. This is the assertion a round trip structurally cannot make.</summary>
    [Fact]
    public void NoGenerationAcceptsAnotherGenerationsFrame()
    {
        byte[] v1 = V1Frame();
        byte[] v2 = V2Frame();
        byte[] v3 = V3Frame(checksum: false);
        byte[] v3c = V3Frame(checksum: true);

        AssertRejects(Sb(V2), v1, "the v2 codec must not accept a v1 frame: it has no acknowledgement block");
        AssertRejects(Sb(V3NoChecksum[0]), v1, "the v3 codec must not accept a v1 frame");
        AssertRejects(Sb(V3Checksum[0]), v1, "the v3 checksum codec must not accept a v1 frame");

        AssertRejects(Sb(V1), v2, "the v1 codec must not accept a v2 frame: the ack block would trail");
        AssertRejects(Sb(V3NoChecksum[0]), v2, "the v3 codec must not accept a v2 frame");
        AssertRejects(Sb(V3Checksum[0]), v2, "the v3 checksum codec must not accept a v2 frame");

        AssertRejects(Sb(V1), v3, "the v1 codec must not accept a v3 frame");
        AssertRejects(Sb(V2), v3, "the v2 codec must not accept a v3 frame");

        // The one-byte pair, which is the era collapse this timeline has actually suffered.
        AssertRejects(Sb(V3Checksum[0]), v3, "a checksum-era codec must not accept a checksum-less frame");
        AssertRejects(Sb(V3NoChecksum[0]), v3c, "a checksum-less codec must not accept a checksum frame");
    }

    /// <summary>The unsigned control. Every era must also frame a NULL signature, because that is what an offline session sends and it is the shape the legacy-era length assertions above cannot cover.</summary>
    [Theory]
    [MemberData(nameof(UnsignedBand))]
    public void EveryProtocolInTheBand_FramesAnUnsignedSend(int protocol, int expectedLength)
    {
        var packet = new ServerboundSignedChatPacket(
            "hi", 0x0102030405060708L, 0x1112131415161718L, Signature: null,
            new LastSeenMessagesUpdate(0, [0, 0, 0], 0));

        byte[] frame = Sb(protocol).Encode(packet);
        Assert.Equal(expectedLength, frame.Length);

        var decoded = (ServerboundSignedChatPacket)Sb(protocol).DecodeFrame(frame);
        Assert.Null(decoded.Signature);
    }

    /// <summary>Unsigned lengths: v1 3+8+8+1+1 = 21, v2 21+2 = 23, v3 3+8+8+1+1+3 (+1) = 24 (25).</summary>
    public static TheoryData<int, int> UnsignedBand
    {
        get
        {
            var data = new TheoryData<int, int> { { V1, 21 }, { V2, 23 } };
            foreach (int protocol in V3NoChecksum)
                data.Add(protocol, 24);

            foreach (int protocol in V3Checksum)
                data.Add(protocol, 25);

            return data;
        }
    }

    /// <summary>Asserts that a frame does NOT survive a codec: it either faults or re-encodes differently.</summary>
    private static void AssertRejects(BoundPacketCodec bound, byte[] frame, string because)
    {
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

    private static BoundPacketCodec Sb(int protocol) =>
        BoundCodec.At(protocol, PacketFlow.Serverbound, ChatId);

    // Literal-frame helpers, deliberately hand-rolled so no production writer can agree with itself.

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

    private static byte[] Str(string s)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(s);
        return [.. VarInt(utf8.Length), .. utf8];
    }

    private static byte[] I64(long v)
    {
        var b = new byte[8];
        for (int i = 0; i < 8; i++)
            b[i] = (byte)(v >> (56 - (8 * i)));

        return b;
    }

    private static byte[] Uuid(Guid guid) => guid.ToByteArray(bigEndian: true);

    private static byte[] Concat(params byte[][] parts)
    {
        var all = new List<byte>();
        foreach (byte[] part in parts)
            all.AddRange(part);

        return [.. all];
    }
}
