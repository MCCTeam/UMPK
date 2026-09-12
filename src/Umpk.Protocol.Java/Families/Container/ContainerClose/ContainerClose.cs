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
        /// <summary>Close a container (<c>minecraft:container_close</c>).</summary>
        public static readonly PacketType<ClientboundContainerClosePacket> ContainerClose =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("container_close"));
    }

    public static partial class Serverbound
    {
        /// <summary>Close a container (<c>minecraft:container_close</c>).</summary>
        public static readonly PacketType<ServerboundContainerClosePacket> ContainerClose =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("container_close"));
    }
}

/// <summary>Close a container window.</summary>
/// <param name="ContainerId">The window id.</param>
public sealed record ClientboundContainerClosePacket(int ContainerId) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.ContainerClose;
}

// Serverbound records

/// <summary>Close a container window.</summary>
/// <param name="ContainerId">The window id.</param>
public sealed record ServerboundContainerClosePacket(int ContainerId) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.ContainerClose;
}
