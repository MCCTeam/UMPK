using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChatCodecs
{
    /// <summary>1.19.3+ serverbound standalone chat acknowledgement (<c>minecraft:chat_ack</c>): a single VarInt offset advancing the server's last-seen count.</summary>
    public static readonly PacketCodec<ServerboundChatAckPacket> ChatAckV1_19_3 =
        PacketCodec<ServerboundChatAckPacket>.Of(
            static (ref PacketWriter w, ServerboundChatAckPacket p, PacketCodecContext _) => w.WriteVarInt(p.Offset),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundChatAckPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareChatAck(PacketBindings bindings)
    {
        // The v2 body is a last-seen collection followed by an optional entry. The per-entry sender UUID that body needs is the one ChatCodecs.ReadLastSeenV2 discards.
        bindings.Packet(PlayPackets.Serverbound.ChatAck)
            .MarkerFrom(
                JavaProtocols.V1_19_1,
                MarkerReason.WrongCodecWouldBeWorse,
                "The 1.19.1 acknowledgement body is a whole last-seen update, a counted list of profile id and signature entries plus an optional trailing entry, while the codec bound from 1.19.3 writes a bare VarInt offset. The chat model discards the per-entry sender id that body needs, so it has to grow before this can bind at all.")
            .From(JavaProtocols.V1_19_3, ChatCodecs.ChatAckV1_19_3);
    }
}
