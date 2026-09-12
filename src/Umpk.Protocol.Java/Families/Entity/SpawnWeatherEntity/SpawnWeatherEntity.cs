using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>1.8 spawn global (lightning) entity (<c>minecraft:spawn_weather_entity</c>).</summary>
        public static readonly PacketType<ClientboundAddGlobalEntityPacket> AddGlobalEntity =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("spawn_weather_entity"));
    }
}

/// <summary>1.8 spawn global (lightning) entity: entity id, type byte, fixed-point position.</summary>
public sealed record ClientboundAddGlobalEntityPacket(int EntityId, byte GlobalType, double X, double Y, double Z) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.AddGlobalEntity;
}
