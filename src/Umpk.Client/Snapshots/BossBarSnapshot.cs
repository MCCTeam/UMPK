using Umpk.Game.Players;
using Umpk.Text;

namespace Umpk.Client.Snapshots;

/// <summary>One boss bar.</summary>
/// <param name="Uuid">The boss-bar uuid.</param>
/// <param name="Title">The title component.</param>
/// <param name="Progress">The fill fraction, always in [0, 1].</param>
/// <param name="Color">The bar color, the typed enum (never flattened to a string).</param>
/// <param name="Overlay">The notch overlay.</param>
/// <param name="Flags">The bar flags.</param>
public sealed record BossBarSnapshot(
    Guid Uuid, Component Title, float Progress, BossBarColor Color, BossBarOverlay Overlay, BossBarFlags Flags)
{
    /// <summary>Projects a <see cref="BossBarSnapshot"/> from a live boss bar. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="bar"/> is null.</exception>
    public static BossBarSnapshot Project(BossBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return new BossBarSnapshot(bar.Uuid, bar.Title, bar.Progress, bar.Color, bar.Overlay, bar.Flags);
    }
}
