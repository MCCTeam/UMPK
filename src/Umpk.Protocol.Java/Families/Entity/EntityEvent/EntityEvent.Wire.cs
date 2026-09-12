using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>Entity event / status (all versions): 4-byte entity id, byte event id.</summary>
    public static readonly PacketCodec<ClientboundEntityEventPacket> EntityEvent =
        PacketCodec<ClientboundEntityEventPacket>.Of(
            static (ref PacketWriter w, ClientboundEntityEventPacket p, PacketCodecContext _) =>
            {
                w.WriteInt(p.EntityId);
                w.WriteSByte(p.EventId);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundEntityEventPacket(r.ReadInt(), r.ReadSByte()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareEntityEvent(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Clientbound.EntityEvent)
            .From(JavaProtocols.V1_8, EntityStateCodecs.EntityEvent)
            .AliasedAs(Identifier.Minecraft("entity_status"));
    }
}
