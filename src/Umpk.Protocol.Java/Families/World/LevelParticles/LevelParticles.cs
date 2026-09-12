using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Level particles (<c>minecraft:level_particles</c>).</summary>
        public static readonly PacketType<ClientboundLevelParticlesPacket> LevelParticles =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("level_particles"));
    }
}

/// <summary>Level particles. The particle-type payload is modeled by <see cref="Codecs.ParticleData"/>: a type id plus a structured option value (with a raw-bytes fallback for item-carrying particles). 1.8 uses a signed-int type id and a fixed per-type trailing VarInt count; modern uses a registry VarInt id and a self-describing per-type payload.</summary>
public sealed record ClientboundLevelParticlesPacket(
    bool OverrideLimiter,
    bool AlwaysShow,
    double X,
    double Y,
    double Z,
    float XDist,
    float YDist,
    float ZDist,
    float MaxSpeed,
    int Count,
    Codecs.ParticleData Particle) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.LevelParticles;
}
