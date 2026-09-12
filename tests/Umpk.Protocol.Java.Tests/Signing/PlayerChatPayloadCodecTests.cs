using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>Hand-built signing-era player_chat frames for the 1.19 v1, 1.19.2 v2, and 1.19.3 v3 wire layouts. Each structurally distinct generation is asserted independently for byte-exact round trip.</summary>
public class PlayerChatPayloadCodecTests
{
    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");

    private static byte[] Sig(byte fill)
    {
        var s = new byte[256];
        Array.Fill(s, fill);
        return s;
    }

    [Fact]
    public void PlayerChat_V1_19_RoundTripsByteExact()
    {
        // 759 (1.19.0): signed component + optional unsigned + varint type + sender(uuid+name+target)
        // + instant + salt-signature pair (long salt + byte-array signature).
        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: Sig(0x11), SignedContent: null,
            TimestampMillis: 1_700_000_000_000L, Salt: 0x0123_4567_89AB_CDEFL,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Steve"), TargetName: null)
        {
            SignedComponent = Component.Text("hello v1"),
        };

        byte[] wire = CodecRoundTrip.Encode(ChatCodecs.V1_19, packet);
        ClientboundPlayerChatPacket back = CodecRoundTrip.Decode(ChatCodecs.V1_19, wire);
        byte[] wire2 = CodecRoundTrip.Encode(ChatCodecs.V1_19, back);

        Assert.Equal(wire, wire2);
        Assert.Equal(Sender, back.Sender);
        Assert.Equal(packet.Salt, back.Salt);
        Assert.Equal(256, back.Signature!.Length);
        Assert.Equal("hello v1", back.SignedComponent!.ToPlainText());
    }

    [Fact]
    public void PlayerChat_V1_19_1_RoundTripsByteExact()
    {
        // 760 (1.19.1/2): PlayerChatMessage (signed header prev-sig? + uuid; header signature; signed body = PLAIN message string + OPTIONAL formatted component; instant + salt + last-seen) + optional unsigned + filter, then chat type. The signed body is a plain string here (not a single JSON component as in 1.19.0).
        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: Sig(0x22), SignedContent: "hello v2",
            TimestampMillis: 1_700_000_000_000L, Salt: 0x1122_3344_5566_7788L,
            UnsignedContent: null, ChatTypeId: 1, SenderName: Component.Text("Steve"), TargetName: null)
        {
            SignedComponent = Component.Text("hello v2 (formatted)"),
            PreviousSignature = null,
            FilterType = 0,
        };

        byte[] wire = CodecRoundTrip.Encode(ChatCodecs.V1_19_1, packet);
        ClientboundPlayerChatPacket back = CodecRoundTrip.Decode(ChatCodecs.V1_19_1, wire);
        byte[] wire2 = CodecRoundTrip.Encode(ChatCodecs.V1_19_1, back);

        Assert.Equal(wire, wire2);
        Assert.Equal(1, back.ChatTypeId);
        Assert.Equal("hello v2", back.SignedContent);
        Assert.Equal("hello v2 (formatted)", back.SignedComponent!.ToPlainText());
        Assert.Equal(0, back.FilterType);
    }

    [Fact]
    public void PlayerChat_V1_19_3_RoundTripsByteExact()
    {
        // 761/762/763 (1.19.3+): uuid sender + varint index + nullable 256-byte signature + signed body (utf content + instant + salt + last-seen) + nullable unsigned + filter + chat type.
        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 3, Signature: Sig(0x33), SignedContent: "hello v3",
            TimestampMillis: 1_700_000_000_000L, Salt: 0x00FF_00FF_00FF_00FFL,
            UnsignedContent: Component.Text("hello v3 (unsigned)"), ChatTypeId: 0,
            SenderName: Component.Text("Steve"), TargetName: null)
        {
            LastSeen = [],
            FilterType = 0,
        };

        byte[] wire = CodecRoundTrip.Encode(ChatCodecs.V1_19_3, packet);
        ClientboundPlayerChatPacket back = CodecRoundTrip.Decode(ChatCodecs.V1_19_3, wire);
        byte[] wire2 = CodecRoundTrip.Encode(ChatCodecs.V1_19_3, back);

        Assert.Equal(wire, wire2);
        Assert.Equal(3, back.Index);
        Assert.Equal("hello v3", back.SignedContent);
        Assert.Equal(0x00FF_00FF_00FF_00FFL, back.Salt);
        Assert.Equal("hello v3 (unsigned)", back.UnsignedContent!.ToPlainText());
    }

    [Fact]
    public void PlayerChat_V1_19_3_LastSeenSignaturesRoundTrip()
    {
        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 1, Signature: Sig(0x44), SignedContent: "x",
            TimestampMillis: 1L, Salt: 2L, UnsignedContent: null, ChatTypeId: 0,
            SenderName: Component.Text("A"), TargetName: null)
        {
            // Mixed packing: a full inline signature and a signature-cache reference, which is what a steady-state server actually sends once it has seen a signature before.
            LastSeen =
            [
                PackedMessageSignature.Full(Sig(0xAA)),
                PackedMessageSignature.Cached(6),
            ],
        };

        ClientboundPlayerChatPacket back = CodecRoundTrip.Cycle(ChatCodecs.V1_19_3, packet);
        Assert.Equal(2, back.LastSeen.Count);
        Assert.Equal(PackedMessageSignature.FullSignatureId, back.LastSeen[0].Id);
        Assert.Equal(256, back.LastSeen[0].FullSignature!.Length);
        Assert.Equal(6, back.LastSeen[1].Id);
        Assert.Null(back.LastSeen[1].FullSignature);
    }

    /// <summary>The v3 packed last-seen list is capped at 20 entries. Reading an uncapped VarInt count would let a hostile or broken server make this side allocate an arbitrarily large array before parsing even starts. 21 entries (one past the cap) must be rejected on decode.</summary>
    [Fact]
    public void PlayerChat_V1_19_3_LastSeenBeyondVanillaCap_ThrowsProtocolViolation()
    {
        var entries = new PackedMessageSignature[21];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = PackedMessageSignature.Full(Sig((byte)i));

        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 1, Signature: Sig(0x55), SignedContent: "x",
            TimestampMillis: 1L, Salt: 2L, UnsignedContent: null, ChatTypeId: 0,
            SenderName: Component.Text("A"), TargetName: null)
        {
            LastSeen = entries,
        };

        // Encoding carries no cap of its own, so it has to succeed here for the decode side's rejection to mean anything: this proves a malformed/hostile FRAME is rejected, not merely that this codec never produces one itself.
        byte[] wire = CodecRoundTrip.Encode(ChatCodecs.V1_19_3, packet);

        Assert.Throws<ProtocolViolationException>(() => CodecRoundTrip.Decode(ChatCodecs.V1_19_3, wire));
    }

    /// <summary>Exactly at the cap (20 entries) must still decode: the cap is inclusive, matching vanilla's <c>limitValue</c> semantics.</summary>
    [Fact]
    public void PlayerChat_V1_19_3_LastSeenAtVanillaCap_RoundTrips()
    {
        var entries = new PackedMessageSignature[20];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = PackedMessageSignature.Full(Sig((byte)i));

        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 1, Signature: Sig(0x56), SignedContent: "x",
            TimestampMillis: 1L, Salt: 2L, UnsignedContent: null, ChatTypeId: 0,
            SenderName: Component.Text("A"), TargetName: null)
        {
            LastSeen = entries,
        };

        ClientboundPlayerChatPacket back = CodecRoundTrip.Cycle(ChatCodecs.V1_19_3, packet);
        Assert.Equal(20, back.LastSeen.Count);
    }

    /// <summary>The same cap, v2 case: v2's own window is capped at 5 entries (not v3's later 20-entry cache window), matching this codebase's existing <c>LastSeenMessagesCollector.Window1_19</c>. The wire comment on <c>WriteLastSeenV2</c> already documents "capped at 5 entries by the wire itself"; this pins the read side actually enforcing it.</summary>
    [Fact]
    public void PlayerChat_V1_19_1_LastSeenBeyondVanillaCap_ThrowsProtocolViolation()
    {
        var entries = new PackedMessageSignature[6];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = PackedMessageSignature.Full(Guid.NewGuid(), Sig((byte)i));

        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: Sig(0x57), SignedContent: "hello v2",
            TimestampMillis: 1_700_000_000_000L, Salt: 1L,
            UnsignedContent: null, ChatTypeId: 1, SenderName: Component.Text("Steve"), TargetName: null)
        {
            LastSeen = entries,
        };

        byte[] wire = CodecRoundTrip.Encode(ChatCodecs.V1_19_1, packet);

        Assert.Throws<ProtocolViolationException>(() => CodecRoundTrip.Decode(ChatCodecs.V1_19_1, wire));
    }
}
