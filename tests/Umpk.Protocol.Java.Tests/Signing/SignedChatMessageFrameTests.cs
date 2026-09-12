using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>
/// Byte-anchored pins for the v1 (1.19, protocol 759) and v2 (1.19.1/1.19.2, protocol 760) OUTBOUND signed chat and chat-command frames.
/// <para>On v2 the acknowledged window is folded into the signed body, so a client that signs over a non-empty window and then declares an empty one on the wire produces a message the server cannot re-derive and rejects. Every pin below therefore uses a NON-EMPTY window and a NON-EMPTY signature: an empty list and an absent optional encode identically under either framing.</para>
/// <para>The v1 field order is a message string capped at 256 characters, an epoch-millis long, a salt long, a length-prefixed signature byte array, then a signed-preview boolean. The v2 acknowledgment block uses the same list-and-optional-entry shape as the clientbound v2 frames.</para>
/// </summary>
public sealed class SignedChatMessageFrameTests
{
    private static readonly Guid Alex = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Steve = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");

    private const long TimestampMillis = 1_700_000_000_000L;
    private const long Salt = 0x0102_0304_0506_0708L;

    private static byte[] Signature(byte fill) => [.. Enumerable.Repeat(fill, 256)];

    private static byte[] Build(Action<PacketWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        write(w);
        return buffer.WrittenSpan.ToArray();
    }

    // v1 chat, protocol 759.
    [Fact]
    public void SignedChatV1_19_PinnedFrame()
    {
        byte[] signature = Signature(0x5A);
        var packet = new ServerboundSignedChatPacket(
            "hello 1.19", TimestampMillis, Salt, signature, new LastSeenMessagesUpdate(0, new byte[3], 0));

        byte[] expected = Build(w =>
        {
            w.WriteString("hello 1.19", 256);   // capped message string
            w.WriteLong(TimestampMillis);       // epoch-millisecond timestamp
            w.WriteLong(Salt);                  // signature salt
            w.WriteVarInt(signature.Length);    // signature byte length
            w.WriteBytes(signature);
            w.WriteBool(false);                 // signed-preview flag
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCodecs.SignedV1_19, packet));

        ServerboundSignedChatPacket decoded = CodecRoundTrip.Decode(ChatCodecs.SignedV1_19, expected);
        Assert.Equal("hello 1.19", decoded.Message);
        Assert.Equal(TimestampMillis, decoded.TimestampMillis);
        Assert.Equal(Salt, decoded.Salt);
        Assert.Equal(signature, decoded.Signature);

        // v1 has no acknowledgment at all: the v2 frame appends one, so the two must differ.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(ChatCodecs.SignedV1_19_1, packet));
    }

    /// <summary>The frame the era registrar actually selects at 759 is the v1 one.</summary>
    [Fact]
    public void SignedChat_At759_ResolvesTheV1Frame()
    {
        byte[] signature = Signature(0x5A);
        var packet = new ServerboundSignedChatPacket(
            "hello 1.19", TimestampMillis, Salt, signature, new LastSeenMessagesUpdate(0, new byte[3], 0));

        BoundPacketCodec bound = BoundCodec.At(759, PacketFlow.Serverbound, "chat");
        Assert.Equal(CodecRoundTrip.Encode(ChatCodecs.SignedV1_19, packet), bound.Encode(packet));
    }

    // v2 chat, protocol 760: the v1 fields plus the acknowledgment block.
    [Fact]
    public void SignedChatV1_19_1_PinnedFrame_CarriesTheLastSeenWindow()
    {
        byte[] signature = Signature(0x5A);
        byte[] alexSignature = Signature(0x11);
        byte[] steveSignature = Signature(0x22);
        byte[] receivedSignature = Signature(0x33);

        var packet = new ServerboundSignedChatPacket(
            "hello 1.19.2", TimestampMillis, Salt, signature, new LastSeenMessagesUpdate(0, new byte[3], 0))
        {
            LegacyLastSeen =
            [
                new LastSeenMessageEntry(Alex, alexSignature),
                new LastSeenMessageEntry(Steve, steveSignature),
            ],
            LegacyLastReceived = new LastSeenMessageEntry(Steve, receivedSignature),
        };

        byte[] expected = Build(w =>
        {
            w.WriteString("hello 1.19.2", 256);
            w.WriteLong(TimestampMillis);
            w.WriteLong(Salt);
            w.WriteVarInt(signature.Length);
            w.WriteBytes(signature);
            w.WriteBool(false);                  // signedPreview

            // LastSeenMessages: VarInt count, then per entry uuid + VarInt-prefixed signature.
            w.WriteVarInt(2);
            w.WriteUuid(Alex);
            w.WriteVarInt(alexSignature.Length);
            w.WriteBytes(alexSignature);
            w.WriteUuid(Steve);
            w.WriteVarInt(steveSignature.Length);
            w.WriteBytes(steveSignature);

            // Optional last-received entry.
            w.WriteBool(true);
            w.WriteUuid(Steve);
            w.WriteVarInt(receivedSignature.Length);
            w.WriteBytes(receivedSignature);
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCodecs.SignedV1_19_1, packet));

        ServerboundSignedChatPacket decoded = CodecRoundTrip.Decode(ChatCodecs.SignedV1_19_1, expected);
        Assert.Equal(2, decoded.LegacyLastSeen.Count);
        Assert.Equal(Alex, decoded.LegacyLastSeen[0].ProfileId);
        Assert.Equal(alexSignature, decoded.LegacyLastSeen[0].Signature);
        Assert.Equal(Steve, decoded.LegacyLastSeen[1].ProfileId);
        Assert.Equal(steveSignature, decoded.LegacyLastSeen[1].Signature);
        Assert.Equal(Steve, decoded.LegacyLastReceived!.ProfileId);
        Assert.Equal(receivedSignature, decoded.LegacyLastReceived.Signature);

        // Re-encoding the decoded frame preserves the two entries byte-exactly.
        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCodecs.SignedV1_19_1, decoded));
    }

    /// <summary>The window is not optional decoration: the same packet with and without entries must produce different bytes so a populated signed window cannot travel beside an empty declaration.</summary>
    [Fact]
    public void SignedChatV1_19_1_EmptyAndPopulatedWindows_ProduceDifferentFrames()
    {
        byte[] signature = Signature(0x5A);
        var empty = new ServerboundSignedChatPacket(
            "hi", TimestampMillis, Salt, signature, new LastSeenMessagesUpdate(0, new byte[3], 0));
        ServerboundSignedChatPacket populated = empty with
        {
            LegacyLastSeen = [new LastSeenMessageEntry(Alex, Signature(0x11))],
        };

        Assert.NotEqual(
            CodecRoundTrip.Encode(ChatCodecs.SignedV1_19_1, empty),
            CodecRoundTrip.Encode(ChatCodecs.SignedV1_19_1, populated));
    }

    [Fact]
    public void SignedChat_At760_ResolvesTheV2Frame()
    {
        byte[] signature = Signature(0x5A);
        var packet = new ServerboundSignedChatPacket(
            "hello", TimestampMillis, Salt, signature, new LastSeenMessagesUpdate(0, new byte[3], 0))
        {
            LegacyLastSeen = [new LastSeenMessageEntry(Alex, Signature(0x11))],
        };

        BoundPacketCodec bound = BoundCodec.At(760, PacketFlow.Serverbound, "chat");
        Assert.Equal(CodecRoundTrip.Encode(ChatCodecs.SignedV1_19_1, packet), bound.Encode(packet));
    }

    // v2 chat_command, protocol 760: the same acknowledgment block after the argument signatures.
    [Fact]
    public void SignedChatCommandV1_19_1_PinnedFrame_CarriesTheLastSeenWindow()
    {
        byte[] argumentSignature = Signature(0x7E);
        byte[] alexSignature = Signature(0x11);

        var packet = new ServerboundSignedChatCommandPacket(
            "msg Steve hello", TimestampMillis, Salt,
            [new SignedCommandArgument("message", argumentSignature)],
            new LastSeenMessagesUpdate(0, new byte[3], 0))
        {
            LegacyLastSeen = [new LastSeenMessageEntry(Alex, alexSignature)],
        };

        byte[] expected = Build(w =>
        {
            w.WriteString("msg Steve hello", 256);   // writeUtf(command, 256)
            w.WriteLong(TimestampMillis);            // writeInstant
            w.WriteLong(Salt);                       // writeLong(salt), hoisted out of the block at 760
            w.WriteVarInt(1);                        // ArgumentSignatures: collection count
            w.WriteString("message", 16);            // entry name, MAX_ARGUMENT_NAME_LENGTH = 16
            w.WriteVarInt(argumentSignature.Length); // entry signature is length-prefixed before 1.19.3
            w.WriteBytes(argumentSignature);
            w.WriteBool(false);                      // writeBoolean(signedPreview)
            w.WriteVarInt(1);                        // LastSeenMessages count
            w.WriteUuid(Alex);
            w.WriteVarInt(alexSignature.Length);
            w.WriteBytes(alexSignature);
            w.WriteBool(false);                      // no last-received entry
        });

        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCommandCodecs.SignedV1_19_1, packet));

        ServerboundSignedChatCommandPacket decoded =
            CodecRoundTrip.Decode(ChatCommandCodecs.SignedV1_19_1, expected);
        Assert.Equal("msg Steve hello", decoded.Command);
        Assert.Equal("message", Assert.Single(decoded.ArgumentSignatures).Name);
        Assert.Equal(Alex, Assert.Single(decoded.LegacyLastSeen).ProfileId);
        Assert.Equal(alexSignature, decoded.LegacyLastSeen[0].Signature);
        Assert.Null(decoded.LegacyLastReceived);
        Assert.Equal(expected, CodecRoundTrip.Encode(ChatCommandCodecs.SignedV1_19_1, decoded));

        // Differential vs 759, which has no acknowledgment block at all.
        Assert.NotEqual(expected, CodecRoundTrip.Encode(ChatCommandCodecs.SignedV1_19, packet));
    }

    [Fact]
    public void ChatCommand_At760_ResolvesTheV2Frame()
    {
        var packet = new ServerboundSignedChatCommandPacket(
            "seed", TimestampMillis, Salt,
            [new SignedCommandArgument("message", Signature(0x7E))],
            new LastSeenMessagesUpdate(0, new byte[3], 0))
        {
            LegacyLastSeen = [new LastSeenMessageEntry(Alex, Signature(0x11))],
        };

        BoundPacketCodec bound = BoundCodec.At(760, PacketFlow.Serverbound, "chat_command");
        Assert.Equal(CodecRoundTrip.Encode(ChatCommandCodecs.SignedV1_19_1, packet), bound.Encode(packet));
    }
}
