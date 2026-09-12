using Umpk.Client.State;

namespace Umpk.Client.Snapshots;

/// <summary>An immutable snapshot of the local player's server-advertised abilities.</summary>
/// <param name="Flying">Whether the player is currently flying.</param>
/// <param name="MayFly">Whether the player may toggle flight.</param>
/// <param name="Invulnerable">Whether the player is invulnerable per server abilities.</param>
/// <param name="InstantBuild">Whether creative instant-build is allowed.</param>
/// <param name="FlyingSpeed">Server-advertised flying speed.</param>
/// <param name="WalkingSpeed">Server-advertised walking speed.</param>
public sealed record AbilitiesSnapshot(
    bool Flying, bool MayFly, bool Invulnerable, bool InstantBuild, float FlyingSpeed, float WalkingSpeed)
{
    /// <summary>Projects an <see cref="AbilitiesSnapshot"/> from live tracked state. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="self"/> is null.</exception>
    public static AbilitiesSnapshot Project(SelfState self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return new AbilitiesSnapshot(
            self.Flying, self.MayFly, self.Invulnerable, self.InstantBuild, self.FlyingSpeed, self.WalkingSpeed);
    }
}
