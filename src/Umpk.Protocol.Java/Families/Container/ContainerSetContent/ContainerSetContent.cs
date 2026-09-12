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
        /// <summary>Full container contents (<c>minecraft:container_set_content</c>).</summary>
        public static readonly PacketType<ClientboundContainerSetContentPacket> ContainerSetContent =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("container_set_content"));
    }
}

/// <summary>Full container contents plus the carried cursor stack (modern) / just items (1.8).</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="StateId">The container revision id (1.17.1+); 0 on 1.8.</param>
/// <param name="Items">The slot contents.</param>
/// <param name="CarriedItem">The cursor stack (modern); empty on 1.8.</param>
public sealed record ClientboundContainerSetContentPacket(
    int ContainerId,
    int StateId,
    IReadOnlyList<ItemStack> Items,
    ItemStack CarriedItem) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.ContainerSetContent;
}
