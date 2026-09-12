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
        /// <summary>Click inside a container (<c>minecraft:container_click</c>).</summary>
        public static readonly PacketType<ServerboundContainerClickPacket> ContainerClick =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("container_click"));
    }
}

/// <summary>Click inside a container. 1.8 carries the old action-number/clicked-item handshake; 1.21.5+ carries the state id, the changed-slots map with hashed stacks, and the hashed carried stack.</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="StateId">The container revision id (modern); 0 on 1.8.</param>
/// <param name="Slot">The clicked slot.</param>
/// <param name="Button">The mouse/keyboard button.</param>
/// <param name="Mode">The click mode ordinal (pickup, quick-move, swap, clone, throw, quick-craft, pickup-all).</param>
/// <param name="ActionNumber">The 1.8 action number; 0 on modern.</param>
/// <param name="LegacyClickedItem">The 1.8 clicked item; null on modern.</param>
/// <param name="ChangedSlots">The modern predicted changed slots; empty on 1.8. Hashed at encode time.</param>
/// <param name="CarriedItem">The modern predicted carried (cursor) stack; null on 1.8. Hashed at encode time.</param>
public sealed record ServerboundContainerClickPacket(
    int ContainerId,
    int StateId,
    short Slot,
    byte Button,
    int Mode,
    short ActionNumber,
    ItemStack? LegacyClickedItem,
    IReadOnlyList<PredictedSlot> ChangedSlots,
    ItemStack? CarriedItem) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.ContainerClick;
}
