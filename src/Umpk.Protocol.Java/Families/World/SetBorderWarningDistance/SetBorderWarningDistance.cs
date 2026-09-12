using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Set border warning distance (<c>minecraft:set_border_warning_distance</c>).</summary>
        public static readonly PacketType<ClientboundSetBorderWarningDistancePacket> SetBorderWarningDistance =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_border_warning_distance"));
    }
}

/// <summary>Set border warning distance in blocks.</summary>
public sealed record ClientboundSetBorderWarningDistancePacket(int WarningBlocks) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SetBorderWarningDistance;
}
