using System.Globalization;
using Umpk.Geometry;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Goals;
using Umpk.Pathfinding.Moves;
using Umpk.Pathfinding.Tests.Fixtures;
using Umpk.Physics;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Pathfinding.Tests.Moves;

public sealed class BarrierToggleTests(ITestOutputHelper output)
{
    private static readonly PhysicsProfile Profile = PhysicsProfile.ForProtocol(774);

    /// <summary>Course row G5b "panelclose": a corridor at z=321 with an OPEN <c>half=bottom</c> trapdoor at (772,100,321) whose panel stands across the lane on the cell's west face. The head cell above it is clear, which is what makes the row solvable at all.</summary>
    private static FixtureWorld G5bWorld(int panelCell)
    {
        var world = new FixtureWorld();
        world.Fill(768, 99, 320, 775, 99, 322, FixtureWorld.Stone);
        world.Fill(768, 100, 320, 775, 101, 320, FixtureWorld.Stone);
        world.Fill(768, 100, 322, 775, 101, 322, FixtureWorld.Stone);
        world.Set(772, 100, 321, panelCell);
        return world;
    }

    [Theory]
    [InlineData(FixtureWorld.OpenTrapdoorBottomWest, true)]
    [InlineData(FixtureWorld.OpenTrapdoorNorth, false)]
    [InlineData(FixtureWorld.OpenDoorNorth, false)]
    [InlineData(FixtureWorld.OpenFenceGate, false)]
    [InlineData(FixtureWorld.ClosedTrapdoorBottom, false)]
    [InlineData(FixtureWorld.IronTrapdoorOpenWest, false)]
    [InlineData(FixtureWorld.Stone, false)]
    public void ClosesIntoAFloor_IsTheBottomTrapdoorAndNothingElse(int stateId, bool expected)
    {
        var world = new FixtureWorld();
        world.Set(0, 64, 0, stateId);
        CalculationContext ctx = FixtureContext.Around(world, 0, 64, 0);

        Assert.Equal(expected, MoveHelper.ClosesIntoAFloor(ctx.GetBlock(0, 64, 0)));
    }

    /// <summary>G5b plans, and the move that enters the panel's cell carries the CLOSE, verified against <c>open == false</c>.</summary>
    [Fact]
    public void G5b_Plans_AndTheEntryMoveShutsThePanel()
    {
        FixtureWorld world = G5bWorld(FixtureWorld.OpenTrapdoorBottomWest);
        var start = new BlockPos(768, 100, 321);
        var goal = new BlockPos(775, 100, 321);
        PlanningWorldView view = world.Capture(start, goal, margin: 12);

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));

        Assert.Equal(PathStatus.Success, result.Status);
        IReadOnlyList<PathSegment> segments =
            PathSegmentBuilder.FromPath(result.Path, view, PathfinderOptions.Default);
        PathSegment crossing = Assert.Single(segments, s => s.Interaction is not null);
        InteractionRequirement requirement = crossing.Interaction!.Value;
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{requirement.Kind} on {requirement.Target} from {requirement.From} expectOpen={requirement.ExpectedOpen}"));

        Assert.Equal(InteractionKind.CloseByHand, requirement.Kind);
        Assert.Equal(new BlockPos(772, 100, 321), requirement.Target);
        Assert.Equal(new BlockPos(772, 100, 321), requirement.Witness);
        Assert.False(requirement.ExpectedOpen);

        // A panel that is about to be folded flat is not a doorway to squeeze past: aligning to a free-side band that will not exist is a 40-tick hold for nothing.
        Assert.Null(crossing.Crossing);
    }

    /// <summary>G5b executes: the plan is built against the OPEN world the body sees, and the run happens in the world the close makes - the 3/16 slab the body steps over.</summary>
    [Fact]
    public void G5b_Executes()
    {
        FixtureWorld open = G5bWorld(FixtureWorld.OpenTrapdoorBottomWest);
        FixtureWorld shut = G5bWorld(FixtureWorld.ClosedTrapdoorBottom);
        var start = new BlockPos(768, 100, 321);
        var goal = new BlockPos(775, 100, 321);
        PlanningWorldView planned = open.Capture(start, goal, margin: 12);
        PlanningWorldView walked = shut.Capture(start, goal, margin: 12);

        PathResult result = PathPlanner.FindPath(planned, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);
        IReadOnlyList<PathSegment> segments =
            PathSegmentBuilder.FromPath(result.Path, planned, PathfinderOptions.Default);

        var executor = new PathExecutor(new PathExecutionContext(walked, Profile), segments);
        var engine = new PlayerPhysics(walked, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(768.5, 100.0, 321.5), -90f, 0f);
        engine.Step(MovementInput.None);

        bool shutIt = false;
        PathExecutorState state = PathExecutorState.InProgress;
        for (int i = 0; i < 600 && state == PathExecutorState.InProgress; i++)
        {
            PathExecutorTick tick = executor.Tick(engine.State);
            state = tick.State;
            if (tick.PendingInteraction is { } pending)
            {
                Assert.Equal(InteractionKind.CloseByHand, pending.Kind);
                shutIt = true;

                // The panel is GONE once it is shut, so there is no observed crossing to hand back.
                executor.NotifyInteractionSatisfied();
                continue;
            }

            engine.SetRotation(tick.Output.TargetYaw, tick.Output.TargetPitch);
            engine.Step(tick.Output.Input);
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"G5b ended {state} at ({engine.State.Position.X:F4}, {engine.State.Position.Y:F4}, {engine.State.Position.Z:F4})"));
        Assert.True(shutIt, "the executor never asked for the panel to be shut");
        Assert.Equal(PathExecutorState.Complete, state);
        Assert.InRange(engine.State.Position.X, 775.0, 776.0);
    }

    /// <summary>Course row L5 "irontrapdoorbutton": a <c>half=top</c> iron lid at (261,99,961) flush with the corridor floor, a stone button directly above it, and a three-block shaft under it.</summary>
    private static FixtureWorld L5World(int lidCell)
    {
        var world = new FixtureWorld();
        world.Fill(256, 99, 960, 263, 99, 962, FixtureWorld.Stone);
        world.Fill(256, 100, 960, 263, 101, 960, FixtureWorld.Stone);
        world.Fill(256, 100, 962, 263, 101, 962, FixtureWorld.Stone);
        world.Fill(260, 95, 960, 262, 98, 962, FixtureWorld.Stone);
        world.Fill(261, 97, 961, 261, 98, 961, FixtureWorld.Air);
        world.Set(261, 99, 961, lidCell);
        world.Set(261, 100, 961, FixtureWorld.StoneButtonWallSouth);
        return world;
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 12)]
    [InlineData(3, 15)]
    public void LidTicksToClear_DominatesTheMeasurement(int blocks, int measured)
    {
        double model = DoorActivation.LidTicksToClear(blocks);
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"lid {blocks} blocks: measured {measured}, model {model}, doorway {DoorActivation.RealTicksToClear(blocks + 1)}"));

        Assert.True(model >= measured, $"the lid model {model} is under the measured {measured}");
        Assert.True(model <= measured + 2, $"the lid model {model} is more than two ticks over {measured}");

        // The doorway model is untouched, and it is what a lid could never fit.
        Assert.True(DoorActivation.RealTicksToClear(blocks + 1) > model);
    }

    /// <summary>A stone button cannot open a doorway: the shortest crossing takes 17 model ticks plus ten observation ticks, exceeding the twenty-tick window.</summary>
    [Fact]
    public void AStoneButton_StillFitsNoDoorway()
    {
        Assert.False(DoorActivation.FitsWindow(2, DoorActivation.StoneButtonWindowTicks));
        Assert.False(DoorActivation.FitsWindow(2, DoorActivation.StoneButtonWindowTicks, lid: false));

        // ...and it does fit a one-block lid, with one tick to spare. Stated as an equality so the day InteractLatency calibrates, this line is what says which way the boundary moved.
        Assert.True(DoorActivation.FitsWindow(1, DoorActivation.StoneButtonWindowTicks, lid: true));
        Assert.Equal(19.0, DoorActivation.LidTicksToClear(1) + ActionCosts.InteractLatency, 6);
    }

    /// <summary>L5's button resolves, and the row plans.</summary>
    [Fact]
    public void L5_ResolvesItsButton_AndPlans()
    {
        FixtureWorld world = L5World(FixtureWorld.IronTrapdoorClosedTop);
        var start = new BlockPos(256, 100, 961);
        var goal = new BlockPos(261, 97, 961);
        PlanningWorldView view = world.Capture(start, goal, margin: 12);
        var ctx = new CalculationContext(view, PathfinderOptions.Default);

        Assert.True(
            DoorActivation.TryResolve(ctx, new BlockPos(261, 99, 961), out DoorActivation activation),
            "L5's stone button did not resolve as an activator for the lid it powers");
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"L5 activator {activation.Activator} stand {activation.StandCell} {activation.Kind} window {activation.WindowTicks}"));
        Assert.Equal(new BlockPos(261, 100, 961), activation.Activator);
        Assert.Equal(ActivatorKind.Button, activation.Kind);
        Assert.Equal(DoorActivation.StoneButtonWindowTicks, activation.WindowTicks);

        // The approach cell, one block short of the shaft - NOT the plate itself and not the shaft floor, both of which are in the lid's own column and both of which would price a zero-block crossing.
        Assert.Equal(new BlockPos(260, 100, 961), activation.StandCell);

        Assert.Equal(
            BarrierKind.NeedsActivator,
            MoveHelper.ClassifyBarrier(ctx, ctx.GetBlock(261, 99, 961), 261, 99, 961));

        PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);
        PathNode last = result.Path[^1];
        Assert.Equal((goal.X, goal.Y, goal.Z), (last.X, last.Y, last.Z));
    }

    /// <summary>L5 executes: press the button, step off the lid, land in the shaft.</summary>
    [Fact]
    public void L5_Executes()
    {
        FixtureWorld shut = L5World(FixtureWorld.IronTrapdoorClosedTop);
        FixtureWorld open = L5World(FixtureWorld.IronTrapdoorOpenWest);
        var start = new BlockPos(256, 100, 961);
        var goal = new BlockPos(261, 97, 961);
        PlanningWorldView planned = shut.Capture(start, goal, margin: 12);
        PlanningWorldView walked = open.Capture(start, goal, margin: 12);

        PathResult result = PathPlanner.FindPath(planned, PathfinderOptions.Default, start, new GoalBlock(goal));
        Assert.Equal(PathStatus.Success, result.Status);
        IReadOnlyList<PathSegment> segments =
            PathSegmentBuilder.FromPath(result.Path, planned, PathfinderOptions.Default);

        var executor = new PathExecutor(new PathExecutionContext(walked, Profile), segments);
        var engine = new PlayerPhysics(walked, Profile);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(256.5, 100.0, 961.5), -90f, 0f);
        engine.Step(MovementInput.None);

        bool pressed = false;
        PathExecutorState state = PathExecutorState.InProgress;
        for (int i = 0; i < 600 && state == PathExecutorState.InProgress; i++)
        {
            PathExecutorTick tick = executor.Tick(engine.State);
            state = tick.State;
            if (tick.PendingInteraction is { } pending)
            {
                Assert.Equal(InteractionKind.PressActivator, pending.Kind);
                Assert.Equal(new BlockPos(261, 99, 961), pending.Witness);
                Assert.True(pending.ExpectedOpen);
                pressed = true;
                BarrierCrossing? seen =
                    BarrierCrossing.TryResolve(walked, 261, 99, 961, out BarrierCrossing panel) ? panel : null;
                executor.NotifyInteractionSatisfied(seen);
                continue;
            }

            engine.SetRotation(tick.Output.TargetYaw, tick.Output.TargetPitch);
            engine.Step(tick.Output.Input);
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"L5 ended {state} at ({engine.State.Position.X:F4}, {engine.State.Position.Y:F4}, {engine.State.Position.Z:F4})"));
        Assert.True(pressed, "the executor never asked for the lid's button to be pressed");
        Assert.Equal(PathExecutorState.Complete, state);
        Assert.InRange(engine.State.Position.Y, 96.9, 97.1);
    }
}
