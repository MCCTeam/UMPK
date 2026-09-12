using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>1.8 combined world border packet (<c>minecraft:world_border</c>, action-tagged).</summary>
        public static readonly PacketType<ClientboundWorldBorderPacket> WorldBorder =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("world_border"));
    }
}

/// <summary>The 1.8 combined world-border packet. One <see cref="Action"/> tag selects which fields are present; absent fields default to zero. The era codec reads/writes only the fields the action carries.</summary>
public sealed record ClientboundWorldBorderPacket(
    WorldBorderAction Action,
    double CenterX,
    double CenterZ,
    double OldSize,
    double NewSize,
    long LerpTime,
    int PortalTeleportBoundary,
    int WarningTime,
    int WarningBlocks) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.WorldBorder;
}

/// <summary>The action tag of the 1.8 combined world-border packet.</summary>
public enum WorldBorderAction
{
    /// <summary>Set the border size instantly.</summary>
    SetSize = 0,

    /// <summary>Animate the border size over time.</summary>
    LerpSize = 1,

    /// <summary>Move the border center.</summary>
    SetCenter = 2,

    /// <summary>Full border initialization.</summary>
    Initialize = 3,

    /// <summary>Set the warning time in seconds.</summary>
    SetWarningTime = 4,

    /// <summary>Set the warning distance in blocks.</summary>
    SetWarningBlocks = 5,
}
