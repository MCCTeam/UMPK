using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>Damage event (1.19.4+): id, damage-type id, optional cause/direct ids (-1 absent), optional position.</summary>
    public static readonly PacketCodec<ClientboundDamageEventPacket> DamageEvent =
        PacketCodec<ClientboundDamageEventPacket>.Of(
            static (ref PacketWriter w, ClientboundDamageEventPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteVarInt(p.SourceTypeId);
                w.WriteVarInt((p.SourceCauseId ?? -1) + 1);
                w.WriteVarInt((p.SourceDirectId ?? -1) + 1);
                w.WriteOptionalStruct(p.SourcePosition, static (ref PacketWriter ww, Vec3d v) =>
                {
                    ww.WriteDouble(v.X); ww.WriteDouble(v.Y); ww.WriteDouble(v.Z);
                });
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                int type = r.ReadVarInt();
                int cause = r.ReadVarInt() - 1;
                int direct = r.ReadVarInt() - 1;
                Vec3d? pos = r.ReadBool() ? new Vec3d(r.ReadDouble(), r.ReadDouble(), r.ReadDouble()) : null;
                return new ClientboundDamageEventPacket(id, type, cause < 0 ? null : cause, direct < 0 ? null : direct, pos);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareDamageEvent(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.DamageEvent)
            .From(JavaProtocols.V1_19_4, EntityStateCodecs.DamageEvent);
    }
}
