using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding;

/// <summary>Caller-visible planning capabilities and tuning. Every value is an <c>init</c> so a caller composes an options record once and passes it to <see cref="PathPlanner"/> or the execution driver.</summary>
public sealed record PathfinderOptions
{
    /// <summary>Whether sprinting moves and sprint-jumps are allowed.</summary>
    public bool AllowSprint { get; init; } = true;

    /// <summary>Whether the sprint-jump (parkour) move family is allowed at all.</summary>
    public bool AllowParkour { get; init; } = true;

    /// <summary>Whether upward parkour (positive y-delta sprint jumps) is allowed.</summary>
    public bool AllowParkourAscend { get; init; } = true;

    /// <summary>Whether swimming moves through water are allowed.</summary>
    public bool AllowSwim { get; init; } = true;

    /// <summary>Whether the search carries the air deficit as a banded dimension of the node key and refuses a move the player could not survive.</summary>
    /// <remarks>
    /// <para>On by default, and the difference it makes is routing rather than refusing: <c>BreathValidator</c> already refuses an unsurvivable plan after the fact, but it can only judge the ONE route the search returned. It cannot route through an air pocket that is off the shortest line, because choosing where to detour is a search and doing it afterwards is running this badly.</para>
    /// <para>Turn it off for a fast path where the route is known dry, or to measure what the dimension costs. Dry terrain is unaffected either way: with no water anywhere the deficit is 0 at every node, every node bands to 0, and the key is exactly the key it was.</para>
    /// </remarks>
    public bool BreathAware { get; init; } = true;

    /// <summary>Whether ladder/vine climbing moves are allowed.</summary>
    public bool AllowClimb { get; init; } = true;

    /// <summary>Whether the planner may grab a ladder/vine mid-fall to arrest a long drop.</summary>
    public bool AllowLadderGrabDuringFall { get; init; } = true;

    /// <summary>Whether diagonal descend steps are allowed (a corner drop of one block).</summary>
    public bool AllowDiagonalDescend { get; init; } = true;

    /// <summary>The maximum safe fall height onto solid ground, in blocks.</summary>
    public int MaxFallHeight { get; init; } = 3;

    /// <summary>The maximum fall height when the landing is water (water absorbs fall damage).</summary>
    public int MaxFallHeightIntoWater { get; init; } = 20;

    /// <summary>The additive cost, in ticks, charged for a jump takeoff.</summary>
    public double JumpPenalty { get; init; } = ActionCosts.JumpPenalty;

    /// <summary>The wall-clock planning budget. Enforced against an injected time source, never a wall-clock read inside the loop.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The maximum number of A* nodes expanded before the search aborts with a partial path.</summary>
    public int MaxNodes { get; init; } = 800_000;

    /// <summary>The maximum number of replans the execution driver performs before giving up.</summary>
    public int MaxReplans { get; init; } = 5;

    /// <summary>Block identifiers the planner treats as hazards and never walks through or onto (hazard extension point).</summary>
    public IReadOnlySet<Identifier> BlocksToAvoid { get; init; } = EmptyBlocks;

    /// <summary>Whether a block whose collision shape holds a centred body BELOW the cell's top - a slab, a snow layer, a dirt path, a honey block, a lily pad, a stair, a campfire - counts as standable. Default TRUE.</summary>
    /// <remarks>
    /// <para>The node Y is a logical feet cell. <c>PathSegmentBuilder</c> resolves the endpoint to the support's actual elevation, so partial-height supports do not require wider completion gates.</para>
    /// <para>Setting this to false narrows the graph to full unit cubes. <c>MoveHelper.CanWalkOn</c> keeps its own bounds either way, refusing a support of exactly zero (<c>snow[layers=1]</c>, the empty shape) and anything reaching past its own cell (a fence post and a closed fence gate, both 1.5).</para>
    /// </remarks>
    public bool AllowPartialHeightSupport { get; init; } = true;

    /// <summary>Whether the plan may open a hand-openable door, trapdoor or fence gate on its way through, at <see cref="ActionCosts.InteractLatency"/> a crossing.</summary>
    /// <remarks>
    /// <para>Default true, because it is what vanilla's own mob pathfinder assumes of a villager and what a player does without thinking. Setting it false narrows the graph back to the barriers that are already open. This is what a caller wants when it must not change the world it walks through - a spectator, a route being previewed, or a plot whose doors belong to somebody else.</para>
    /// <para>It never makes an <c>iron_door</c> or an <c>iron_trapdoor</c> passable: those blocks cannot be opened by hand and need a redstone activator rather than a hand.</para>
    /// </remarks>
    public bool AllowDoorInteraction { get; init; } = true;

    private static readonly IReadOnlySet<Identifier> EmptyBlocks = new HashSet<Identifier>();

    /// <summary>The default option set (all capabilities enabled with vanilla-calibrated limits).</summary>
    public static PathfinderOptions Default { get; } = new();

    /// <summary>The world height span, and so the largest drop expressible inside a world: the bound <see cref="UnsafeFalls"/> uses instead of an unbounded value.</summary>
    /// <remarks>The descend moves scan downward one block at a time up to the fall limit (<c>MoveDescend</c>, <c>MoveSprintDescend</c>), so the limit has to stay finite or the scan does not terminate in any practical time. -64 to 320 is the 1.18+ build range.</remarks>
    public const int WorldHeightSpan = 384;

    /// <summary><see cref="Default"/> with the fall-damage guards lifted: any drop inside the world is planable, onto ground or into water.</summary>
    /// <remarks>This is what a caller asks for when it wants a route the safe limits refuse, and it means exactly what it says: the resulting plan can kill the player. Nothing here makes a fall survivable, it only stops the planner from refusing to consider one. Planning is also slower because each descend candidate may scan up to <see cref="WorldHeightSpan"/> blocks; <see cref="Timeout"/> and <see cref="MaxNodes"/> still bound the search.</remarks>
    public static PathfinderOptions UnsafeFalls { get; } = new()
    {
        MaxFallHeight = WorldHeightSpan,
        MaxFallHeightIntoWater = WorldHeightSpan,
    };
}
