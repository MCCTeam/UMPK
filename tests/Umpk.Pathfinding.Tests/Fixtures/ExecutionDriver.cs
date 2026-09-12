using Umpk.Geometry;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Telemetry;
using Umpk.Physics;

namespace Umpk.Pathfinding.Tests.Fixtures;

/// <summary>Fixture data for <c>TickSample</c>.</summary>
internal readonly record struct TickSample(int SegmentIndex, Vec3d Position, bool OnGround);

/// <summary>A test harness that drives a <see cref="PathExecutor"/> against a real <see cref="PlayerPhysics"/> engine over a fixture world, exactly as the Navigator does: each tick it reads the engine state, ticks the executor, applies the emitted rotation, and steps the engine with the emitted input. This proves the execution templates produce input sequences that actually move the vanilla-accurate engine to each segment's target, rather than only checking template internals.</summary>
internal sealed class ExecutionDriver
{
    private readonly PlayerPhysics _engine;
    private readonly PathExecutor _executor;
    private readonly List<TickSample> _trace = [];
    private readonly List<MovementInput> _inputs = [];

    internal ExecutionDriver(
        PathExecutionContext ctx,
        IReadOnlyList<PathSegment> segments,
        Vec3d startPos,
        float startYaw = 0f,
        IPathExecutionObserver? observer = null,
        double deviationThreshold = 1.5,
        bool seedAtStartPos = false,
        StationControllerOptions? station = null)
    {
        _engine = new PlayerPhysics(ctx.World, ctx.Profile);
        _engine.SetConditions(ctx.Conditions);
        // Seed at the plan's actual first-segment start (the A* may nudge the start Y), falling back to the caller's position for an empty plan. seedAtStartPos overrides that and seeds exactly where the caller stands, which is how the flush-contact approaches are set up: the whole point of a flush start is that the body's leading faces rest on the destination column's boundary, and snapping to the block centre is the one thing that would make that case untestable.
        Vec3d seed = seedAtStartPos || segments.Count == 0 ? startPos : segments[0].Start;
        _engine.Reset(seed, startYaw, 0f);
        // Settle onto the ground for one tick so OnGround/InWater flags are populated before execution.
        _engine.Step(MovementInput.None);
        _executor = new PathExecutor(
            ctx, segments, station ?? StationControllerOptions.Default, observer, deviationThreshold);
    }

    internal PhysicsState State => _engine.State;

    /// <summary>The executor under test, so a row can read the phase machine's own counters.</summary>
    internal PathExecutor Executor => _executor;

    /// <summary>Drives exactly ONE tick and hands back what the executor said, for rows that have to inspect the tick payload rather than only the end state.</summary>
    internal PathExecutorTick Step()
    {
        _trace.Add(new TickSample(_executor.CurrentIndex, _engine.State.Position, _engine.State.OnGround));
        PathExecutorTick step = _executor.Tick(_engine.State);
        if (step.DeviationExceeded)
            DeviationSeen = true;

        if (step.State == PathExecutorState.InProgress)
        {
            _inputs.Add(step.Output.Input);
            _engine.SetRotation(step.Output.TargetYaw, step.Output.TargetPitch);
            _engine.Step(step.Output.Input);
        }

        return step;
    }

    internal bool DeviationSeen { get; private set; }

    /// <summary>Ticks on which the engine reported OnGround. Diagnostic only.</summary>
    internal int GroundedTicks { get; private set; }

    /// <summary>Ticks in water and grounded (a wade). Diagnostic only.</summary>
    internal int WadeTicks { get; private set; }

    /// <summary>Ticks in water and NOT grounded (buoyant). Diagnostic only.</summary>
    internal int BuoyantTicks { get; private set; }

    /// <summary>Ticks with the head under water. Diagnostic only.</summary>
    internal int SubmergedTicks { get; private set; }

    /// <summary>Per-tick record of which segment index was about to be ticked, where the engine stood, and whether it was on the ground, so a test can bound the path taken WITHIN one segment rather than only its end state (an overshoot that later recovers is invisible in the final position) and can name the LANDING tick rather than the last tick.</summary>
    internal IReadOnlyList<TickSample> Trace => _trace;

    /// <summary>Every input the executor actually emitted, in order, one per driven tick. The trace above records where the bot WAS; this records what it was told to do, which is the only way to assert on a bit (Sprint) that a position sample cannot distinguish from a fast walk.</summary>
    internal IReadOnlyList<MovementInput> EmittedInputs => _inputs;

    /// <summary>Runs until the executor completes/fails or the tick budget is exhausted.</summary>
    internal PathExecutorState Run(int maxTicks = 4000)
    {
        for (int tick = 0; tick < maxTicks; tick++)
        {
            _trace.Add(new TickSample(_executor.CurrentIndex, _engine.State.Position, _engine.State.OnGround));
            if (_engine.State.OnGround)
                GroundedTicks++;

            if (_engine.State.InWater)
                if (_engine.State.OnGround)
                    WadeTicks++;

                else
                    BuoyantTicks++;

            if (_engine.State.IsUnderWater)
                SubmergedTicks++;

            PathExecutorTick step = _executor.Tick(_engine.State);
            if (step.DeviationExceeded)
                DeviationSeen = true;

            if (step.State != PathExecutorState.InProgress)
                return step.State;

            _inputs.Add(step.Output.Input);
            _engine.SetRotation(step.Output.TargetYaw, step.Output.TargetPitch);
            _engine.Step(step.Output.Input);
        }

        return PathExecutorState.InProgress;
    }
}
