using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Set border size (<c>minecraft:set_border_size</c>).</summary>
        public static readonly PacketType<ClientboundSetBorderSizePacket> SetBorderSize =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_border_size"));
    }
}

/// <summary>Set border size (instant).</summary>
public sealed record ClientboundSetBorderSizePacket(double Size) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SetBorderSize;
}
