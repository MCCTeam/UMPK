using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Set border center (<c>minecraft:set_border_center</c>).</summary>
        public static readonly PacketType<ClientboundSetBorderCenterPacket> SetBorderCenter =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_border_center"));
    }
}

/// <summary>Set border center.</summary>
public sealed record ClientboundSetBorderCenterPacket(double CenterX, double CenterZ) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SetBorderCenter;
}
