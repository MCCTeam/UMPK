using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Start configuration (<c>minecraft:start_configuration</c>): empty payload, terminal back into the configuration phase once the client acknowledges (1.20.2+).</summary>
        public static readonly PacketType<ClientboundStartConfigurationPacket> StartConfiguration =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("start_configuration"));
    }
}

/// <summary>Start configuration (clientbound play, empty payload); the Play-to-Configuration re-entry gate.</summary>
public sealed record ClientboundStartConfigurationPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.StartConfiguration;
}
