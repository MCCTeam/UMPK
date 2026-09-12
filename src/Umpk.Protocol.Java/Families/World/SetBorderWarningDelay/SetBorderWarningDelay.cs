using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Set border warning delay (<c>minecraft:set_border_warning_delay</c>).</summary>
        public static readonly PacketType<ClientboundSetBorderWarningDelayPacket> SetBorderWarningDelay =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_border_warning_delay"));
    }
}

/// <summary>Set border warning delay in seconds.</summary>
public sealed record ClientboundSetBorderWarningDelayPacket(int WarningDelay) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SetBorderWarningDelay;
}
