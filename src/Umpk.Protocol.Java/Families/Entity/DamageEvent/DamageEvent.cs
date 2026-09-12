using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Damage event (<c>minecraft:damage_event</c>, 1.19.4+).</summary>
        public static readonly PacketType<ClientboundDamageEventPacket> DamageEvent =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("damage_event"));
    }
}

/// <summary>Damage event (1.19.4+): the hurt entity id, the damage-type holder id, optional cause/direct entity ids (-1 = absent), and an optional source position.</summary>
public sealed record ClientboundDamageEventPacket(
    int EntityId,
    int SourceTypeId,
    int? SourceCauseId,
    int? SourceDirectId,
    Vec3d? SourcePosition) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.DamageEvent;
}
