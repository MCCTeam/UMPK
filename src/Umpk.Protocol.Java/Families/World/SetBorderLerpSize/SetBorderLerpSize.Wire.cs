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
    /// <summary>Set border lerp size: old double, new double, VarLong lerp time.</summary>
    public static readonly PacketCodec<ClientboundSetBorderLerpSizePacket> SetBorderLerpSizeV1_17 =
        PacketCodec<ClientboundSetBorderLerpSizePacket>.Of(
            static (ref PacketWriter w, ClientboundSetBorderLerpSizePacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.OldSize);
                w.WriteDouble(p.NewSize);
                w.WriteVarLong(p.LerpTime);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetBorderLerpSizePacket(r.ReadDouble(), r.ReadDouble(), r.ReadVarLong()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetBorderLerpSize(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.SetBorderLerpSize)
            .From(JavaEras.Caves, WorldBorderCodecs.SetBorderLerpSizeV1_17);
    }
}
