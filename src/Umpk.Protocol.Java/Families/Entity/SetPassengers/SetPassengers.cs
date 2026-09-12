using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Passenger graph (<c>minecraft:set_passengers</c>).</summary>
        public static readonly PacketType<ClientboundSetPassengersPacket> SetPassengers =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_passengers"));
    }
}

/// <summary>Passenger graph: vehicle id then the VarInt list of passenger ids.</summary>
public sealed record ClientboundSetPassengersPacket(int VehicleId, IReadOnlyList<int> Passengers) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.SetPassengers;
}
