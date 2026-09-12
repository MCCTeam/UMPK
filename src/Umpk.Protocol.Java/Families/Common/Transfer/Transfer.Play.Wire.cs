using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class PlayCommonCodecs
{
    /// <summary>Play-phase transfer: host string plus VarInt port.</summary>
    public static readonly PacketCodec<ClientboundTransferPacket> Transfer =
        PacketCodec<ClientboundTransferPacket>.Of(
            static (ref PacketWriter w, ClientboundTransferPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteTransfer(ref w, p.Host, p.Port),
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                (string host, int port) = CommonPayloads.ReadTransfer(ref r);
                return new ClientboundTransferPacket(host, port);
            },
            WireShape.Of("string,varint"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareTransferPlay(PacketBindings bindings)
    {
        bindings.Packet(PlayPackets.Clientbound.Transfer)
            .From(JavaEras.ItemComponents, PlayCommonCodecs.Transfer);
    }
}
