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
        /// <summary>Rename the item in an anvil (<c>minecraft:rename_item</c>).</summary>
        public static readonly PacketType<ServerboundRenameItemPacket> RenameItem =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("rename_item"));
    }
}

/// <summary>Rename the item in an anvil (1.13+): the new item name string.</summary>
/// <param name="Name">The new item name.</param>
public sealed record ServerboundRenameItemPacket(string Name) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.RenameItem;
}
