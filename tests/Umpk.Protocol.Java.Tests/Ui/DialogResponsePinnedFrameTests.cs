using System.Buffers;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>
/// Byte-anchored tests for the serverbound dialog response frame (1.21.6+). A dialog answer and a dialog cancel both travel as <c>minecraft:custom_click_action</c>; these pin the frame both ways.
/// <para>The action identifier is its <c>namespace:path</c> UTF-8 string. The payload is LENGTH PREFIXED: <c>lengthPrefixed</c> writes a VarInt byte length then the inner body, which is unnamed-root network NBT and encodes "absent" as a bare TAG_End (0x00) rather than a boolean prefix.</para>
/// <para>The prefix is the regression guard: without it a live server rejects every dialog click after reporting four extra bytes. Note that an ABSENT payload is length 1 carrying a single 0x00 byte, NOT length 0.</para>
/// </summary>
public sealed class DialogResponsePinnedFrameTests
{
    private static byte[] Build(Action<PacketWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        write(w);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] Encode(ServerboundCustomClickActionPacket packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        UiMiscCodecs.CustomClickActionV1_21_6.Encode(ref writer, packet, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }

    private static ServerboundCustomClickActionPacket Decode(byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        ServerboundCustomClickActionPacket decoded = UiMiscCodecs.CustomClickActionV1_21_6.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);
        return decoded;
    }

    // The literal-byte anchor. A cancel with no exit-action additions and no inputs carries an empty payload compound, which is the smallest complete frame the response path can produce.
    [Fact]
    public void DialogCancel_EmptyPayload_RawByteFrame()
    {
        var packet = new ServerboundCustomClickActionPacket(Identifier.Parse("a:b"), new NbtCompound());

        byte[] expected =
        [
            0x03, 0x61, 0x3A, 0x62, // writeUtf("a:b"): VarInt length 3 then 'a' ':' 'b'
            0x02,                   // lengthPrefixed(65536): VarInt payload length, 2 bytes follow
            0x0A,                   // unnamed-root NBT: TAG_Compound type byte
            0x00,                   // TAG_End: the compound has no members
        ];

        Assert.Equal(expected, Encode(packet));

        ServerboundCustomClickActionPacket decoded = Decode(expected);
        Assert.Equal(Identifier.Parse("a:b"), decoded.Id);
        Assert.Empty(Assert.IsType<NbtCompound>(decoded.Payload));
    }

    // The response frame a dialog submit produces: the action id plus the payload compound whose members are the action's fixed additions followed by the dialog's input values, in dialog order.
    [Fact]
    public void DialogSubmit_PayloadFrame_Pinned()
    {
        var payload = new NbtCompound();
        payload.PutString("source", "reward_menu"); // the button action's fixed additions
        payload.PutString("note", "hello");         // text input
        payload.PutString("subscribe", "yes");      // boolean input, via on_true
        payload.PutString("reward", "shield");      // single option, the preselected id

        var packet = new ServerboundCustomClickActionPacket(Identifier.Parse("example:claim"), payload);

        byte[] body = NbtWriter.ToArray(payload, NbtWireFormat.JavaUnnamedRoot);
        byte[] expected = Build(w =>
        {
            w.WriteString("example:claim");  // ResourceLocation
            w.WriteByteArray(body);          // lengthPrefixed(65536) around the unnamed-root tag
        });

        Assert.Equal(expected, Encode(packet));

        ServerboundCustomClickActionPacket decoded = Decode(expected);
        Assert.Equal(Identifier.Parse("example:claim"), decoded.Id);
        var decodedPayload = Assert.IsType<NbtCompound>(decoded.Payload);
        Assert.Equal(["source", "note", "subscribe", "reward"], decodedPayload.Keys);
        Assert.Equal("shield", decodedPayload.GetString("reward"));
    }

    // "Absent" is a bare TAG_End inside the length prefix, not a boolean-prefixed optional and not a zero length: a one-byte body, and the decode must map it back to null rather than to an empty compound.
    [Fact]
    public void DialogResponse_AbsentPayload_RawByteFrame()
    {
        var packet = new ServerboundCustomClickActionPacket(Identifier.Parse("example:claim"), null);

        byte[] expected = Build(w =>
        {
            w.WriteString("example:claim");
            w.WriteByteArray([0x00]); // VarInt length 1, then TAG_End marking the optional tag absent
        });

        Assert.Equal(expected, Encode(packet));
        Assert.Null(Decode(expected).Payload);
    }

    // A prefix-less frame must not round-trip through the length-prefixed codec. Decoding it either fails outright or leaves the tag bytes unread; it must never be accepted as an equivalent encoding of the same packet.
    [Fact]
    public void DialogResponse_PrefixlessFrame_IsNotAccepted()
    {
        var payload = new NbtCompound();
        payload.PutString("source", "reward_menu");

        byte[] prefixless = Build(w =>
        {
            w.WriteString("example:claim");
            w.WriteNbt(payload, NbtWireFormat.JavaUnnamedRoot); // intentionally omit the required length prefix
        });

        Assert.NotEqual(prefixless, Encode(new ServerboundCustomClickActionPacket(Identifier.Parse("example:claim"), payload)));

        var reader = new PacketReader(prefixless);
        try
        {
            UiMiscCodecs.CustomClickActionV1_21_6.Decode(ref reader, PacketCodecContext.Registryless);
        }
        catch (ProtocolViolationException)
        {
            return;
        }
        catch (NbtFormatException)
        {
            return;
        }

        Assert.NotEqual(0, reader.Remaining);
    }

    // A payload at the vanilla ceiling is rejected rather than silently truncated by the VarInt prefix.
    [Fact]
    public void DialogResponse_OverlongDeclaredLength_Throws()
    {
        byte[] frame = Build(w =>
        {
            w.WriteString("example:claim");
            w.WriteVarInt(65537); // one past the 65536-byte payload limit
            w.WriteBytes([0x00]);
        });

        var reader = new PacketReader(frame);
        try
        {
            UiMiscCodecs.CustomClickActionV1_21_6.Decode(ref reader, PacketCodecContext.Registryless);
        }
        catch (ProtocolViolationException)
        {
            return;
        }

        Assert.Fail("An over-long declared payload length must be rejected.");
    }
}
