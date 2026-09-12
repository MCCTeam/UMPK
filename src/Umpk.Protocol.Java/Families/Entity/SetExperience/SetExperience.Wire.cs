using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>Experience (all versions): progress float, VarInt level, VarInt total.</summary>
    public static readonly PacketCodec<ClientboundSetExperiencePacket> SetExperience =
        PacketCodec<ClientboundSetExperiencePacket>.Of(
            static (ref PacketWriter w, ClientboundSetExperiencePacket p, PacketCodecContext _) =>
            {
                w.WriteFloat(p.ExperienceProgress);
                w.WriteVarInt(p.Level);
                w.WriteVarInt(p.TotalExperience);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetExperiencePacket(r.ReadFloat(), r.ReadVarInt(), r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetExperience(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.SetExperience)
            .From(JavaProtocols.V1_8, EntityStateCodecs.SetExperience);
    }
}
