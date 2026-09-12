using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityMoveCodecs
{
    /// <summary>Head rotation (all versions): entity id VarInt, head-yaw angle byte.</summary>
    public static readonly PacketCodec<ClientboundRotateHeadPacket> RotateHead =
        PacketCodec<ClientboundRotateHeadPacket>.Of(
            static (ref PacketWriter w, ClientboundRotateHeadPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteAngle(p.HeadYaw);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundRotateHeadPacket(r.ReadVarInt(), r.ReadAngle()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRotateHead(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.RotateHead)
            .From(JavaProtocols.V1_8, EntityMoveCodecs.RotateHead);
    }
}
