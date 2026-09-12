using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Head rotation (<c>minecraft:rotate_head</c>).</summary>
        public static readonly PacketType<ClientboundRotateHeadPacket> RotateHead =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("rotate_head"));
    }
}

/// <summary>Head rotation: entity id, packed head-yaw angle byte.</summary>
public sealed record ClientboundRotateHeadPacket(int EntityId, float HeadYaw) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.RotateHead;
}
