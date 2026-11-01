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

/// <summary>Entity interaction. Through 1.21.11, Action selects interact (0), attack (1) or interact-at (2); 1.9 adds the hand and 1.16 adds UsingSecondaryAction. From 26.1, the wire has no action field: Action is 0, Hand and the entity-relative InteractAt are required, and the location uses LP vector encoding. Attacks use the separate packet.</summary>
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
