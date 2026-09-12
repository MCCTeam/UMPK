using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class ItemPackets
{
    public static partial class Serverbound
    {
        /// <summary>Pick an item from an entity (<c>minecraft:pick_item_from_entity</c>).</summary>
        public static readonly PacketType<ServerboundPickItemFromEntityPacket> PickItemFromEntity =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("pick_item_from_entity"));
    }
}

/// <summary>Pick an item from an entity.</summary>
/// <param name="EntityId">The entity id.</param>
/// <param name="IncludeData">Whether to include entity data.</param>
public sealed record ServerboundPickItemFromEntityPacket(int EntityId, bool IncludeData) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.PickItemFromEntity;
}
