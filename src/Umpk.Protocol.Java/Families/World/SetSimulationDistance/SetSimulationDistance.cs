using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Set simulation distance (<c>minecraft:set_simulation_distance</c>).</summary>
        public static readonly PacketType<ClientboundSetSimulationDistancePacket> SetSimulationDistance =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_simulation_distance"));
    }
}

/// <summary>Set simulation distance in chunks.</summary>
public sealed record ClientboundSetSimulationDistancePacket(int SimulationDistance) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SetSimulationDistance;
}
