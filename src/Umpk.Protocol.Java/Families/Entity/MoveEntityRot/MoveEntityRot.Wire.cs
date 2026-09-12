using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityMoveCodecs
{
    /// <summary>Rotation only (byte yaw/pitch), identical shape across eras.</summary>
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

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMoveEntityRot(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.MoveEntityRot)
            .From(JavaProtocols.V1_8, EntityMoveCodecs.MoveEntityRot)
            .AliasedAs(Identifier.Minecraft("move_entity_rotation"));
    }
}
