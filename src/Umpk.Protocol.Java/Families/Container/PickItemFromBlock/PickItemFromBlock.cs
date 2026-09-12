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
        /// <summary>Pick an item from a block (<c>minecraft:pick_item_from_block</c>).</summary>
        public static readonly PacketType<ServerboundPickItemFromBlockPacket> PickItemFromBlock =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("pick_item_from_block"));
    }
}

/// <summary>Pick an item from a block into the hotbar.</summary>
/// <param name="Position">The block position.</param>
/// <param name="IncludeData">Whether to include block-entity data.</param>
public sealed record ServerboundPickItemFromBlockPacket(BlockPos Position, bool IncludeData) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.PickItemFromBlock;
}
