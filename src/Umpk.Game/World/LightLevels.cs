namespace Umpk.Game.World;

/// <summary>The two light values at a block position: sky light and block light, each 0-15. Sky light is absent (reported as 0) in dimensions without skylight or where the server sent no sky data.</summary>
/// <param name="Sky">Sky light level, 0-15.</param>
/// <param name="Block">Block light level, 0-15.</param>
public readonly record struct LightLevels(byte Sky, byte Block)
{
    /// <summary>Fully dark (both channels zero).</summary>
    public static readonly LightLevels Dark = new(0, 0);
}
