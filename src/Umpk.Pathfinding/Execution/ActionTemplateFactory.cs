using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution.Templates;

namespace Umpk.Pathfinding.Execution;

/// <summary>Maps a <see cref="PathSegment"/> to the action template that executes it. <see cref="MoveType.Swim"/> selects a <see cref="SwimTemplate"/> instead of falling through to the throwing default.</summary>
/// <remarks>The routing follows the CLASSIFICATION, and the classification is no longer the node-Y delta: a step whose two support elevations differ by no more than <c>BlockSupport.PlayerStepHeight</c> is emitted as a <see cref="MoveType.Traverse"/> by <c>MoveHelper.IsWalkedStep</c> and therefore arrives here as a <see cref="WalkTemplate"/>, which is the point of classifying it: <see cref="AscendTemplate"/> presses Jump whenever <c>dy &gt; 0.1</c>, so a slab kerb routed to it bunny-hops where vanilla walks, and <see cref="DescendTemplate"/> brakes for a landing that never happens.</remarks>
public static class ActionTemplateFactory
{
    /// <summary>Creates the template for a segment.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The segment's move type has no template.</exception>
    public static IActionTemplate Create(PathExecutionContext ctx, PathSegment segment)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(segment);

        return segment.MoveType switch
        {
            MoveType.Traverse or MoveType.Diagonal => new WalkTemplate(ctx, segment),
            MoveType.Ascend => new AscendTemplate(ctx, segment),
            MoveType.Descend => new DescendTemplate(ctx, segment),
            MoveType.Fall => new FallTemplate(ctx, segment),
            MoveType.Climb => new ClimbTemplate(ctx, segment),
            MoveType.Parkour => new SprintJumpTemplate(ctx, segment),
            MoveType.Swim => new SwimTemplate(ctx, segment),
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment.MoveType, "No template for move type."),
        };
    }
}
