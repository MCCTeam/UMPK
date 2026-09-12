using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>1.8 spawn experience orb (<c>minecraft:add_experience_orb</c>).</summary>
        public static readonly PacketType<ClientboundAddExperienceOrbPacket> AddExperienceOrb =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("add_experience_orb"));
    }
}

/// <summary>1.8 spawn experience orb: entity id, fixed-point position, xp value.</summary>
public sealed record ClientboundAddExperienceOrbPacket(int EntityId, double X, double Y, double Z, short Value) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.AddExperienceOrb;
}
