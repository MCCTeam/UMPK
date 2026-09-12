using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityMoveCodecs
{
    /// <summary>1.8 relative move (byte deltas).</summary>
    public static readonly PacketCodec<ClientboundMoveEntityPosPacket> MoveEntityPosV1_8 =
        PacketCodec<ClientboundMoveEntityPosPacket>.Of(
            static (ref PacketWriter w, ClientboundMoveEntityPosPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteSByte((sbyte)p.DeltaX);
                w.WriteSByte((sbyte)p.DeltaY);
                w.WriteSByte((sbyte)p.DeltaZ);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundMoveEntityPosPacket(r.ReadVarInt(), r.ReadSByte(), r.ReadSByte(), r.ReadSByte(), r.ReadBool()));

    /// <summary>Modern relative move (short deltas).</summary>
    public static readonly PacketCodec<ClientboundMoveEntityPosPacket> MoveEntityPosV1_9 =
        PacketCodec<ClientboundMoveEntityPosPacket>.Of(
            static (ref PacketWriter w, ClientboundMoveEntityPosPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteShort(p.DeltaX);
                w.WriteShort(p.DeltaY);
                w.WriteShort(p.DeltaZ);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundMoveEntityPosPacket(r.ReadVarInt(), r.ReadShort(), r.ReadShort(), r.ReadShort(), r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMoveEntityPos(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.MoveEntityPos)
            .From(JavaProtocols.V1_8, EntityMoveCodecs.MoveEntityPosV1_8)
            .From(JavaProtocols.V1_9, EntityMoveCodecs.MoveEntityPosV1_9)
            .AliasedAs(Identifier.Minecraft("move_entity_position"));
    }
}
