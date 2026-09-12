using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        /// <summary>Item cooldown (<c>minecraft:cooldown</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundCooldownPacket> Cooldown =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("cooldown"));
    }
}

/// <summary>Item cooldown: a cooldown target and the duration in ticks. On 1.21.5 the group is the item's identifier; on 26.x it is a dedicated cooldown-group identifier. Both are a namespaced id on the wire, so <paramref name="CooldownGroup"/> carries an <see cref="Identifier"/> either way. Before 1.21.2 the cooldown is per ITEM and the wire carries the item's VarInt registry id instead, which is what <paramref name="ItemId"/> holds; it is null from 1.21.2 on and <paramref name="CooldownGroup"/> is default below it.</summary>
public sealed record ClientboundCooldownPacket(Identifier CooldownGroup, int Ticks, int? ItemId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.Cooldown;
}
