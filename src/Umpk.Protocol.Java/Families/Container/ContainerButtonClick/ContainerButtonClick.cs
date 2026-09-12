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
        /// <summary>A container button (<c>minecraft:container_button_click</c>).</summary>
        public static readonly PacketType<ServerboundContainerButtonClickPacket> ContainerButtonClick =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("container_button_click"));
    }
}

/// <summary>Click a container button (enchant, lectern page, loom pattern,...).</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="ButtonId">The button index.</param>
public sealed record ServerboundContainerButtonClickPacket(int ContainerId, int ButtonId) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.ContainerButtonClick;
}
