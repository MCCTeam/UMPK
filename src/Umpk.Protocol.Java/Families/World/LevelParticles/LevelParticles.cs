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
    private LevelParticles777Fields? _fields777;

    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.LevelParticles;

    /// <summary>The Y maximum speed, sent only on 26.3+ (777), where the single speed splits into three per-axis floats. Defaults to <see cref="MaxSpeed"/> so pre-777 construction keeps all three axes equal.</summary>
    public float YMaxSpeed
    {
        get => _fields777?.YMaxSpeed ?? MaxSpeed;
        init => _fields777 = CreateFields777(value, ZMaxSpeed, Randomization);
    }

    /// <summary>The Z maximum speed, sent only on 26.3+ (777), where the single speed splits into three per-axis floats. Defaults to <see cref="MaxSpeed"/> so pre-777 construction keeps all three axes equal.</summary>
    public float ZMaxSpeed
    {
        get => _fields777?.ZMaxSpeed ?? MaxSpeed;
        init => _fields777 = CreateFields777(YMaxSpeed, value, Randomization);
    }

    /// <summary>The 26.3+ (777) randomization type. Defaults to <see cref="LevelParticleRandomizationType.Default"/>; older eras carry no such field.</summary>
    public LevelParticleRandomizationType Randomization
    {
        get => _fields777?.Randomization ?? LevelParticleRandomizationType.Default;
        init => _fields777 = CreateFields777(YMaxSpeed, ZMaxSpeed, value);
    }

    /// <summary>Builds a 26.3+ (777) level-particles packet carrying per-axis maximum speeds and a randomization type.</summary>
    public ClientboundLevelParticlesPacket(
        bool overrideLimiter,
        bool alwaysShow,
        double x,
        double y,
        double z,
        float xDist,
        float yDist,
        float zDist,
        float xMaxSpeed,
        float yMaxSpeed,
        float zMaxSpeed,
        int count,
        Codecs.ParticleData particle,
        LevelParticleRandomizationType randomization = LevelParticleRandomizationType.Default)
        : this(overrideLimiter, alwaysShow, x, y, z, xDist, yDist, zDist, xMaxSpeed, count, particle)
    {
        _fields777 = CreateFields777(yMaxSpeed, zMaxSpeed, randomization);
    }

    private LevelParticles777Fields? CreateFields777(
        float yMaxSpeed,
        float zMaxSpeed,
        LevelParticleRandomizationType randomization) =>
        yMaxSpeed.Equals(MaxSpeed)
        && zMaxSpeed.Equals(MaxSpeed)
        && randomization == LevelParticleRandomizationType.Default
            ? null
            : new LevelParticles777Fields(yMaxSpeed, zMaxSpeed, randomization);

    private sealed record LevelParticles777Fields(
        float YMaxSpeed,
        float ZMaxSpeed,
        LevelParticleRandomizationType Randomization);
}

/// <summary>The 26.3+ (777) level-particles randomization type (<c>ClientboundLevelParticlesPacket.RandomizationType</c>): 0 default, 1 alternative, 2 alternative-with-speed.</summary>
public enum LevelParticleRandomizationType
{
    /// <summary>Default randomization.</summary>
    Default = 0,

    /// <summary>Alternative randomization.</summary>
    Alternative = 1,

    /// <summary>Alternative randomization with speed.</summary>
    AlternativeWithSpeed = 2,
}
