using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Leash / entity link (<c>minecraft:set_entity_link</c> / 1.8 <c>attach_entity</c>).</summary>
        public static readonly PacketType<ClientboundSetEntityLinkPacket> SetEntityLink =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_entity_link"));
    }
}

/// <summary>Leash / entity link: source (leashed) id and destination (holder) id as 4-byte ints. The 1.8 <c>attach_entity</c> shape adds a trailing leash boolean byte (<see cref="LegacyLeash"/> set).</summary>
public sealed record ClientboundSetEntityLinkPacket(int SourceId, int DestId, bool? LegacyLeash) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.SetEntityLink;
}
