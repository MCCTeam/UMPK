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
        /// <summary>Toggle a container slot's crafting state (<c>minecraft:container_slot_state_changed</c>).</summary>
        public static readonly PacketType<ServerboundContainerSlotStateChangedPacket> ContainerSlotStateChanged =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("container_slot_state_changed"));
    }
}

/// <summary>Toggle a container slot's crafting-enabled state (1.20.3+ crafter).</summary>
/// <param name="SlotId">The slot index.</param>
/// <param name="ContainerId">The window id.</param>
/// <param name="NewState">The new enabled state.</param>
public sealed record ServerboundContainerSlotStateChangedPacket(int SlotId, int ContainerId, bool NewState) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.ContainerSlotStateChanged;
}
