using Umpk.Game.Registries;

namespace Umpk.Game.Entities;

/// <summary>A single active status effect on an entity. Amplifier 0 is level I. The 1.20.5+ VarInt amplifier is a plain <see cref="int"/> here. A duration of -1 represents an effectively infinite effect.</summary>
public sealed record EffectInstance
{
    /// <summary>Creates an effect instance for a resolved effect registry entry.</summary>
    /// <param name="effect">The effect type (a <c>mob_effect</c> registry handle).</param>
    /// <param name="amplifier">The amplifier; 0 is level I.</param>
    /// <param name="duration">Remaining duration in ticks; -1 for infinite.</param>
    /// <param name="flags">The packed effect flags.</param>
    public EffectInstance(RegistryEntry<MobEffectDefinition> effect, int amplifier, int duration, EffectFlags flags)
    {
        Effect = effect;
        Amplifier = amplifier;
        Duration = duration;
        Flags = flags;
    }

    /// <summary>The effect type (a <c>mob_effect</c> registry handle). The effect's numeric network id is available through <see cref="RegistryEntry{T}.NetworkId"/>.</summary>
    public RegistryEntry<MobEffectDefinition> Effect { get; }

    /// <summary>The amplifier; 0 is level I, 1 is level II, and so on.</summary>
    public int Amplifier { get; }

    /// <summary>Remaining duration in ticks; -1 for infinite.</summary>
    public int Duration { get; }

    /// <summary>The packed effect flags.</summary>
    public EffectFlags Flags { get; }

    /// <summary>The effect level shown to players (<see cref="Amplifier"/> + 1).</summary>
    public int Level => Amplifier + 1;

    /// <summary>True when <see cref="Duration"/> encodes the infinite sentinel (-1).</summary>
    public bool IsInfinite => Duration < 0;

    /// <summary>Whether this effect is ambient (from a beacon, dimmer particles).</summary>
    public bool IsAmbient => (Flags & EffectFlags.Ambient) != 0;

    /// <summary>Whether particles are shown for this effect.</summary>
    public bool ShowParticles => (Flags & EffectFlags.ShowParticles) != 0;

    /// <summary>Whether the effect icon is shown in the HUD.</summary>
    public bool ShowIcon => (Flags & EffectFlags.ShowIcon) != 0;
}
