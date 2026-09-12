using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldBorderCodecs
{
    /// <summary>Set border center: two doubles.</summary>
    public static readonly PacketCodec<ClientboundSetBorderCenterPacket> SetBorderCenterV1_17 =
        PacketCodec<ClientboundSetBorderCenterPacket>.Of(
            static (ref PacketWriter w, ClientboundSetBorderCenterPacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.CenterX);
                w.WriteDouble(p.CenterZ);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetBorderCenterPacket(r.ReadDouble(), r.ReadDouble()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetBorderCenter(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.SetBorderCenter)
            .From(JavaEras.Caves, WorldBorderCodecs.SetBorderCenterV1_17);
    }
}
