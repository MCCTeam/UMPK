using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Initialize world border (<c>minecraft:initialize_border</c>).</summary>
        public static readonly PacketType<ClientboundInitializeBorderPacket> InitializeBorder =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("initialize_border"));
    }
}

// World border

/// <summary>Initialize world border (modern split-family): full center/size/lerp/warning block.</summary>
public sealed record ClientboundInitializeBorderPacket(
    double CenterX,
    double CenterZ,
    double OldSize,
    double NewSize,
    long LerpTime,
    int NewAbsoluteMaxSize,
    int WarningBlocks,
    int WarningTime) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.InitializeBorder;
}
