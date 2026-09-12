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
        /// <summary>A container property/data value (<c>minecraft:container_set_data</c>).</summary>
        public static readonly PacketType<ClientboundContainerSetDataPacket> ContainerSetData =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("container_set_data"));
    }
}

/// <summary>A container property/data value (furnace progress, enchant levels,...).</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="PropertyId">The property index.</param>
/// <param name="Value">The property value.</param>
public sealed record ClientboundContainerSetDataPacket(int ContainerId, short PropertyId, short Value) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.ContainerSetData;
}
