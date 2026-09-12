using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntitySpawnCodecs
{
    /// <summary>Remove entities: a VarInt-prefixed VarInt list of ids (all versions).</summary>
    public static readonly PacketCodec<ClientboundRemoveEntitiesPacket> RemoveEntities =
        PacketCodec<ClientboundRemoveEntitiesPacket>.Of(
            static (ref PacketWriter w, ClientboundRemoveEntitiesPacket p, PacketCodecContext _) =>
                w.WriteList(p.EntityIds, static (ref PacketWriter ww, int id) => ww.WriteVarInt(id)),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundRemoveEntitiesPacket(r.ReadList(static (ref PacketReader rr) => rr.ReadVarInt())));

    /// <summary>1.17.0 singular remove_entity: a single VarInt entity id (1.17.0 briefly replaced the plural remove_entities with one packet per removed entity, reverted at 1.17.1). Modelled as a one-element RemoveEntities so the applier and consumers stay uniform.</summary>
    public static readonly PacketCodec<ClientboundRemoveEntitiesPacket> RemoveEntityV1_17 =
        PacketCodec<ClientboundRemoveEntitiesPacket>.Of(
            static (ref PacketWriter w, ClientboundRemoveEntitiesPacket p, PacketCodecContext _) =>
                w.WriteVarInt(p.EntityIds.Count > 0 ? p.EntityIds[0] : 0),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundRemoveEntitiesPacket([r.ReadVarInt()]));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRemoveEntities(PacketBindings bindings)
    {
        // 1.17.0 only: the singular remove_entity (one VarInt id) that briefly replaced the plural list;
        // decoded into a one-element removal so entity removal is observed there too. 1.8 spells the plural form entity_destroy.
        bindings.Packet(EntityPackets.Clientbound.RemoveEntities)
            .From(JavaProtocols.V1_8, EntitySpawnCodecs.RemoveEntities)
            .From(JavaProtocols.V1_17, EntitySpawnCodecs.RemoveEntityV1_17)
            .From(JavaProtocols.V1_17_1, EntitySpawnCodecs.RemoveEntities)
            .AliasedAs(Identifier.Minecraft("entity_destroy"))
            .AliasedAs(Identifier.Minecraft("remove_entity"), JavaProtocols.V1_17, JavaProtocols.V1_17);
    }
}
