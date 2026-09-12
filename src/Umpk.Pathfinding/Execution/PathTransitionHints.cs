namespace Umpk.Pathfinding.Execution;

/// <summary>The exit criteria the segment builder attaches to a segment: desired exit heading, speed envelope, and completion gates. <see cref="AllowUngrounded"/> lets water segments complete on a position envelope rather than requiring <c>OnGround</c>.</summary>
public sealed record PathTransitionHints(
    int DesiredHeadingX,
    int DesiredHeadingZ,
    double MinExitSpeed,
    double MaxExitSpeed,
    bool RequireStableFooting,
    bool RequireGrounded,
    bool RequireJumpReady,
    bool AllowAirBrake,
    int HorizonTicks,
    bool AllowUngrounded = false)
{
    /// <summary>The permissive default hints (no footing requirement, generous speed cap).</summary>
    public static PathTransitionHints Default { get; } = new(
        DesiredHeadingX: 0,
        DesiredHeadingZ: 0,
        MinExitSpeed: 0.0,
        MaxExitSpeed: double.PositiveInfinity,
        RequireStableFooting: false,
        RequireGrounded: false,
        RequireJumpReady: false,
        AllowAirBrake: false,
        HorizonTicks: 8);
}
