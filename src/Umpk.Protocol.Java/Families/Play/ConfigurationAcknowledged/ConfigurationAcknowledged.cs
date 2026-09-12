using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Serverbound
    {
        /// <summary>Configuration acknowledged (<c>minecraft:configuration_acknowledged</c>): empty payload, terminal into the configuration phase (mirror of login-acknowledged, 1.20.2+).</summary>
        public static readonly PacketType<ServerboundConfigurationAcknowledgedPacket> ConfigurationAcknowledged =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("configuration_acknowledged"));
    }
}

/// <summary>Configuration acknowledged (serverbound play, empty payload); terminal into configuration.</summary>
public sealed record ServerboundConfigurationAcknowledgedPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.ConfigurationAcknowledged;
}
