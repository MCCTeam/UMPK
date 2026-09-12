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
        /// <summary>Open a container/menu screen (<c>minecraft:open_screen</c>).</summary>
        public static readonly PacketType<ClientboundOpenScreenPacket> OpenScreen =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("open_screen"));
    }
}

// Clientbound records

/// <summary>Open a container screen: window id, menu type, title. 1.8 also carries a string type and slot count.</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="MenuTypeId">The menu-type registry id (modern); -1 with a legacy type on 1.8.</param>
/// <param name="Title">The window title.</param>
/// <param name="LegacyType">The 1.8 string window type, or null on modern.</param>
/// <param name="LegacySlotCount">The 1.8 slot count, or 0 on modern.</param>
/// <param name="LegacyEntityId">The 1.8 horse entity id, or null.</param>
public sealed record ClientboundOpenScreenPacket(
    int ContainerId,
    int MenuTypeId,
    Component Title,
    string? LegacyType,
    int LegacySlotCount,
    int? LegacyEntityId) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.OpenScreen;
}
