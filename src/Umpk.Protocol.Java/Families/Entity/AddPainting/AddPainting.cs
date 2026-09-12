using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>1.8 spawn painting (<c>minecraft:add_painting</c>).</summary>
        public static readonly PacketType<ClientboundAddPaintingPacket> AddPainting =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("add_painting"));
    }
}

/// <summary>Spawn painting: entity id, motive, block position, facing direction. <paramref name="Uuid"/> is the entity uuid 1.9 added and is null on 1.8. From 1.13 the motive is a registry id carried in <paramref name="MotiveId"/> with <paramref name="Title"/> empty; before that the motive is the registry NAME in <paramref name="Title"/> and <paramref name="MotiveId"/> is null.</summary>
public sealed record ClientboundAddPaintingPacket(
    int EntityId,
    string Title,
    BlockPos Position,
    byte Facing,
    Guid? Uuid,
    int? MotiveId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.AddPainting;
}
