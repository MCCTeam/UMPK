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
    /// <summary>Set border size: one double.</summary>
    public static readonly PacketCodec<ClientboundSetBorderSizePacket> SetBorderSizeV1_17 =
        PacketCodec<ClientboundSetBorderSizePacket>.Of(
            static (ref PacketWriter w, ClientboundSetBorderSizePacket p, PacketCodecContext _) => w.WriteDouble(p.Size),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetBorderSizePacket(r.ReadDouble()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetBorderSize(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.SetBorderSize)
            .From(JavaEras.Caves, WorldBorderCodecs.SetBorderSizeV1_17);
    }
}
