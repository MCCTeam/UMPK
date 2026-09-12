using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Execution.Templates;

/// <summary>
/// Climbs a ladder, vine, or scaffold up or down by one block.
/// <para>Climbing activates through either horizontal collision or jump input. Holding jump alone lifts a climber at <c>(0.2 - 0.08) * 0.98 = 0.1176</c> blocks per tick without pushing it out of the shaft. Forward input is used only to correct a real horizontal error.</para>
/// <para>Descent speed is clamped to -0.15 and sneak suppresses sliding, so sneak is the brake on ladders and vines.</para>
/// <para>Scaffolding reverses those controls: its plate disappears while descending, so sneak descends and releasing it stops on a plate.</para>
/// <code>
///                       ladder / vine       scaffolding
/// descend one rung      release everything  hold Sneak stop on a rung        hold Sneak          release
/// </code>
/// <para>Both <c>Sneak</c> presses are therefore gated on the feet cell.</para>
/// </summary>
public sealed class ClimbTemplate : IActionTemplate
{
    /// <summary>
    /// The height window, above the destination rung's floor, that counts as arrived. It is vanilla's own climbable clamp (<c>0.15</c>), which makes the window exactly one tick of climbable travel wide, so it is reachable from both directions: an ascent rises 0.1176 per tick and a descent falls 0.15, and neither can step over a window as wide as its own step.
    /// <para>The window is one-sided so the executor cannot complete below the destination rung while the body is still moving in the cell below.</para>
    /// </summary>
    private const double ArrivalWindow = PhysicsConstants.ClimbMaxSpeed;

    /// <summary>Squared horizontal error at which the climb steers back to the column centre (0.1 blocks). Below it the bot is centred enough that a press would do more harm than good: the column is 0.4 blocks wider than the player's footprint, so 0.1 of slack costs nothing.</summary>
    private const double RecentreErrorSq = 0.01;

    /// <summary>Ticks a single one-block climb may take before the segment is failed.</summary>

    private readonly PathExecutionContext _ctx;

    // The whole segment, for the same reason FallTemplate keeps one: PathSegment.PlannedTickCost is what a per-medium tick budget reads, and two endpoints cannot answer that question.
    private readonly PathSegment _segment;
    /// <summary>The segment's own tick budget, from <see cref="PathExecutionContext.Budget"/>.</summary>
    private readonly int _budget;
    private readonly bool _goingUp;
    private int _tickCount;

    /// <summary>Creates a climb template for a segment.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public ClimbTemplate(PathExecutionContext ctx, PathSegment segment)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(segment);
        _ctx = ctx;
        _segment = segment;
        _budget = ctx.Budget.BudgetFor(segment);
        _goingUp = segment.End.Y > segment.Start.Y;
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
        double horizErrorSq = (dx * dx) + (dz * dz);
        double heightAboveRung = pos.Y - ExpectedEnd.Y;

        float pitch = SegmentGeometry.SmoothPitch(physics.Pitch, _goingUp ? -70f : 70f);
        float yaw = physics.Yaw;

        // Arrival needs BOTH halves. On the rung, because a climb that stops short leaves the bot hanging in the shaft with nothing under it; and the whole footprint inside the column, because a bot whose footprint straddles the column edge is already on its way out of it. The feet cell, which is what decides whether a Sneak press this tick means "hold this height" or "sink a whole cell". On scaffolding the two meanings are inverted; see the class remarks.
        bool scaffold = FallTemplate.IsScaffoldingAt(_ctx, pos.X, pos.Y, pos.Z);

        bool onRung = heightAboveRung >= 0.0 && heightAboveRung <= ArrivalWindow;
        if (onRung && SegmentGeometry.IsFootprintInsideTargetBlock(pos, ExpectedEnd))
        {
            output = TemplateOutput.From(HoldInput(physics, scaffold), yaw, pitch, _ctx, physics);
            return TemplateState.Complete;
        }

        if (_tickCount > _budget)
        {
            output = TemplateOutput.From(MovementInput.None, yaw, pitch, _ctx, physics);
            return TemplateState.Failed;
        }

        var input = new MovementInput();
        if (physics.OnClimbable)
        {
            if (heightAboveRung < 0.0)
            {
                // Below the rung: the jumping arm of vanilla's climb condition, and nothing else.
                input = input with { Jump = true };
            }
            else if (heightAboveRung > ArrivalWindow)
            {
                // Above the rung on a ladder or a vine: release everything and let the -0.15 clamp bring the body back down. This is the arm that recovers an overshoot, which a climb that has spent ticks turning to recentre will always have.
                //
                // Above the rung INSIDE A SCAFFOLD, releasing does nothing at all: the plate the body is resting on is only absent while it is descending, so letting go re-materialises the floor. There the descent is Sneak. Gated on !_goingUp because an ascent that has overshot wants the ladder arm's behaviour - the plate below is what will catch it.
                if (scaffold && !_goingUp)
                    input = input with { Sneak = true };

            }
            else if (!scaffold)
            {
                // In the window but not yet in the column: hold the height with sneak and spend the ticks on the recentre instead of climbing past the rung.
                //
                // Gated OFF on scaffolding. After the climb-clamp exemption a Sneak press with the feet cell scaffolding is an instruction to sink a whole cell, so the press that holds a
                // ladder climber's height would drop a scaffold climber's. The recentre below still runs;
                // it just runs without the brake, because there the plate is the brake.
                input = input with { Sneak = true };
            }
        }

        if (horizErrorSq > RecentreErrorSq)
        {
            // Snap the yaw rather than smoothing it. A climber has no momentum to protect, and every tick spent rotating at 35 degrees is another tick of drift: smoothing a 180 degree correction cost five ticks and a whole extra rung of overshoot on protocol 340.
            yaw = SegmentGeometry.CalculateYaw(dx, dz);
            input = input with { Forward = true };
        }

        output = TemplateOutput.From(input, yaw, pitch, _ctx, physics);
        return TemplateState.InProgress;
    }

    /// <summary>The input that keeps the bot on the rung it just reached. Sneak zeroes the climbable descent which is the only thing holding a player on a rung with nothing under it; standing on solid ground needs no input at all.</summary>
    /// <remarks><paramref name="scaffold"/> inverts it. Inside a scaffolding column the same press is what makes the plates vanish AND what the clamp exempts, so "park here" written as Sneak would be an instruction to slide down the rest of the column. There the parking brake is releasing: the plate comes back the instant the body stops descending, measured at three ticks on the fixture.</remarks>
    private static MovementInput HoldInput(in PhysicsState physics, bool scaffold)
        => physics.OnClimbable && !physics.OnGround && !scaffold
            ? new MovementInput { Sneak = true }
            : MovementInput.None;
}
