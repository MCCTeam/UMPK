using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Accept teleportation (<c>minecraft:accept_teleportation</c>).</summary>
        public static readonly PacketType<ServerboundAcceptTeleportationPacket> AcceptTeleportation =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("accept_teleportation"));
    }
}

/// <summary>Accept teleportation (modern): the VarInt teleport id being confirmed.</summary>
public sealed record ServerboundAcceptTeleportationPacket(int TeleportId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.AcceptTeleportation;
}
