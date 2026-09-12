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
        /// <summary>Set a creative-mode slot (<c>minecraft:creative_inventory_action</c> / <c>set_creative_mode_slot</c>).</summary>
        public static readonly PacketType<ServerboundSetCreativeModeSlotPacket> SetCreativeModeSlot =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("set_creative_mode_slot"));
    }
}

/// <summary>Set a creative-mode slot. 1.8 carries a short slot; modern carries a short slot and a delimited stack.</summary>
/// <param name="Slot">The slot index.</param>
/// <param name="Item">The stack to place.</param>
/// <param name="IsLegacy">True for the 1.8 identity/wire.</param>
public sealed record ServerboundSetCreativeModeSlotPacket(short Slot, ItemStack Item, bool IsLegacy = false) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => IsLegacy ? ItemPackets.Serverbound.LegacyCreativeSlot : ItemPackets.Serverbound.SetCreativeModeSlot;
}
