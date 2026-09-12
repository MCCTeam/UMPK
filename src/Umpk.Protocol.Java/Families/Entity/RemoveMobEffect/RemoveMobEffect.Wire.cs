using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityEffectCodecs
{
    /// <summary>1.8 remove entity effect: entity id VarInt and effect byte.</summary>
    public static readonly PacketCodec<ClientboundRemoveMobEffectPacket> RemoveMobEffectV1_8 =
        PacketCodec<ClientboundRemoveMobEffectPacket>.Of(
            static (ref PacketWriter w, ClientboundRemoveMobEffectPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteByte((byte)p.EffectId);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundRemoveMobEffectPacket(r.ReadVarInt(), r.ReadByte()));

    /// <summary>Modern remove mob effect: entity id VarInt, effect holder VarInt.</summary>
    public static readonly PacketCodec<ClientboundRemoveMobEffectPacket> RemoveMobEffectV1_18 =
        PacketCodec<ClientboundRemoveMobEffectPacket>.Of(
            static (ref PacketWriter w, ClientboundRemoveMobEffectPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteVarInt(p.EffectId);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundRemoveMobEffectPacket(r.ReadVarInt(), r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRemoveMobEffect(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.RemoveMobEffect)
            .From(JavaProtocols.V1_8, EntityEffectCodecs.RemoveMobEffectV1_8)
            .From(JavaProtocols.V1_18, EntityEffectCodecs.RemoveMobEffectV1_18)
            .AliasedAs(Identifier.Minecraft("remove_entity_effect"));
    }
}
