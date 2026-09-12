using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>Accept teleportation (modern): a VarInt teleport id.</summary>
    public static readonly PacketCodec<ServerboundAcceptTeleportationPacket> AcceptTeleportation =
        PacketCodec<ServerboundAcceptTeleportationPacket>.Of(
            static (ref PacketWriter w, ServerboundAcceptTeleportationPacket p, PacketCodecContext _) => w.WriteVarInt(p.TeleportId),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundAcceptTeleportationPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAcceptTeleportation(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.AcceptTeleportation)
            .From(JavaEras.Combat, EntityServerboundCodecs.AcceptTeleportation);
    }
}
