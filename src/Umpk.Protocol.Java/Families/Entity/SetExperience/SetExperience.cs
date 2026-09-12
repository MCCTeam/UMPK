using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Experience (<c>minecraft:set_experience</c>).</summary>
        public static readonly PacketType<ClientboundSetExperiencePacket> SetExperience =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_experience"));
    }
}

/// <summary>Experience: bar progress float, VarInt level, VarInt total experience.</summary>
public sealed record ClientboundSetExperiencePacket(float ExperienceProgress, int Level, int TotalExperience) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.SetExperience;
}
