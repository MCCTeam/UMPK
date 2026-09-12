using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityMoveCodecs
{
    /// <summary>1.8 relative move + rotation (byte deltas, byte rotations).</summary>
    public static readonly PacketCodec<ClientboundMoveEntityPosRotPacket> MoveEntityPosRotV1_8 =
        PacketCodec<ClientboundMoveEntityPosRotPacket>.Of(
            static (ref PacketWriter w, ClientboundMoveEntityPosRotPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteSByte((sbyte)p.DeltaX);
                w.WriteSByte((sbyte)p.DeltaY);
                w.WriteSByte((sbyte)p.DeltaZ);
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundMoveEntityPosRotPacket(r.ReadVarInt(), r.ReadSByte(), r.ReadSByte(), r.ReadSByte(), r.ReadAngle(), r.ReadAngle(), r.ReadBool()));

    /// <summary>Modern relative move + rotation (short deltas, byte rotations).</summary>
    public static readonly PacketCodec<ClientboundMoveEntityPosRotPacket> MoveEntityPosRotV1_9 =
        PacketCodec<ClientboundMoveEntityPosRotPacket>.Of(
            static (ref PacketWriter w, ClientboundMoveEntityPosRotPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteShort(p.DeltaX);
                w.WriteShort(p.DeltaY);
                w.WriteShort(p.DeltaZ);
                w.WriteAngle(p.Yaw);
                w.WriteAngle(p.Pitch);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundMoveEntityPosRotPacket(r.ReadVarInt(), r.ReadShort(), r.ReadShort(), r.ReadShort(), r.ReadAngle(), r.ReadAngle(), r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMoveEntityPosRot(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.MoveEntityPosRot)
            .From(JavaProtocols.V1_8, EntityMoveCodecs.MoveEntityPosRotV1_8)
            .From(JavaProtocols.V1_9, EntityMoveCodecs.MoveEntityPosRotV1_9)
            .AliasedAs(Identifier.Minecraft("move_entity_position_rotation"));
    }
}
