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

    /// <summary>26.3 accept teleportation: VarInt id, three doubles, two floats, in that order.</summary>
    public static readonly PacketCodec<ServerboundAcceptTeleportationPacket> AcceptTeleportationV26_3 =
        PacketCodec<ServerboundAcceptTeleportationPacket>.Of(
            static (ref PacketWriter w, ServerboundAcceptTeleportationPacket p, PacketCodecContext _) =>
            {
                if (!p.HasDestination)
                    throw new ProtocolViolationException(
                        "26.3 accept_teleportation requires the echoed destination; a bare teleport id has no wire form on this era.");

                w.WriteVarInt(p.TeleportId);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteFloat(p.YRot);
                w.WriteFloat(p.XRot);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundAcceptTeleportationPacket(
                r.ReadVarInt(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadFloat(), r.ReadFloat()),
            WireShape.Of("varint,double,double,double,float,float"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAcceptTeleportation(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.AcceptTeleportation)
            .From(JavaEras.Combat, EntityServerboundCodecs.AcceptTeleportation)
            .From(JavaProtocols.V26_3, EntityServerboundCodecs.AcceptTeleportationV26_3);
    }
}
