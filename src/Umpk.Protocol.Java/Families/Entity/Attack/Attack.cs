using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Attack entity (<c>minecraft:attack</c>, 26.1+ split from interact).</summary>
        public static readonly PacketType<ServerboundAttackPacket> Attack =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("attack"));
    }
}

/// <summary>Attack entity (26.1+): a single entity id.</summary>
public sealed record ServerboundAttackPacket(int EntityId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.Attack;
}
