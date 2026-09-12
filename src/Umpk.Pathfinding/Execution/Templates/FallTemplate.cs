using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution.Templates;

/// <summary>A straight-down free fall at a fixed X/Z, waiting for the player to land at the target Y (Fall). Completes on a solid landing or a water landing near the target column. It uses no active steering, emits an empty input each tick, and reads the snapshot for the landing condition.</summary>
public sealed class FallTemplate : IActionTemplate
{
    private readonly PathExecutionContext _ctx;

    // The whole segment, not just its two endpoints. It carries what the planner charged for this move (PathSegment.PlannedTickCost), which is what a per-medium tick budget has to read; a template that kept only Start and End could never ask.
    private readonly PathSegment _segment;
    /// <summary>The segment's own tick budget, from <see cref="PathExecutionContext.Budget"/>.</summary>
    private readonly int _budget;
    private int _tickCount;
    private bool _hasFallen;
    private readonly bool _bouncyLanding;

    /// <summary>Creates a fall template for a segment.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public FallTemplate(PathExecutionContext ctx, PathSegment segment)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(segment);
        _ctx = ctx;
        _segment = segment;
        _budget = ctx.Budget.BudgetFor(segment);
        _bouncyLanding = LandsOnABouncyBlock(ctx, segment.End);
    }

    /// <inheritdoc/>
    public Vec3d ExpectedStart => _segment.Start;

    /// <inheritdoc/>
    public Vec3d ExpectedEnd => _segment.End;

    /// <inheritdoc/>
    public TemplateState Tick(in PhysicsState physics, out TemplateOutput output)
    {
        _tickCount++;
        Vec3d pos = physics.Position;

        double dx = ExpectedEnd.X - pos.X;
        double dz = ExpectedEnd.Z - pos.Z;
        double dy = pos.Y - ExpectedEnd.Y;
        double horizDistSq = (dx * dx) + (dz * dz);

        if (!physics.OnGround)
            _hasFallen = true;

        // Sneak while airborne over a bouncy pad: vanilla's own bounce cancel, and it costs nothing. Sneak inside a scaffolding column: the DESCENT itself. On that one block sneak and release swap meanings, so the same bit that cancels a bounce is the only thing that makes a fall through a scaffold happen at all. See SinksThroughAScaffold.
        var input = (_bouncyLanding && !physics.OnGround) || SinksThroughAScaffold(_ctx, pos, ExpectedEnd.Y)
            ? new MovementInput { Sneak = true }
            : MovementInput.None;
        output = TemplateOutput.From(input, physics.Yaw, physics.Pitch, _ctx, physics);

        if (_hasFallen && physics.OnGround && Math.Abs(dy) < 1.0 && horizDistSq < 1.0
            && (!_bouncyLanding || Math.Abs(physics.Velocity.Y) <= BounceSettleSpeed))
            return TemplateState.Complete;

        if (_hasFallen && physics.InWater && Math.Abs(dy) < 2.0 && horizDistSq < 1.5)
            return TemplateState.Complete;

        if (_tickCount > _budget)
            return TemplateState.Failed;

        return TemplateState.InProgress;
    }

    /// <summary>Whether this segment lands on a block that BOUNCES, and what that costs the completion gate.</summary>
    /// <remarks>
    /// <para><b>The bounce, measured.</b> A twelve-block drop onto slime on protocol 774 inverts ten times before it settles - peak vertical speeds 1.0928, 0.8580, 0.6997, 0.5519, 0.4604, 0.3946, 0.3084, 0.2378, 0.1537, 0.0809 - and the body is not stable on the pad until <b>tick 152</b>. The same drop with <c>Sneak</c> held settles at <b>tick 18</b>, which is exactly what the same drop onto hay takes, because <c>slime bounce response</c> defers to ordinary landing behavior, which zeroes vertical velocity, when the body sneaking ( <c>sneak-controlled bounce suppression</c> is the sneak key).</para>
    /// <para><b>Both arms, and why both.</b> Sneak while airborne over a bouncy pad cancels the bounce at the source and is what the template does; the settle wait is what makes the completion honest when the body is bouncing anyway - arriving mid-bounce from a replan, or from a segment the planner did not know ended on slime. Without it the gate fires on the CONTACT tick, when <c>OnGround</c> is true and the body is about to be thrown back up, and the next segment starts from a pose that no longer exists.</para>
    /// <para><b>Sneak costs no health here</b>, which is worth stating because it is easy to assume otherwise: a normal slime landing applies a zero damage multiplier, while a sneaking landing suppresses the damage call. Both branches are zero damage. The sneak trades the bounce for nothing but a slower approach, so it needs no health or food gate.</para>
    /// </remarks>
    internal static bool LandsOnABouncyBlock(PathExecutionContext ctx, Vec3d end)
        => Moves.MoveHelper.IsBouncyLanding(
            ctx.World.GetBlock(BlockPos.Containing(end.X, end.Y - BouncyLandingProbeDown, end.Z)));

    /// <summary>How far below the segment's resting elevation the landing block is probed.</summary>
    private const double BouncyLandingProbeDown = 0.2;

    /// <summary>Whether the cell containing this point is scaffolding.</summary>
    internal static bool IsScaffoldingAt(PathExecutionContext ctx, double x, double y, double z)
        => ctx.World.GetBlock(BlockPos.Containing(x, y, z)).IsScaffolding;

    /// <summary>Whether this tick should press <c>Sneak</c> to keep sinking through a scaffold, as opposed to landing ON one.</summary>
    /// <remarks>
    /// <para><b>The rule.</b> Vanilla's scaffolding descent is two conditions that both key on the sneak key: the plates vanish (arm 2's <c>!ctx.isDescending()</c>) and the climb clamp does not pin the body. So on a scaffold, and on nothing else in the game, holding sneak is how a body goes DOWN and releasing it is the brake. Measured on the fixture: released mid-descent, the body catches on the next plate within three ticks.</para>
    /// <para><b>Two arms.</b> Feet already inside the column: keep sinking until the target elevation. Feet on a scaffold's top plate: press only if the plan means to pass THROUGH that cell rather than to stand on it, which is what the <c>endY &lt;= cellY</c> term asks.</para>
    /// <para><b>What that second term is, honestly.</b> It is forward insurance, not a live guard. A <c>MoveFall</c> cannot terminate on a climbable cell: the ladder-grab arm keeps scanning and, with grabbing disabled, <c>MoveHelper.CanWalkThrough</c> accepts climbable cells before testing <c>BlocksMotion</c>. Every reachable scaffold-roof landing is therefore a <c>MoveDescend</c> driven by <see cref="DescendTemplate"/>, and removing this term leaves every offline probe byte-identical. It is kept because it costs one comparison and because it is the honest reading of the vanilla rule for the day a move does end on a plate.</para>
    /// </remarks>
    internal static bool SinksThroughAScaffold(PathExecutionContext ctx, Vec3d pos, double endY)
    {
        if (IsScaffoldingAt(ctx, pos.X, pos.Y, pos.Z))
            return pos.Y > endY + ScaffoldArrivalEpsilon;

        double probeY = pos.Y - BouncyLandingProbeDown;
        int cellY = (int)Math.Floor(probeY);
        return IsScaffoldingAt(ctx, pos.X, probeY, pos.Z) && endY <= cellY + ScaffoldArrivalEpsilon;
    }

    /// <summary>How close to the target elevation counts as arrived for the sink arm. One hundredth of a block, well inside the 0.15 the clamp moves the body in a single tick.</summary>
    private const double ScaffoldArrivalEpsilon = 0.01;

    /// <summary>The vertical speed at or under which a body on a bouncy pad counts as settled. A resting body jitters at 0.0398 on slime (the bounce inverts the gravity tick) and at 0.0784 on an ordinary floor; the smallest measured bounce that lifts the body meaningfully is 0.1537.</summary>
    internal const double BounceSettleSpeed = 0.1;
}
