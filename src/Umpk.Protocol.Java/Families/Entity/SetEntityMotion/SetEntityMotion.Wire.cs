using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityMoveCodecs
{
    /// <summary>Velocity (1.8 / 1.21.5): entity id VarInt, three short components.</summary>
    public static readonly PacketCodec<ClientboundSetEntityMotionPacket> SetEntityMotion =
        PacketCodec<ClientboundSetEntityMotionPacket>.Of(
            static (ref PacketWriter w, ClientboundSetEntityMotionPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteShort(p.VelocityX);
                w.WriteShort(p.VelocityY);
                w.WriteShort(p.VelocityZ);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetEntityMotionPacket(r.ReadVarInt(), r.ReadShort(), r.ReadShort(), r.ReadShort()));

    /// <summary>Velocity (1.21.9+): entity id VarInt, then the low-precision quantized velocity block (<c>Vec3.LP_STREAM_CODEC</c> / <c>LpVec3</c>), which is variable length (a single 0 byte for the zero vector, otherwise 6 bytes plus an optional trailing VarInt scale). The block is carried raw so the frame round-trips byte-exactly. The LpVec3 switch landed at 1.21.9, not 26.1, so protocols 773/774/775/776 all share this shape.</summary>
    public static readonly PacketCodec<ClientboundSetEntityMotionPacket> SetEntityMotionV1_21_9 =
        PacketCodec<ClientboundSetEntityMotionPacket>.Of(
            static (ref PacketWriter w, ClientboundSetEntityMotionPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteBytes(p.ModernVelocityRaw ?? [0]);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                byte[] lp = ReadLpVec3(ref r);
                return new ClientboundSetEntityMotionPacket(id, 0, 0, 0, lp);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetEntityMotion(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.SetEntityMotion)
            .From(JavaProtocols.V1_8, EntityMoveCodecs.SetEntityMotion)
            .From(JavaProtocols.V1_21_9, EntityMoveCodecs.SetEntityMotionV1_21_9);
    }
}
