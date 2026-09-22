using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Explosion (<c>minecraft:explode</c>).</summary>
        public static readonly PacketType<ClientboundExplodePacket> Explode =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("explode"));
    }
}

/// <summary>
/// Explosion. Seven wire eras:
/// <list type="bullet">
/// <item>47-754: float center, strength, an <c>int</c>-counted block-offset list, float player motion.</item>
/// <item>755-760: the same with a VarInt-counted list (1.17 moved to <c>readList</c>).</item>
/// <item>761-764: the same with a DOUBLE center (1.19.3 widened it).</item>
/// <item>765-767: the 761 body plus a block-interaction ordinal and a trailing
/// (small particle, large particle, sound) group carried in <see cref="ParticleSoundTail"/>.</item>
/// <item>768-772: center, optional knockback, one particle, a sound holder.</item>
/// <item>773-776: the 768 body with radius and block count inserted and a weighted block-particle list.</item>
/// <item>777: the 773 body plus a trailing bool playSound.</item>
/// </list>
/// Era-absent fields default to null/empty/zero.
/// </summary>
/// <param name="Center">The explosion center. Read as three floats below 761 and three doubles from 761.</param>
/// <param name="LegacyStrength">The explosion power (47-767); zero from 768.</param>
/// <param name="LegacyBlocks">The destroyed-block offsets relative to the truncated center (47-767).</param>
/// <param name="LegacyMotionX">The player knockback X as a bare float (47-767).</param>
/// <param name="LegacyMotionY">The player knockback Y as a bare float (47-767).</param>
/// <param name="LegacyMotionZ">The player knockback Z as a bare float (47-767).</param>
/// <param name="Knockback">The optional player knockback vector (768+).</param>
/// <param name="Particle">The explosion particle (768+).</param>
/// <param name="Sound">The explosion sound (768+).</param>
/// <param name="Radius">The explosion radius (773+).</param>
/// <param name="BlockCount">The destroyed-block count (773+).</param>
/// <param name="BlockParticles">The weighted block-particle list (773+).</param>
/// <param name="BlockInteraction">The <c>Explosion.BlockInteraction</c> ordinal (0 keep, 1 destroy, 2 destroy-with-decay, 3 trigger-block) on 765-767; -1 on every other era, which does not carry it.</param>
/// <param name="ParticleSoundTail">The 765-767 trailing group (small particle, large particle, sound event) captured verbatim, empty on every other era. Those three fields are modeled as raw bytes rather than decoded because a structural decode needs a per-version particle option-shape table and 765's particle registry is NOT the 766 one (1.20.5 renumbered it: ENTITY_EFFECT gained a colour payload, AMBIENT_ENTITY_EFFECT left, and six particles were inserted mid-list). Capturing the group keeps the frame byte-exact and keeps every field ahead of it - center, power, destroyed blocks, knockback, block interaction - decoded and usable, which is what the single-codec collapse was destroying.</param>
public sealed record ClientboundExplodePacket(
    Vec3d Center,
    float LegacyStrength,
    IReadOnlyList<ExplosionBlock> LegacyBlocks,
    float LegacyMotionX,
    float LegacyMotionY,
    float LegacyMotionZ,
    Vec3d? Knockback,
    Codecs.ParticleData? Particle,
    SoundEventHolder? Sound,
    float Radius,
    int BlockCount,
    IReadOnlyList<ExplosionParticleInfo> BlockParticles,
    int BlockInteraction = -1,
    byte[]? ParticleSoundTail = null) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.Explode;

    /// <summary>Whether the explosion plays its sound, sent only on 26.3+ (777) as a trailing bool after the weighted block-particle list. Defaults to <c>true</c>; older eras carry no such field and always play the sound.</summary>
    public bool PlaySound { get; init; } = true;
}

// Explosion

/// <summary>A 1.8 explosion block offset (signed byte deltas relative to the truncated explosion origin).</summary>
public readonly record struct ExplosionBlock(sbyte Dx, sbyte Dy, sbyte Dz);

/// <summary>One weighted block-particle entry of a 1.21.9+ explosion (particle plus scaling/speed and a weight).</summary>
public sealed record ExplosionParticleInfo(Codecs.ParticleData Particle, float Scaling, float Speed, int Weight);
