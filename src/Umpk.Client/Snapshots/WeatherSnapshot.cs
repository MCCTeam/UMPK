using Umpk.Game.World;

namespace Umpk.Client.Snapshots;

/// <summary>The server-reported weather: rain and thunder levels plus the raining flag.</summary>
/// <param name="RainLevel">The rain level, 0 (clear) to 1, vanilla-mapped from the game events (begin/end/level-change).</param>
/// <param name="ThunderLevel">The thunder level, 0 to 1, from the thunder level-change game event.</param>
/// <param name="IsRaining">Whether the server reports rain (begin/end raining game events).</param>
public sealed record WeatherSnapshot(float RainLevel, float ThunderLevel, bool IsRaining)
{
    /// <summary>Projects a <see cref="WeatherSnapshot"/> from a live world. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public static WeatherSnapshot Project(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new WeatherSnapshot(world.RainLevel, world.ThunderLevel, world.IsRaining);
    }
}
