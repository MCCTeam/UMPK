using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChatCommandCodecs
{
    /// <summary>The 1.19-1.20.4 command-string cap.</summary>
    private const int LegacyMaxCommandChars = 256;

    /// <summary>1.19 (protocol 759): command string capped at 256 characters, timestamp, then a signature block containing the salt and a VarInt-counted map of UTF-8 names to VarInt-prefixed signatures, then the signed-preview boolean. An offline client never requests a preview, so encode writes false; decode consumes the flag frame-exactly without retaining it (the packet model carries no preview field, exactly as <see cref="ChatCodecs.SignedV1_19"/> does for chat).</summary>
    public static readonly PacketCodec<ServerboundSignedChatCommandPacket> SignedV1_19 =
        PacketCodec<ServerboundSignedChatCommandPacket>.Of(
            static (ref PacketWriter w, ServerboundSignedChatCommandPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Command, LegacyMaxCommandChars);
                w.WriteLong(p.TimestampMillis);
                w.WriteLong(p.Salt);
                WriteVariableWidthArguments(ref w, p.ArgumentSignatures);
                w.WriteBool(false);
            },
            static (ref PacketReader r, PacketCodecContext ctx) =>
            {
                string command = r.ReadString(LegacyMaxCommandChars);
                long timestamp = r.ReadLong();
                long salt = r.ReadLong();
                SignedCommandArgument[] arguments = ReadVariableWidthArguments(ref r);
                _ = r.ReadBool();
                return new ServerboundSignedChatCommandPacket(
                    command, timestamp, salt, arguments, EmptyAck());
            });

    /// <summary>1.19.1/1.19.2 (protocol 760). The 1.19 field order with the salt hoisted out of the signature block, then the trailing v2 acknowledgment: a collection (limit 5) of (uuid, VarInt-prefixed signature) entries plus an optional last-received entry. The entries are CARRIED, not discarded: on this era each argument signature is computed over the acknowledged window, so an empty list beside a signature built from a non-empty one is rejected. The block is the same one <see cref="ChatCodecs.SignedV1_19_1"/> writes for chat, so both share <see cref="ChatCodecs.WriteLegacyAcknowledgment"/>.</summary>
    public static readonly PacketCodec<ServerboundSignedChatCommandPacket> SignedV1_19_1 =
        PacketCodec<ServerboundSignedChatCommandPacket>.Of(
            static (ref PacketWriter w, ServerboundSignedChatCommandPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Command, LegacyMaxCommandChars);
                w.WriteLong(p.TimestampMillis);
                w.WriteLong(p.Salt);
                WriteVariableWidthArguments(ref w, p.ArgumentSignatures);
                w.WriteBool(false);
                ChatCodecs.WriteLegacyAcknowledgment(ref w, p.LegacyLastSeen, p.LegacyLastReceived);
            },
            static (ref PacketReader r, PacketCodecContext ctx) =>
            {
                string command = r.ReadString(LegacyMaxCommandChars);
                long timestamp = r.ReadLong();
                long salt = r.ReadLong();
                SignedCommandArgument[] arguments = ReadVariableWidthArguments(ref r);
                _ = r.ReadBool();
                (LastSeenMessageEntry[] lastSeen, LastSeenMessageEntry? lastReceived) =
                    ChatCodecs.ReadLegacyAcknowledgment(ref r);

                return new ServerboundSignedChatCommandPacket(
                    command, timestamp, salt, arguments, EmptyAck())
                {
                    LegacyLastSeen = lastSeen,
                    LegacyLastReceived = lastReceived,
                };
            });

    /// <summary>1.19.3-1.20.4 (protocols 761-765): command string capped at 256 characters, timestamp, salt, the fixed-width argument signatures, then the last-seen update (VarInt offset + fixed 20-bit bitset). No preview flag and no checksum byte.</summary>
    public static readonly PacketCodec<ServerboundSignedChatCommandPacket> SignedV1_19_3 =
        PacketCodec<ServerboundSignedChatCommandPacket>.Of(
            static (ref PacketWriter w, ServerboundSignedChatCommandPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Command, LegacyMaxCommandChars);
                w.WriteLong(p.TimestampMillis);
                w.WriteLong(p.Salt);
                WriteFixedWidthArguments(ref w, p.ArgumentSignatures);
                WriteAck(ref w, p.LastSeen, hasChecksum: false);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string command = r.ReadString(LegacyMaxCommandChars);
                long timestamp = r.ReadLong();
                long salt = r.ReadLong();
                SignedCommandArgument[] arguments = ReadFixedWidthArguments(ref r);
                return new ServerboundSignedChatCommandPacket(
                    command, timestamp, salt, arguments, ReadAck(ref r, hasChecksum: false));
            });

    /// <summary>1.20.5+ unsigned <c>chat_command</c> (protocol 766 onward): the command string alone, written with the default 32767-character limit, not the 256-character cap the pre-split packet used.</summary>
    public static readonly PacketCodec<ServerboundChatCommandPacket> UnsignedV1_20_5 =
        PacketCodec<ServerboundChatCommandPacket>.Of(
            static (ref PacketWriter w, ServerboundChatCommandPacket p, PacketCodecContext _) =>
                w.WriteString(p.Command, MaxCommandChars),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundChatCommandPacket(r.ReadString(MaxCommandChars)));

    // shared field writers

    // 1.19 / 1.19.1: the entry signature is a VarInt-prefixed byte array of whatever length the key produced. 1.19 uses a map and 1.19.1 uses a collection, but both emit the same VarInt count and per-entry bytes, so one helper covers both.
    private static void WriteVariableWidthArguments(ref PacketWriter w, IReadOnlyList<SignedCommandArgument> arguments)
    {
        w.WriteVarInt(arguments.Count);
        foreach (SignedCommandArgument argument in arguments)
        {
            w.WriteString(argument.Name, MaxArgumentNameChars);
            w.WriteByteArray(argument.Signature);
        }
    }

    private static SignedCommandArgument[] ReadVariableWidthArguments(ref PacketReader r)
    {
        int count = r.ReadVarInt();
        var arguments = new SignedCommandArgument[count];
        for (int i = 0; i < count; i++)
        {
            string name = r.ReadString(MaxArgumentNameChars);
            arguments[i] = new SignedCommandArgument(name, r.ReadByteArray().ToArray());
        }

        return arguments;
    }

    private static LastSeenMessagesUpdate EmptyAck() =>
        new(0, new byte[AckBitsetBytes], 0);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareChatCommand(PacketBindings bindings)
    {
        // From 1.19 onward, commands must use chat_command or chat_command_signed; ordinary chat only broadcasts text. Era boundaries: 759 folds the salt into the argument-signature block and has no ack window; 760 hoists the salt out and appends the v2 last-seen update; 761 drops the preview flag, widens signatures to fixed 256 bytes and switches to the offset/bitset window; 766 SPLITS the packet, leaving chat_command as the bare command string and moving the signed payload to chat_command_signed; 770 adds the ack checksum byte.
        bindings.Packet(PlayPackets.Serverbound.ChatCommand)
            .FromAs(JavaProtocols.V1_19, PlayPackets.Serverbound.SignedChatCommand, ChatCommandCodecs.SignedV1_19)
            .FromAs(JavaProtocols.V1_19_1, PlayPackets.Serverbound.SignedChatCommand, ChatCommandCodecs.SignedV1_19_1)
            .FromAs(JavaProtocols.V1_19_3, PlayPackets.Serverbound.SignedChatCommand, ChatCommandCodecs.SignedV1_19_3)
            .From(JavaProtocols.V1_20_5, ChatCommandCodecs.UnsignedV1_20_5);
    }
}
