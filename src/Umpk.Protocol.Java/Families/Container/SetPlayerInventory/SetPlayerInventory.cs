using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class ItemPackets
{
    public static partial class Clientbound
    {
        /// <summary>One player-inventory slot (<c>minecraft:set_player_inventory</c>, 1.21.2+).</summary>
        public static readonly PacketType<ClientboundSetPlayerInventoryPacket> SetPlayerInventory =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_player_inventory"));
    }
}

/// <summary>One player-inventory slot (1.21.2+).</summary>
/// <param name="Slot">The player-inventory slot index.</param>
/// <param name="Item">The slot contents.</param>
public sealed record ClientboundSetPlayerInventoryPacket(int Slot, ItemStack Item) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.SetPlayerInventory;
}
