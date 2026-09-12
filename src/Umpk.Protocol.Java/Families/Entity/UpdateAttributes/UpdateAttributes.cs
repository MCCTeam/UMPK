using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Attributes (<c>minecraft:update_attributes</c>).</summary>
        public static readonly PacketType<ClientboundUpdateAttributesPacket> UpdateAttributes =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("update_attributes"));
    }
}

/// <summary>Attributes: entity id then the attribute list.</summary>
public sealed record ClientboundUpdateAttributesPacket(int EntityId, IReadOnlyList<AttributeSnapshot> Attributes) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.UpdateAttributes;
}

/// <summary>A single attribute on the wire: id (string on 1.8, VarInt holder on modern), base value, modifiers.</summary>
public sealed record AttributeSnapshot(string? LegacyKey, int ModernId, double BaseValue, IReadOnlyList<AttributeModifierEntry> Modifiers);
