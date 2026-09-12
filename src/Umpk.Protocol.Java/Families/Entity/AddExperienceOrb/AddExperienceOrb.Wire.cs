using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntitySpawnCodecs
{
    /// <summary>1.8 experience-orb spawn.</summary>
    public static readonly PacketCodec<ClientboundAddExperienceOrbPacket> AddExperienceOrbV1_8 =
        PacketCodec<ClientboundAddExperienceOrbPacket>.Of(
            static (ref PacketWriter w, ClientboundAddExperienceOrbPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteInt(PackLegacyPos(p.X));
                w.WriteInt(PackLegacyPos(p.Y));
                w.WriteInt(PackLegacyPos(p.Z));
                w.WriteShort(p.Value);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundAddExperienceOrbPacket(r.ReadVarInt(), UnpackLegacyPos(r.ReadInt()), UnpackLegacyPos(r.ReadInt()), UnpackLegacyPos(r.ReadInt()), r.ReadShort()));

    /// <summary>1.9-1.13.2 spawn experience orb: id VarInt, double x/y/z, short count.</summary>
    public static readonly PacketCodec<ClientboundAddExperienceOrbPacket> AddExperienceOrbV1_9 =
        PacketCodec<ClientboundAddExperienceOrbPacket>.Of(
            static (ref PacketWriter w, ClientboundAddExperienceOrbPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteShort(p.Value);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundAddExperienceOrbPacket(r.ReadVarInt(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadShort()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAddExperienceOrb(PacketBindings bindings)
    {
        // The 1.9 form (VarInt id, three doubles, short value) never changed again. The packet leaves the protocol at 1.21.5 (no dataset from 770 on registers it). The old MarkerFrom(1.14) was therefore a pure gap, not an era: on 477-769 (30 protocols) experience-orb spawns relayed verbatim and no orb entity was ever tracked.
        bindings.Packet(EntityPackets.Clientbound.AddExperienceOrb)
            .From(JavaProtocols.V1_8, EntitySpawnCodecs.AddExperienceOrbV1_8)
            .From(JavaProtocols.V1_9, EntitySpawnCodecs.AddExperienceOrbV1_9);
    }
}
