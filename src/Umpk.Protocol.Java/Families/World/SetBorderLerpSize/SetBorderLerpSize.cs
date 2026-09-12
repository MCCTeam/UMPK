using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Set border lerp size (<c>minecraft:set_border_lerp_size</c>).</summary>
        public static readonly PacketType<ClientboundSetBorderLerpSizePacket> SetBorderLerpSize =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_border_lerp_size"));
    }
}

/// <summary>Set border lerp size (animated from old to new over a duration).</summary>
public sealed record ClientboundSetBorderLerpSizePacket(double OldSize, double NewSize, long LerpTime) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SetBorderLerpSize;
}
