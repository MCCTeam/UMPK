using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Interact entity (<c>minecraft:interact</c> / 1.8 <c>use_entity</c>).</summary>
        public static readonly PacketType<ServerboundInteractPacket> Interact =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("interact"));
    }
}

/// <summary>Interact entity: entity id, action VarInt (0 interact, 1 attack, 2 interact-at). Interact carries a hand VarInt; interact-at carries three floats then a hand; attack carries nothing. Modern (1.9+) adds a trailing using-secondary-action boolean (<see cref="UsingSecondaryAction"/> set).</summary>
public sealed record ServerboundInteractPacket(
    int EntityId,
    int Action,
    int? Hand,
    Vec3d? InteractAt,
    bool? UsingSecondaryAction) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.Interact;
}
