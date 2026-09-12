using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>Which codec the REGISTRAR actually binds for <c>minecraft:chat_command</c> and <c>minecraft:chat_command_signed</c> at every protocol number.</summary>
/// <remarks>
/// <para>Both identifiers sat in the packet timeline as markers on every protocol that declares them (<c>chat_command</c> on 759-776, <c>chat_command_signed</c> on 766-776), so the frame had a real wire id, no codec, and no way to be sent. Nothing threw: the command send path silently fell back to writing "/cmd" down the ordinary chat wire, which from 1.19 onward is broadcast as chat text and never dispatched. Codec-level tests cannot see that; only resolving by protocol number can.</para>
/// <para>Each era assertion encodes a fixed packet through the BOUND codec and compares it against bytes built field-by-field from the era's wire order, so a timeline that resolves a neighbouring era's codec fails here rather than silently producing a frame the server rejects.</para>
/// </remarks>
public sealed class ChatCommandCodecBindingTests
{
    private const string ChatCommand = "minecraft:chat_command";
    private const string ChatCommandSigned = "minecraft:chat_command_signed";
    private const string Command = "msg Steve hi";
    private const long Timestamp = 1234567890123L;
    private const long Salt = 0x0102030405060708L;

    /// <summary>Every protocol that declares a serverbound <c>chat_command</c> (1.19 through 26.2).</summary>
    public static TheoryData<int> ChatCommandProtocols =>
        [759, 760, 761, 762, 763, 764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776];

    /// <summary>The pre-1.19 protocols, where the packet does not exist and the chat path IS the command path.</summary>
    public static TheoryData<int> PreSigningProtocols =>
        [47, 107, 110, 210, 315, 340, 393, 404, 477, 498, 573, 578, 735, 751, 754, 755, 757, 758];

    private static byte[] Signature()
    {
        var signature = new byte[256];
        for (int i = 0; i < signature.Length; i++)
            signature[i] = (byte)i;

        return signature;
    }

    private static ServerboundSignedChatCommandPacket Signed() =>
        new(Command, Timestamp, Salt,
            [new SignedCommandArgument("message", Signature())],
            new LastSeenMessagesUpdate(Offset: 3, Acknowledged: [0x05, 0x00, 0x00], Checksum: 0x2A));

    private static ServerboundChatCommandSignedPacket Standalone() =>
        new(Command, Timestamp, Salt,
            [new SignedCommandArgument("message", Signature())],
            new LastSeenMessagesUpdate(Offset: 3, Acknowledged: [0x05, 0x00, 0x00], Checksum: 0x2A));

    private static byte[] Build(Action<PacketWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        write(w);
        return buffer.WrittenSpan.ToArray();
    }

    // chat_command.

    [Theory]
    [MemberData(nameof(ChatCommandProtocols))]
    public void ChatCommand_IsImplementedOnEverySigningProtocol(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, ChatCommand);
        Assert.True(bound.IsImplemented);

        // 759-765 carry the signed payload under this identity; 766 split it out and left the bare command string here.
        Type expected = protocol < 766
            ? typeof(ServerboundSignedChatCommandPacket)
            : typeof(ServerboundChatCommandPacket);
        Assert.Equal(expected, bound.Type.PayloadType);
    }

    [Theory]
    [MemberData(nameof(PreSigningProtocols))]
    public void ChatCommand_ResolvesNothingBefore1_19(int protocol) =>
        Assert.False(BoundCodec.EntryAt(protocol, PacketFlow.Serverbound, ChatCommand).IsImplemented);

    [Fact]
    public void ChatCommand_At759_BindsTheV1Form()
    {
        byte[] signature = Signature();
        byte[] expected = Build(w =>
        {
            w.WriteString(Command, 256);
            w.WriteLong(Timestamp);
            w.WriteLong(Salt);          // 1.19 keeps the salt inside ArgumentSignatures
            w.WriteVarInt(1);
            w.WriteString("message", 16);
            w.WriteByteArray(signature);
            w.WriteBool(false);         // signedPreview, dropped at 1.19.3
        });

        Assert.Equal(expected, BoundCodec.At(759, PacketFlow.Serverbound, ChatCommand).Encode(Signed()));
    }

    [Fact]
    public void ChatCommand_At760_BindsTheV2Form_WithTheTrailingListWindow()
    {
        byte[] signature = Signature();
        byte[] expected = Build(w =>
        {
            w.WriteString(Command, 256);
            w.WriteLong(Timestamp);
            w.WriteLong(Salt);
            w.WriteVarInt(1);
            w.WriteString("message", 16);
            w.WriteByteArray(signature);
            w.WriteBool(false);   // signedPreview
            w.WriteVarInt(0);     // LastSeenMessages entries
            w.WriteBool(false);   // no last-received entry
        });

        Assert.Equal(expected, BoundCodec.At(760, PacketFlow.Serverbound, ChatCommand).Encode(Signed()));

        // The 1.19 timeline step must NOT leak forward: it stops two bytes short of the v2 frame.
        Assert.NotEqual(expected, BoundCodec.At(759, PacketFlow.Serverbound, ChatCommand).Encode(Signed()));
    }

    [Theory]
    [InlineData(761)]
    [InlineData(762)]
    [InlineData(763)]
    [InlineData(764)]
    [InlineData(765)]
    public void ChatCommand_761To765_BindTheV3Form(int protocol)
    {
        byte[] signature = Signature();
        byte[] expected = Build(w =>
        {
            w.WriteString(Command, 256);
            w.WriteLong(Timestamp);
            w.WriteLong(Salt);
            w.WriteVarInt(1);
            w.WriteString("message", 16);
            w.WriteBytes(signature);            // fixed 256 bytes, no length prefix
            w.WriteVarInt(3);                   // last-seen offset
            w.WriteBytes([0x05, 0x00, 0x00]);   // fixed 20-bit ack bitset
        });

        Assert.Equal(expected, BoundCodec.At(protocol, PacketFlow.Serverbound, ChatCommand).Encode(Signed()));
    }

    [Theory]
    [InlineData(766)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(776)]
    public void ChatCommand_766AndLater_BindTheBareUnsignedForm(int protocol)
    {
        byte[] expected = Build(w => w.WriteString(Command));

        Assert.Equal(
            expected,
            BoundCodec.At(protocol, PacketFlow.Serverbound, ChatCommand)
                .Encode(new ServerboundChatCommandPacket(Command)));
    }

    // chat_command_signed.

    [Theory]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    [InlineData(765)]
    public void ChatCommandSigned_ResolvesNothingBeforeTheSplit(int protocol) =>
        Assert.False(BoundCodec.EntryAt(protocol, PacketFlow.Serverbound, ChatCommandSigned).IsImplemented);

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    public void ChatCommandSigned_766To769_HaveNoAckChecksum(int protocol)
    {
        byte[] signature = Signature();
        byte[] expected = Build(w =>
        {
            w.WriteString(Command);
            w.WriteLong(Timestamp);
            w.WriteLong(Salt);
            w.WriteVarInt(1);
            w.WriteString("message", 16);
            w.WriteBytes(signature);
            w.WriteVarInt(3);
            w.WriteBytes([0x05, 0x00, 0x00]);
        });

        Assert.Equal(
            expected,
            BoundCodec.At(protocol, PacketFlow.Serverbound, ChatCommandSigned).Encode(Standalone()));
    }

    [Theory]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    public void ChatCommandSigned_770AndLater_AppendTheAckChecksum(int protocol)
    {
        byte[] signature = Signature();
        byte[] expected = Build(w =>
        {
            w.WriteString(Command);
            w.WriteLong(Timestamp);
            w.WriteLong(Salt);
            w.WriteVarInt(1);
            w.WriteString("message", 16);
            w.WriteBytes(signature);
            w.WriteVarInt(3);
            w.WriteBytes([0x05, 0x00, 0x00]);
            w.WriteByte(0x2A);   // checksum, added to the last-seen update in 1.21.5
        });

        byte[] actual = BoundCodec.At(protocol, PacketFlow.Serverbound, ChatCommandSigned).Encode(Standalone());
        Assert.Equal(expected, actual);

        // The pre-checksum era must not leak forward: 769 is exactly one byte shorter.
        Assert.Equal(
            actual.Length - 1,
            BoundCodec.At(769, PacketFlow.Serverbound, ChatCommandSigned).Encode(Standalone()).Length);
    }
}
