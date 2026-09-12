using Umpk.Geometry;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Execution;

/// <summary>One executable step of a path: a start and end point (block centers, <c>x+0.5, y, z+0.5</c>), the <see cref="MoveType"/> that produced it, its parkour profile, and the exit transition/hints the segment builder attached.</summary>
public sealed record PathSegment
{
    /// <summary>The segment start point (block center of the parent node).</summary>
    public required Vec3d Start { get; init; }

    /// <summary>The segment end point (block center of the destination node).</summary>
    public required Vec3d End { get; init; }

    /// <summary>The LOGICAL feet cell of <see cref="Start"/>: the planner's own integer node Y, which is the cell a move set, a goal and every block lookup are expressed in. Defaults to <c>floor(Start.Y)</c>, which is exact for as long as the endpoint carries that integer.</summary>
    /// <remarks>
    /// <para><b>Why this exists.</b> <see cref="Start"/> and <see cref="End"/> are POSITIONS: once <see cref="PathSegmentBuilder"/> resolves a partial support's true elevation into them, a destination on a bottom slab reads <c>End.Y = 60.5</c> where the planner's node was cell 61. Flooring that position to get a cell then reads cell 60 - one below the node every predicate in the search used - and the consumers that do so are not obviously position-based: <c>BreathValidator</c>'s submerged test is <c>IsSubmerged(x, floor(y), z)</c>, whose own definition is "the cell ABOVE the feet cell is water", so a one-cell shift moves it from the head cell to the feet cell. On a bottom-slab floor under a one-deep sheet with dry air above, the integer convention reads cell 62 = air and answers <c>False</c>; the resolved one reads cell 61 = water and answers <c>True</c>, i.e. it drowns a player standing in a puddle. The same shift changes the arrival reserve from 19.1 ticks to 21.72.</para>
    /// <para>So the cell is carried explicitly rather than recovered from the position. A hand-built segment, or any segment whose endpoints are still the planner's integers, gets the same answer from the fallback as it always did; a resolved one states the cell it came from.</para>
    /// </remarks>
    public int StartFeetY
    {
        get => _startFeetY ?? (int)Math.Floor(Start.Y);
        init => _startFeetY = value;
    }

    /// <summary>The LOGICAL feet cell of <see cref="End"/>, the planner's integer node Y. See <see cref="StartFeetY"/> for why an endpoint's own Y cannot be floored to recover it.</summary>
    public int EndFeetY
    {
        get => _endFeetY ?? (int)Math.Floor(End.Y);
        init => _endFeetY = value;
    }

    /// <summary>How far ABOVE <see cref="End"/>'s own elevation the body may legitimately come to rest while its centre is still inside the destination column: the highest support among the destination's COPLANAR neighbours, less the destination's own. Zero over uniform terrain, and zero on a segment nobody resolved.</summary>
    /// <remarks>
    /// <para><b>The straddle, made explicit.</b> A body is 0.6 wide and a cell is 1 wide, so a body whose centre is anywhere in a column has its footprint over that column's neighbours as well, and collision resolution rests it on whichever of them is highest. On a bottom slab abutting a full block one higher, a centre at x = 2.7 settles at 1.5, while a centre at x = 2.75 settles at 2.0, and both are inside the slab's column.</para>
    /// <para>The completion gates need this slack, but the endpoint does not. Across 24 approach positions for a one-block ascent onto that slab, 11 land with the centre inside the column. Five land on the slab at 1.5 and 6 on the stone at 2.0. A gate anchored on either elevation alone refuses about half of a perfectly good ascend. But <see cref="End"/> is also what the templates STEER at - <c>AscendTemplate</c> presses Jump for as long as <c>dy &gt; 0.1</c> - so aiming it at the neighbour's 2.0 makes the bot hop in place forever after landing on the slab. The endpoint stays the column's own elevation and the gates widen UPWARD by this much.</para>
    /// <para><b>Coplanar only, and that bound is load-bearing.</b> A neighbour one cell higher is a wall face during the landing, not a floor the same landing can end on, and counting it would widen the band by a whole block on every staircase - which is exactly the discrimination <c>AscendTemplate.IsBetterLanding</c>'s elevation key exists to keep. A staircase tread's coplanar neighbours are air, so this stays 0 there and the key is untouched.</para>
    /// </remarks>
    public double EndElevationSlack { get; init; }

    /// <summary>The segment's length in PLANNER blocks: horizontal displacement against the LOGICAL feet-cell rise, not the resolved elevation change.</summary>
    /// <remarks>The consumers of this are prices, not kinematics - <c>BreathValidator.RealTicks</c> turns it into ticks of lung at a medium's rate, and <c>SegmentBudgetPolicy.ExpectedTicks</c> turns it into a tick budget - and both of those are re-derivations of what the SEARCH charged, which the search charged on its integer grid. Reading the resolved rise instead would quietly discount every wet ascend onto a partial support (a one-block step onto a bottom slab measures 1.118 blocks resolved against 1.414 logical, a 21% cut in the breath it is priced at), which is the optimistic direction on the one axis that kills the player.</remarks>
    public double PlannedBlocks
    {
        get
        {
            double dx = End.X - Start.X;
            double dz = End.Z - Start.Z;
            double dy = EndFeetY - StartFeetY;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
    }

    /// <summary>The move that produced this segment.</summary>
    public required MoveType MoveType { get; init; }

    /// <summary>The parkour profile of the move.</summary>
    public ParkourProfile ParkourProfile { get; init; } = ParkourProfile.None;

    /// <summary>What the planner charged for this one move, in planner ticks, or <c>0</c> when the segment did not come from a search.</summary>
    /// <remarks>
    /// <para>This is the exact edge cost, not an estimate from the geometry. The search accumulates each emitted cost and does not relax closed nodes. The difference of two adjacent nodes' <c>GCost</c> is the cost of the move between them and nothing else, and <see cref="PathSegmentBuilder"/> reads it there.</para>
    /// <para>It is <c>0</c>, not a guess, for a segment nobody planned - a hand-built one in a test, or any future caller that assembles segments directly. Consumers must treat <c>&lt;= 0</c> as "unknown" and fall back to length times a nominal rate rather than believing a free move.</para>
    /// </remarks>
    public double PlannedTickCost { get; init; }

    /// <summary>Ticks the body must STAND STILL at this segment's destination, refilling its lung, before the next segment may begin. <c>0</c> for every segment that needs no pause, which is almost all of them.</summary>
    /// <remarks>
    /// <para>The planner computes <c>breatheTicks = deficit / RefillPerTick</c> at every cell whose head is out of the water, zeroes the deficit on the strength of it, and charges the wait to the g-cost so that an air detour is a decision the search can weigh. This field carries that pause into execution.</para>
    /// <para><b>Relationship to <see cref="PlannedTickCost"/>.</b> The wait is deliberately not part of the travel cost. Keeping it separate prevents a segment that costs 4 ticks to cross from receiving a travel budget based on a 45-tick pause. <see cref="PlannedTickCost"/> is the move, this is the wait after it.</para>
    /// <para>It is <c>0</c>, not a guess, for a segment nobody planned, exactly as <see cref="PlannedTickCost"/> is.</para>
    /// </remarks>
    public double BreathHoldTicks { get; init; }

    /// <summary>The doorway this segment passes through, when either of its endpoints shares a column with an open door or trapdoor panel. Null everywhere else, which is everywhere on terrain that holds no such block.</summary>
    /// <remarks>The executor aims off the panel while a crossing is set: a centred body has 0.0125 blocks of clearance on the panel side, and the measured pass band is a lateral offset of 0.5 to 0.7 (<see cref="BarrierCrossing"/>). Both the segment that ENTERS the doorway and the one that leaves it carry it, because the body's footprint is beside the panel for the whole of the cell's width and neither half of that is over when its centre reaches the middle.</remarks>
    public BarrierCrossing? Crossing { get; init; }

    /// <summary>What has to happen in the world before this segment's body may cross: a door to open, and the cell to open it from. Null on every segment that crosses terrain as it stands, which is every segment on terrain that holds no closed barrier.</summary>
    /// <remarks>The planner has already PRICED this - the edge that produced this segment carries <see cref="Umpk.Pathfinding.Core.ActionCosts.InteractLatency"/> on top of the move - so the payload is not what makes the crossing legal, it is what tells the driver above the executor which block the price was for. See <see cref="InteractionRequirement"/> for why a request rather than an action.</remarks>
    public InteractionRequirement? Interaction { get; init; }

    /// <summary>How this segment hands off to the next.</summary>
    public PathTransitionType ExitTransition { get; init; } = PathTransitionType.FinalStop;

    /// <summary>The exit criteria (heading, speed envelope, completion gates).</summary>
    public PathTransitionHints ExitHints { get; init; } = PathTransitionHints.Default;

    /// <summary>Whether the segment preserves sprint momentum into the next segment.</summary>
    public bool PreserveSprint { get; init; }

    /// <summary>The sign of the X delta from start to end (-1, 0, or 1).</summary>
    public int HeadingX => Math.Sign(End.X - Start.X);

    /// <summary>The sign of the Z delta from start to end (-1, 0, or 1).</summary>
    public int HeadingZ => Math.Sign(End.Z - Start.Z);

    /// <summary>Whether this segment is executed in water (swim) and completes on a position envelope.</summary>
    public bool IsWaterSegment => MoveType == MoveType.Swim;

    private readonly int? _startFeetY;

    private readonly int? _endFeetY;
}
