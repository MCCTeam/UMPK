using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityMoveCodecs
{
    /// <summary>Entity position sync (1.21.2+): id, PositionMoveRotation, on-ground.</summary>
    public static readonly PacketCodec<ClientboundEntityPositionSyncPacket> EntityPositionSync =
        PacketCodec<ClientboundEntityPositionSyncPacket>.Of(
            static (ref PacketWriter w, ClientboundEntityPositionSyncPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                WritePositionMoveRotation(ref w, p.Values);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundEntityPositionSyncPacket(r.ReadVarInt(), ReadPositionMoveRotation(ref r), r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareEntityPositionSync(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.EntityPositionSync)
            .From(JavaEras.WideIds, EntityMoveCodecs.EntityPositionSync);
    }
}
