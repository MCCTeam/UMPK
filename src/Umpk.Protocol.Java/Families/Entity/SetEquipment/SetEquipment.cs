using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Equipment (<c>minecraft:set_equipment</c> / 1.8 <c>entity_equipment</c>).</summary>
        public static readonly PacketType<ClientboundSetEquipmentPacket> SetEquipment =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_equipment"));
    }
}

// Equipment / attributes / effects

/// <summary>Equipment. 1.8: a single slot short plus one item (the item is terminal, so it decodes fully via the legacy item codec). Modern: a grouped list where each entry is a slot byte (top bit = another entry follows) and a modern item stack; no modern item codec is available yet, so the modern payload after the entity id is captured raw. See <see cref="ModernRaw"/>.</summary>
public sealed record ClientboundSetEquipmentPacket(
    int EntityId,
    EquipmentSlot LegacySlot,
    EntityItemSlot? LegacyItem,
    byte[]? ModernRaw) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.SetEquipment;
}
