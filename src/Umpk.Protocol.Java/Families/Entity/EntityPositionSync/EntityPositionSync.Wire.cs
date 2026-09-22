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

    /// <summary>26.3 entity position sync (protocol 777): VarInt id, position path (ordinal 0 linear: three doubles; ordinal 1 stepped: VarInt count then three doubles plus a VarInt tick offset per knot), float yaw, float pitch, on-ground bool.</summary>
    public static readonly PacketCodec<ClientboundEntityPositionSyncPacket> EntityPositionSyncV26_3 =
        PacketCodec<ClientboundEntityPositionSyncPacket>.Of(
            static (ref PacketWriter w, ClientboundEntityPositionSyncPacket p, PacketCodecContext _) =>
            {
                if (p.Path is null)
                    throw new ProtocolViolationException(
                        "26.3 entity_position_sync requires the position path; a bare position/move/rotation has no wire form on this era.");

                w.WriteVarInt(p.EntityId);
                WritePositionPath(ref w, p.Path);
                w.WriteFloat(p.Values.YRot);
                w.WriteFloat(p.Values.XRot);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                EntityPositionPath path = ReadPositionPath(ref r);
                float yaw = r.ReadFloat();
                float pitch = r.ReadFloat();
                bool onGround = r.ReadBool();
                Vec3d destination = path switch
                {
                    EntityPositionPath.Linear linear => linear.Position,
                    EntityPositionPath.Stepped stepped when stepped.Steps.Count > 0 =>
                        stepped.Steps[^1].Position,
                    _ => new Vec3d(0, 0, 0),
                };
                return new ClientboundEntityPositionSyncPacket(
                    id, new PositionMoveRotation(destination, new Vec3d(0, 0, 0), yaw, pitch), onGround)
                {
                    Path = path,
                };
            },
            WireShape.Of("varint,position_path,float,float,bool"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareEntityPositionSync(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.EntityPositionSync)
            .From(JavaEras.WideIds, EntityMoveCodecs.EntityPositionSync)
            .From(JavaProtocols.V26_3, EntityMoveCodecs.EntityPositionSyncV26_3);
    }
}
