using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Remove mob effect (<c>minecraft:remove_mob_effect</c> / 1.8 <c>remove_entity_effect</c>).</summary>
        public static readonly PacketType<ClientboundRemoveMobEffectPacket> RemoveMobEffect =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("remove_mob_effect"));
    }
}

/// <summary>Remove mob effect: entity id and effect id (byte on 1.8, holder VarInt modern).</summary>
public sealed record ClientboundRemoveMobEffectPacket(int EntityId, int EffectId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.RemoveMobEffect;
}
