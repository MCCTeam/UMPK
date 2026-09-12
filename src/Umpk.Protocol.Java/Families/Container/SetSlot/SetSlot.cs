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
        /// <summary>The 1.8 one-slot packet (<c>minecraft:set_slot</c>); distinct identifier from modern.</summary>
        public static readonly PacketType<ClientboundContainerSetSlotPacket> LegacySetSlot =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_slot"));
    }
}

/// <summary>One container slot's contents.</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="StateId">The container revision id (1.17.1+); 0 on 1.8.</param>
/// <param name="Slot">The slot index.</param>
/// <param name="Item">The slot contents.</param>
/// <param name="IsLegacy">True when this is the 1.8 <c>set_slot</c> identity.</param>
public sealed record ClientboundContainerSetSlotPacket(
    int ContainerId,
    int StateId,
    int Slot,
    ItemStack Item,
    bool IsLegacy = false) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => IsLegacy ? ItemPackets.Clientbound.LegacySetSlot : ItemPackets.Clientbound.ContainerSetSlot;
}
