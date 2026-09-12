using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChatCommandCodecs
{
    /// <summary>1.20.5-1.21.4 <c>chat_command_signed</c> (protocols 766-769): the split-out signed payload, with the last-seen update still checksum-free.</summary>
    public static readonly PacketCodec<ServerboundChatCommandSignedPacket> SignedStandaloneV1_20_5 =
        Standalone(hasChecksum: false);

    /// <summary>1.21.5-26.2 <c>chat_command_signed</c> (protocols 770-776): the 1.20.5 body with the trailing last-seen checksum byte added in 1.21.5.</summary>
    public static readonly PacketCodec<ServerboundChatCommandSignedPacket> SignedStandaloneV1_21_5 =
        Standalone(hasChecksum: true);

    private static PacketCodec<ServerboundChatCommandSignedPacket> Standalone(bool hasChecksum) =>
        PacketCodec<ServerboundChatCommandSignedPacket>.Of(
            (ref PacketWriter w, ServerboundChatCommandSignedPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Command, MaxCommandChars);
                w.WriteLong(p.TimestampMillis);
                w.WriteLong(p.Salt);
                WriteFixedWidthArguments(ref w, p.ArgumentSignatures);
                WriteAck(ref w, p.LastSeen, hasChecksum);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string command = r.ReadString(MaxCommandChars);
                long timestamp = r.ReadLong();
                long salt = r.ReadLong();
                SignedCommandArgument[] arguments = ReadFixedWidthArguments(ref r);
                return new ServerboundChatCommandSignedPacket(
                    command, timestamp, salt, arguments, ReadAck(ref r, hasChecksum));
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareChatCommandSigned(PacketBindings bindings)
    {
        bindings.Packet(PlayPackets.Serverbound.ChatCommandSigned)
            .From(JavaEras.ItemComponents, ChatCommandCodecs.SignedStandaloneV1_20_5)
            .From(JavaEras.ModernComponents, ChatCommandCodecs.SignedStandaloneV1_21_5);
    }
}
