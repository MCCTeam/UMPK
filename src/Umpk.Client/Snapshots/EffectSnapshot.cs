using Umpk.Client.State;
using Umpk.Game.Entities;
using Umpk.Game.Registries;

namespace Umpk.Client.Snapshots;

/// <summary>An immutable snapshot of one active status effect.</summary>
/// <param name="EffectId">The namespaced effect id (for example <c>minecraft:speed</c>), resolved against the session's mob-effect registry. When the registry cannot name <paramref name="NetworkId"/> (no registries loaded yet, or a network id the registry does not carry), this is a synthesized <c>umpk:unknown_effect_&lt;id&gt;</c> identifier rather than a thrown exception or a dropped entry: the effect is still reported, just unnamed.</param>
/// <param name="NetworkId">The raw effect network (registry) id, always present regardless of whether it named.</param>
/// <param name="Amplifier">The amplifier; 0 is level I.</param>
/// <param name="Level">The 1-based level (<paramref name="Amplifier"/> + 1).</param>
/// <param name="Duration">Remaining duration in ticks; -1 for infinite.</param>
/// <param name="IsInfinite">Whether <paramref name="Duration"/> is infinite (negative).</param>
/// <param name="IsAmbient">Whether the effect is ambient (from a beacon or a nearby source, not drunk/eaten).</param>
/// <param name="ShowParticles">Whether particles are shown.</param>
/// <param name="ShowIcon">Whether the HUD icon is shown.</param>
public sealed record EffectSnapshot(
    Identifier EffectId,
    int NetworkId,
    int Amplifier,
    int Level,
    int Duration,
    bool IsInfinite,
    bool IsAmbient,
    bool ShowParticles,
    bool ShowIcon)
{
    /// <summary>The namespace synthesized identifiers use when the registry cannot name an effect id.</summary>
    private const string UnnamedNamespace = "umpk";

    /// <summary>The ambient bit in <see cref="ActiveEffect.Flags"/> (vanilla effect-flags bit 0).</summary>
    private const byte AmbientFlag = 0x01;

    /// <summary>The show-particles bit in <see cref="ActiveEffect.Flags"/> (vanilla effect-flags bit 1).</summary>
    private const byte ParticlesFlag = 0x02;

    /// <summary>The show-icon bit in <see cref="ActiveEffect.Flags"/> (vanilla effect-flags bit 2).</summary>
    private const byte IconFlag = 0x04;

    /// <summary>Projects every active effect on the local player. Pure; no session loop involved. Never omits an effect for want of a registry name; see <see cref="EffectId"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static IReadOnlyList<EffectSnapshot> Project(ClientState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        RegistryAccess? registries = state.Registries;
        var effects = new List<EffectSnapshot>(state.Self.ActiveEffects.Count);
        foreach (KeyValuePair<int, ActiveEffect> pair in state.Self.ActiveEffects)
        {
            ActiveEffect effect = pair.Value;
            Identifier id = registries is not null && registries.MobEffects.TryGetKey(effect.EffectId, out Identifier key)
                ? key
                : new Identifier(UnnamedNamespace, $"unknown_effect_{effect.EffectId}");

            effects.Add(new EffectSnapshot(
                id,
                effect.EffectId,
                effect.Amplifier,
                effect.Amplifier + 1,
                effect.Duration,
                effect.Duration < 0,
                (effect.Flags & AmbientFlag) != 0,
                (effect.Flags & ParticlesFlag) != 0,
                (effect.Flags & IconFlag) != 0));
        }

        return effects;
    }

    /// <summary>Projects a tracked entity's effect instance, which already carries a resolved registry entry (unlike the local player's effects, which are the raw network id UMPK does not resolve at apply time; see <see cref="Project(ClientState)"/>). Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="instance"/> is null.</exception>
    public static EffectSnapshot Project(EffectInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        RegistryEntry<MobEffectDefinition> effect = instance.Effect;
        return new EffectSnapshot(
            effect.Id,
            effect.NetworkId,
            instance.Amplifier,
            instance.Level,
            instance.Duration,
            instance.IsInfinite,
            instance.IsAmbient,
            instance.ShowParticles,
            instance.ShowIcon);
    }
}
