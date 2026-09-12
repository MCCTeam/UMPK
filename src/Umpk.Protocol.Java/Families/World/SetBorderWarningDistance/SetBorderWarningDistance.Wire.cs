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
    /// <summary>Set border warning distance: a VarInt.</summary>
    public static readonly PacketCodec<ClientboundSetBorderWarningDistancePacket> SetBorderWarningDistanceV1_17 =
        PacketCodec<ClientboundSetBorderWarningDistancePacket>.Of(
            static (ref PacketWriter w, ClientboundSetBorderWarningDistancePacket p, PacketCodecContext _) => w.WriteVarInt(p.WarningBlocks),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetBorderWarningDistancePacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetBorderWarningDistance(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.SetBorderWarningDistance)
            .From(JavaEras.Caves, WorldBorderCodecs.SetBorderWarningDistanceV1_17);
    }
}
