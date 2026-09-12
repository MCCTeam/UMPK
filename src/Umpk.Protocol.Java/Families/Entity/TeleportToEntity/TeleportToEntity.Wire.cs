using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>Spectator teleport to an entity (1.9+): the target entity's UUID (writeUUID = most/least longs). Constant across eras.</summary>
    public static readonly PacketCodec<ServerboundTeleportToEntityPacket> TeleportToEntity =
        PacketCodec<ServerboundTeleportToEntityPacket>.Of(
            static (ref PacketWriter w, ServerboundTeleportToEntityPacket p, PacketCodecContext _) => w.WriteUuid(p.Target),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundTeleportToEntityPacket(r.ReadUuid()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareTeleportToEntity(PacketBindings bindings)
    {
        // Spectator teleport-to-entity: the target UUID, one wire form on every protocol that carries it. Protocol 47 spells the same bare-UUID packet minecraft:spectate, so the binding starts at 47.
        bindings.Packet(EntityPackets.Serverbound.TeleportToEntity)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.TeleportToEntity)
            .AliasedAs(Identifier.Minecraft("spectate"));
    }
}
