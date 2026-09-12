namespace Umpk.Game.Entities;

/// <summary>The packed status-effect flag bits, matching the vanilla wire byte on the effect packet (ambient = 1, show-particles = 2, show-icon = 4, blend = 8 on 1.20.5+).</summary>
[Flags]
public enum EffectFlags : byte
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>The effect is ambient (beacon source; dimmer particles).</summary>
    Ambient = 1,

    /// <summary>Particles are shown.</summary>
    ShowParticles = 2,

    /// <summary>The HUD icon is shown.</summary>
    ShowIcon = 4,

    /// <summary>The effect uses blended fade transitions (1.20.5+).</summary>
    Blend = 8,
}
