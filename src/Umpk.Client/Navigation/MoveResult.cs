using Umpk.Geometry;

namespace Umpk.Client.Navigation;

/// <summary>Whether a verified move actually arrived, as a value rather than a boolean the caller has to remember the polarity of.</summary>
public enum MoveOutcome
{
    /// <summary>The player came to rest somewhere other than the requested destination.</summary>
    StoppedShort = 0,

    /// <summary>The player arrived, per <see cref="Navigator.Judge"/>'s verdict for the request kind.</summary>
    Reached = 1,

    /// <summary>The player came to rest NEXT TO the requested destination because no body can occupy the destination block itself. Not an arrival, and deliberately not the same thing as <see cref="StoppedShort"/>.</summary>
    /// <remarks>
    /// The two failures a caller has to be able to tell apart are "I cannot stand there" and "I cannot get there". This is the first: the route was found and executed, the body is resting in the nearest cell the planner can finish in, and the only reason the request is unsatisfied is that the cell the caller named is not a place a body fits (a solid block, a ladder rung, the floor under a stalactite whose spike overlaps the head band). <see cref="StoppedShort"/> keeps its original meaning: a route that did not deliver the body where it was supposed to.
    /// <para><see cref="MoveResult.Reached"/> stays FALSE for this outcome. Getting close is not arriving, and a caller that reads only the boolean keeps behaving exactly as it did.</para>
    /// </remarks>
    StoppedNear = 2,
}

/// <summary>The verdict for a verified move: what was asked for, where the player actually ended up, and whether that counts as an arrival. A completed move TASK is not an arrival; see <see cref="Navigator.Judge"/> for why the request is judged one of two different ways depending on <see cref="SubBlockRequest"/>.</summary>
/// <param name="Target">The requested destination point.</param>
/// <param name="StoppedAt">Where the player actually came to rest.</param>
/// <param name="SubBlockRequest">Whether the request never left the block it started in (see <see cref="Navigator.IsSubBlockRequest"/>), which selects the distance-based verdict over the block-level one.</param>
/// <param name="Reached">The verdict itself.</param>
/// <param name="HorizontalDistance">The horizontal (X/Z) distance from <see cref="StoppedAt"/> to <see cref="Target"/>. Always populated, horizontal only, because that is what the approach that produces a sub-block arrival can actually control; see <see cref="Navigator.Judge"/>.</param>
/// <param name="DestinationUnstandable">Whether the loaded world says no body can occupy the destination block. Set only on a verdict that did NOT reach, and only by <see cref="Navigator.MoveToVerifiedAsync(Vec3d, System.Threading.CancellationToken)"/>, which is the one caller with a session to ask the world with; <see cref="Navigator.Judge"/> is pure and cannot know it. Defaults to false, so every existing construction site and <c>Judge</c> itself are unchanged.</param>
public sealed record MoveResult(
    Vec3d Target,
    Vec3d StoppedAt,
    bool SubBlockRequest,
    bool Reached,
    double HorizontalDistance,
    bool DestinationUnstandable = false)
{
    /// <summary>The verdict as a value; see <see cref="Reached"/> for the boolean it is derived from.</summary>
    /// <remarks>
    /// A <see cref="SubBlockRequest"/> can never read <see cref="MoveOutcome.StoppedNear"/>, and the one case where it could is the case where the answer would be a lie. Such a request never leaves the block the body occupies, so the destination block IS the occupied block, and the only way that block fails the occupancy test is a body already inside terrain (teleported into a wall, or resting under a spike that takes its head cell). A host would then print "the walk stopped in the nearest block that can hold a body" and name the very cell the body never left.
    /// <para>The rule lives here rather than at the one call site that sets the flag because it is TOTAL: it holds for every host and every way the flag could be set, and it is a pure function of the record, so it is pinned by a test that constructs one rather than by a fixture that would have to contrive a body unable to walk half a block inside its own cell. The fixture cannot: a body teleported inside the solid pillar walks to the centre and the verdict comes back Reached at 0.0061, so a navigator-level guard passes with the rule removed.</para>
    /// </remarks>
    public MoveOutcome Outcome => Reached
        ? MoveOutcome.Reached
        : DestinationUnstandable && !SubBlockRequest ? MoveOutcome.StoppedNear : MoveOutcome.StoppedShort;
}
