using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>The v2 (protocol 760, 1.19.1/1.19.2) clientbound <c>player_chat</c> last-seen window, pinned against an independently generated frame rather than UMPK's encoder.</summary>
/// <remarks>
/// <para>The reference frame was generated independently and includes the signing header, signed body, filter mask, chat type, and packet wrapper. Every byte here came from that independent writer.</para>
/// <para>The fixture inputs: sender <c>11111111-2222-3333-4444-555555555555</c>, content "hello world", salt <c>0x0123456789ABCDEF</c>, timestamp epoch second 1700000000, filter PASS_THROUGH, chat type 1, sender name literal <c>Alice</c>, no target, no unsigned override, no preceding signature, header signature <c>Sig(0x30)</c>, and a TWO-entry last-seen window of (<c>aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee</c>, <c>Sig(0x10)</c>) and (<c>00112233-4455-6677-8899-aabbccddeeff</c>, <c>Sig(0x20)</c>), where <c>Sig(seed)[i] == (i * 31 + seed) &amp; 0xFF</c> over 256 bytes.</para>
/// <para>A non-empty window is the point of the fixture. An empty v2 window is a single <c>0x00</c> byte, which every wrong reading of this structure also produces, so an empty-window test proves nothing at all about the per-entry layout. The packet carries the sender's last-seen window, so as soon as two key-signed players are chatting the list is populated: this is the normal case, not an edge case.</para>
/// </remarks>
public sealed class LastSeenMessageCodecTests
{
    private const int V1Protocol = 759;
    private const int V2Protocol = 760;
    private const int V3Protocol = 761;
    private const string PlayerChatId = "minecraft:player_chat";

    private static readonly Guid Sender = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid PeerA = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid PeerB = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    /// <summary>The complete vanilla-written clientbound <c>player_chat</c> body, 874 bytes.</summary>
    private const string VanillaFrameHex =
        "00111111112222333344445555555555558002304f6e8daccbea0928476685a4c3e201203f5e7d9cbbdaf91837567594b3d2f1102f4e6d8cabcae908" +
        "27466584a3c2e1001f3e5d7c9bbad9f81736557493b2d1f00f2e4d6c8baac9e80726456483a2c1e0ff1e3d5c7b9ab9d8f71635547392b1d0ef0e2d4c" +
        "6b8aa9c8e70625446382a1c0dffe1d3c5b7a99b8d7f61534537291b0cfee0d2c4b6a89a8c7e60524436281a0bfdefd1c3b5a7998b7d6f51433527190" +
        "afceed0c2b4a6988a7c6e504234261809fbeddfc1b3a597897b6d5f4133251708faecdec0b2a496887a6c5e4032241607f9ebddcfb1a39587796b5d4" +
        "f31231506f8eadcceb0a29486786a5c4e30221405f7e9dbcdbfa1938577695b4d3f2110b68656c6c6f20776f726c64000000018bcfe5680001234567" +
        "89abcdef02aaaaaaaabbbbccccddddeeeeeeeeeeee8002102f4e6d8cabcae90827466584a3c2e1001f3e5d7c9bbad9f81736557493b2d1f00f2e4d6c" +
        "8baac9e80726456483a2c1e0ff1e3d5c7b9ab9d8f71635547392b1d0ef0e2d4c6b8aa9c8e70625446382a1c0dffe1d3c5b7a99b8d7f61534537291b0" +
        "cfee0d2c4b6a89a8c7e60524436281a0bfdefd1c3b5a7998b7d6f51433527190afceed0c2b4a6988a7c6e504234261809fbeddfc1b3a597897b6d5f4" +
        "133251708faecdec0b2a496887a6c5e4032241607f9ebddcfb1a39587796b5d4f31231506f8eadcceb0a29486786a5c4e30221405f7e9dbcdbfa1938" +
        "577695b4d3f211304f6e8daccbea0928476685a4c3e201203f5e7d9cbbdaf91837567594b3d2f100112233445566778899aabbccddeeff8002203f5e" +
        "7d9cbbdaf91837567594b3d2f1102f4e6d8cabcae90827466584a3c2e1001f3e5d7c9bbad9f81736557493b2d1f00f2e4d6c8baac9e80726456483a2" +
        "c1e0ff1e3d5c7b9ab9d8f71635547392b1d0ef0e2d4c6b8aa9c8e70625446382a1c0dffe1d3c5b7a99b8d7f61534537291b0cfee0d2c4b6a89a8c7e6" +
        "0524436281a0bfdefd1c3b5a7998b7d6f51433527190afceed0c2b4a6988a7c6e504234261809fbeddfc1b3a597897b6d5f4133251708faecdec0b2a" +
        "496887a6c5e4032241607f9ebddcfb1a39587796b5d4f31231506f8eadcceb0a29486786a5c4e30221405f7e9dbcdbfa1938577695b4d3f211304f6e" +
        "8daccbea0928476685a4c3e201000101107b2274657874223a22416c696365227d00";

    /// <summary>The same independently generated window on its own, 549 bytes: a VarInt count of 2, then per entry a big-endian uuid and a VarInt-prefixed 256-byte signature (1 + 2 * (16 + 2 + 256)).</summary>
    private const string VanillaLastSeenHex =
        "02aaaaaaaabbbbccccddddeeeeeeeeeeee8002102f4e6d8cabcae90827466584a3c2e1001f3e5d7c9bbad9f81736557493b2d1f00f2e4d6c8baac9e8" +
        "0726456483a2c1e0ff1e3d5c7b9ab9d8f71635547392b1d0ef0e2d4c6b8aa9c8e70625446382a1c0dffe1d3c5b7a99b8d7f61534537291b0cfee0d2" +
        "c4b6a89a8c7e60524436281a0bfdefd1c3b5a7998b7d6f51433527190afceed0c2b4a6988a7c6e504234261809fbeddfc1b3a597897b6d5f4133251" +
        "708faecdec0b2a496887a6c5e4032241607f9ebddcfb1a39587796b5d4f31231506f8eadcceb0a29486786a5c4e30221405f7e9dbcdbfa193857769" +
        "5b4d3f211304f6e8daccbea0928476685a4c3e201203f5e7d9cbbdaf91837567594b3d2f100112233445566778899aabbccddeeff8002203f5e7d9c" +
        "bbdaf91837567594b3d2f1102f4e6d8cabcae90827466584a3c2e1001f3e5d7c9bbad9f81736557493b2d1f00f2e4d6c8baac9e80726456483a2c1e" +
        "0ff1e3d5c7b9ab9d8f71635547392b1d0ef0e2d4c6b8aa9c8e70625446382a1c0dffe1d3c5b7a99b8d7f61534537291b0cfee0d2c4b6a89a8c7e605" +
        "24436281a0bfdefd1c3b5a7998b7d6f51433527190afceed0c2b4a6988a7c6e504234261809fbeddfc1b3a597897b6d5f4133251708faecdec0b2a4" +
        "96887a6c5e4032241607f9ebddcfb1a39587796b5d4f31231506f8eadcceb0a29486786a5c4e30221405f7e9dbcdbfa1938577695b4d3f211304f6e" +
        "8daccbea0928476685a4c3e201";

    /// <summary>The vanilla frame length, stated outright.</summary>
    private const int VanillaFrameBytes = 874;

    /// <summary>The vanilla last-seen block length, stated outright: 1 + 2 * (16 + 2 + 256).</summary>
    private const int VanillaLastSeenBytes = 549;

    /// <summary>Where the last-seen block starts inside the frame: 1 (absent preceding signature) + 16 (sender) + 2 + 256 (header signature) + 1 + 11 ("hello world") + 1 (absent formatted component) + 8 (timestamp) + 8 (salt).</summary>
    private const int LastSeenOffset = 304;

    private static byte[] Sig(int seed)
    {
        var bytes = new byte[256];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((i * 31) + seed);

        return bytes;
    }

    /// <summary>The vanilla frame decodes to the fields vanilla put in it, including EACH last-seen entry's own sender profile id, and re-encodes byte for byte. Substituting another UUID for an entry changes the frame, making per-entry sender preservation observable.</summary>
    [Fact]
    public void V2VanillaFrame_DecodesEveryLastSeenProfileId_AndReEncodesByteExact()
    {
        byte[] frame = Convert.FromHexString(VanillaFrameHex);
        Assert.Equal(VanillaFrameBytes, frame.Length);

        var packet = (ClientboundPlayerChatPacket)Cb(V2Protocol).DecodeFrame(frame);

        Assert.Equal(Sender, packet.Sender);
        Assert.Equal("hello world", packet.SignedContent);
        Assert.Null(packet.SignedComponent);
        Assert.Null(packet.PreviousSignature);
        Assert.Equal(0x0123456789ABCDEFL, packet.Salt);
        Assert.Equal(1_700_000_000_000L, packet.TimestampMillis);
        Assert.Equal(Sig(0x30), packet.Signature);
        Assert.Equal(1, packet.ChatTypeId);
        Assert.Equal("Alice", packet.SenderName.ToPlainText());

        Assert.Equal(2, packet.LastSeen.Count);
        Assert.Equal(PeerA, packet.LastSeen[0].ProfileId);
        Assert.Equal(Sig(0x10), packet.LastSeen[0].FullSignature);
        Assert.Equal(PeerB, packet.LastSeen[1].ProfileId);
        Assert.Equal(Sig(0x20), packet.LastSeen[1].FullSignature);

        // The two entries carry DIFFERENT profile ids, and neither is the chat sender. A decode that dropped the per-entry uuid, or a verifier that substituted the sender for it, cannot satisfy this and cannot satisfy the re-encode below.
        Assert.NotEqual(packet.LastSeen[0].ProfileId, packet.LastSeen[1].ProfileId);
        Assert.NotEqual(packet.Sender, packet.LastSeen[0].ProfileId);
        Assert.NotEqual(packet.Sender, packet.LastSeen[1].ProfileId);

        Assert.Equal(frame, Cb(V2Protocol).Encode(packet));
    }

    /// <summary>The last-seen block on its own, at its stated offset and length, byte-identical to the independent frame for the same two entries.</summary>
    [Fact]
    public void V2LastSeenBlock_MatchesVanillaLastSeenMessagesWrite()
    {
        byte[] expected = Convert.FromHexString(VanillaLastSeenHex);
        Assert.Equal(VanillaLastSeenBytes, expected.Length);

        byte[] frame = Convert.FromHexString(VanillaFrameHex);
        Assert.Equal(expected, frame.AsSpan(LastSeenOffset, VanillaLastSeenBytes).ToArray());

        // And UMPK's own writer reproduces that block from the decoded model, at the same offset.
        byte[] reEncoded = Cb(V2Protocol).Encode(Cb(V2Protocol).DecodeFrame(frame));
        Assert.Equal(expected, reEncoded.AsSpan(LastSeenOffset, VanillaLastSeenBytes).ToArray());
    }

    /// <summary>Cross-era rejection: the vanilla v2 frame must not survive the v1 (759) or v3 (761) codec. A round trip cannot make this assertion, because a wrong codec agrees with itself.</summary>
    [Fact]
    public void V2VanillaFrame_IsRejectedByTheV1AndV3Codecs()
    {
        byte[] frame = Convert.FromHexString(VanillaFrameHex);

        AssertRejects(Cb(V1Protocol), frame, "the v1 codec must not accept a v2 player_chat frame");
        AssertRejects(Cb(V3Protocol), frame, "the v3 codec must not accept a v2 player_chat frame");
    }

    /// <summary>The converse direction, so the rejection above is not an artefact of one frame: a v3 frame carrying a non-empty packed last-seen window must not survive the v2 codec either. It is built through the v3 codec (the era whose shape is being offered), which is legitimate here because the assertion is about the OTHER era refusing it.</summary>
    [Fact]
    public void V3Frame_IsRejectedByTheV2Codec()
    {
        var v3 = new ClientboundPlayerChatPacket(
            Sender, Index: 4, Signature: Sig(0x55), SignedContent: "hello world",
            TimestampMillis: 1_700_000_000_000L, Salt: 0x0123456789ABCDEFL,
            UnsignedContent: null, ChatTypeId: 1,
            SenderName: Umpk.Text.Component.Text("Alice"), TargetName: null)
        {
            LastSeen = [PackedMessageSignature.Full(Sig(0x10)), PackedMessageSignature.Cached(6)],
        };

        byte[] frame = Cb(V3Protocol).Encode(v3);
        AssertRejects(Cb(V2Protocol), frame, "the v2 codec must not accept a v3 player_chat frame");
    }

    /// <summary>v3 has no per-entry profile id ANYWHERE. Each packed entry is only an integer id and an optional full signature. So a v3 round trip must leave <see cref="PackedMessageSignature.ProfileId"/> empty no matter what was put in, and the model field is structurally unreachable on that era rather than merely unobserved.</summary>
    [Fact]
    public void V3RoundTrip_CarriesNoProfileId()
    {
        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 1, Signature: Sig(0x44), SignedContent: "x",
            TimestampMillis: 1L, Salt: 2L, UnsignedContent: null, ChatTypeId: 0,
            SenderName: Umpk.Text.Component.Text("A"), TargetName: null)
        {
            // Deliberately populated: the v3 wire has no slot for it, so it must be gone after a trip.
            LastSeen =
            [
                PackedMessageSignature.Full(PeerA, Sig(0xAA)),
                PackedMessageSignature.Cached(6),
            ],
        };

        byte[] frame = Cb(V3Protocol).Encode(packet);
        var back = (ClientboundPlayerChatPacket)Cb(V3Protocol).DecodeFrame(frame);

        Assert.Equal(2, back.LastSeen.Count);
        Assert.Equal(Guid.Empty, back.LastSeen[0].ProfileId);
        Assert.Equal(Guid.Empty, back.LastSeen[1].ProfileId);
        Assert.Equal(Sig(0xAA), back.LastSeen[0].FullSignature);
        Assert.Equal(6, back.LastSeen[1].Id);

        // The frame length is the v3 packed shape, and it has no room for a uuid anywhere: 16 (sender) + 1 (index) + 1 (signature present) + 256 (signature) + 1 + 1 ("x")
        // + 8 (timestamp) + 8 (salt) + 1 (last-seen count)
        // + 1 + 256 (full entry: VarInt 0 then the bytes) + 1 (cached entry: VarInt 7, no bytes)
        // + 1 (no unsigned) + 1 (filter) + 1 (chat type) + 1 + 12 ({"text":"A"}) + 1 (no target)
        // = 568. Two per-entry uuids would have cost 32 more.
        Assert.Equal(568, frame.Length);
    }

    private static void AssertRejects(BoundPacketCodec bound, byte[] frame, string because)
    {
        object decoded;
        try
        {
            decoded = bound.DecodeFrame(frame);
        }
        catch (Exception)
        {
            return;
        }

        Assert.False(frame.SequenceEqual(bound.Encode(decoded)), because);
    }

    private static BoundPacketCodec Cb(int protocol) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, PlayerChatId);
}
