using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityMoveCodecs
{
    /// <summary>Rotation only through 26.2: entity id, byte yaw and pitch, then ground flag.</summary>
    public static readonly PacketCodec<ClientboundMoveEntityRotPacket> MoveEntityRot =
        PacketCodec<ClientboundMoveEntityRotPacket>.Of(
            static (ref PacketWriter w, ClientboundMoveEntityRotPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundMoveEntityRotPacket(r.ReadVarInt(), r.ReadAngle(), r.ReadAngle(), r.ReadBool()));

    /// <summary>26.3 rotation only: entity id, ground flag, then byte yaw and pitch.</summary>
    // 26.3-client-decompiled ClientboundMoveEntityPacket.java:158-170 moves the ground flag before the angles; 26.2-client-decompiled ClientboundMoveEntityPacket.java:170-183 retains the earlier order.
    public static readonly PacketCodec<ClientboundMoveEntityRotPacket> MoveEntityRotV26_3 =
        PacketCodec<ClientboundMoveEntityRotPacket>.Of(
            static (ref PacketWriter w, ClientboundMoveEntityRotPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteBool(p.OnGround);
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                bool onGround = r.ReadBool();
                return new ClientboundMoveEntityRotPacket(id, r.ReadAngle(), r.ReadAngle(), onGround);
            },
            WireShape.Of("varint,bool,angle,angle"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMoveEntityRot(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.MoveEntityRot)
            .From(JavaProtocols.V1_8, EntityMoveCodecs.MoveEntityRot)
            .From(JavaProtocols.V26_3, EntityMoveCodecs.MoveEntityRotV26_3)
            .AliasedAs(Identifier.Minecraft("move_entity_rotation"));
    }
}
