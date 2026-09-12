---
title: Umpk.Pathfinding
description: An A* planner over a captured world region, plus per-move execution templates that drive the physics engine.
sidebar:
  order: 10
---

`Umpk.Pathfinding` is two halves that meet in the middle. The planner is a session-free A* search over a detached snapshot of the world that produces a list of block positions and the move type used to reach each one. The executor turns that list into per-tick `MovementInput` values by running a small template per move and checking the result against what the physics engine actually did.

The split matters. Planning is pure and can run off the session loop against a frozen region. Execution is a feedback loop and has to run on the tick.

## Its place in the stack

`Umpk.Pathfinding` depends on [Umpk.Core](/packages/umpk-core), [Umpk.Game](/packages/umpk-game) and [Umpk.Physics](/packages/umpk-physics). It has no idea a network exists. [Umpk.Client](/packages/umpk-client) wraps it in `Navigator` and exposes it as `client.Actions.Movement.NavigateAsync`.

## Main entry points

`PlanningWorldView.Capture(world, shapes, a, b, margin)` copies a box of blocks out of a live [Umpk.Game](/packages/umpk-game) `World` into a `RegionSnapshot` and wraps it. That snapshot is what the planner reads, and it does not change under you while the search runs.

`PathPlanner.FindPath` and `FindPathAsync` are the front door: a world view, a `PathfinderOptions`, a start `BlockPos` and an `IGoal`, returning a `PathResult`.

`PathResult` carries `Status` (`Success`, `Partial`, `Failed`), `Path` as a list of `PathNode`, `Moves` as the parallel list of `MoveType`, `Cost`, and a `PathDiagnostics` with `NodesExplored`, `ElapsedMilliseconds`, `UnloadedChunkHits`, `TimedOut` and `NodeBudgetExhausted`.

`PathfinderOptions` is a record with `PathfinderOptions.Default` and the switches you would expect: `AllowSprint`, `AllowParkour`, `AllowParkourAscend`, `AllowSwim`, `AllowClimb`, `AllowDiagonalDescend`, `AllowLadderGrabDuringFall`, `MaxFallHeight`, `MaxFallHeightIntoWater`, `JumpPenalty`, `MaxNodes`, `MaxReplans`, `Timeout` and `BlocksToAvoid`.

Goals live in `Umpk.Pathfinding.Goals`: `GoalBlock` for an exact block, `GoalXZ` for a column at any height, `GoalNear` for a radius, `GoalComposite` for any-of, and `EntityGoal` for a moving target with a `ShouldReplan` test. All implement `IGoal`, which is two methods: `IsInGoal` and `Heuristic`.

`AStarPathFinder` and `CalculationContext` are the layer under `PathPlanner`, exposed for when you want your own move set. `AStarPathFinder.BuildDefaultExpanders()` gives the standard one. `Umpk.Pathfinding.Moves` holds the individual moves (`MoveJump`, `MoveDescend`, `MoveFall`, `MoveClimb`, `MoveSwim`, `MoveSwimExit`, `MoveSwimVertical`, `MoveSprintDescend`) and the expanders.

`Umpk.Pathfinding.Execution` is the second half. `PathSegmentBuilder.FromPath` turns path nodes into `PathSegment` values. `PathExecutor` takes those segments and a `PathExecutionContext` (a physics world view, a profile and conditions) and gives you a `PathExecutorTick` per call to `Tick`, carrying a `TemplateOutput` with the `MovementInput`, target yaw and pitch for that tick. `Umpk.Pathfinding.Execution.Templates` has the per-move templates: `WalkTemplate`, `AscendTemplate`, `DescendTemplate`, `FallTemplate`, `ClimbTemplate`, `SprintJumpTemplate` and `SwimTemplate`.

## Example

Planning, adapted from `tests/Umpk.Pathfinding.Tests/PlannerSmokeTests.cs`:

```csharp
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;
using Umpk.Pathfinding.Goals;

var start = new BlockPos(0, 65, 0);
var target = new BlockPos(40, 68, -12);

// Freeze a region big enough to contain both ends plus room to route around obstacles.
PlanningWorldView view = PlanningWorldView.Capture(world, shapes, start, target, margin: 16);

PathResult result = PathPlanner.FindPath(view, PathfinderOptions.Default, start, new GoalBlock(target));

if (result.Status == PathStatus.Failed)
{
    Console.WriteLine($"No route. Explored {result.Diagnostics.NodesExplored} nodes.");
    return;
}

for (int i = 0; i < result.Path.Count; i++)
{
    PathNode node = result.Path[i];
    Console.WriteLine($"{node.X},{node.Y},{node.Z} via {node.MoveUsed}");
}
```

Execution, one tick at a time:

```csharp
using Umpk.Pathfinding.Execution;
using Umpk.Physics;

var context = new PathExecutionContext(physicsWorld, profile, PhysicsConditions.Default);
var executor = new PathExecutor(context, PathSegmentBuilder.FromPath(result.Path));

while (!executor.IsComplete)
{
    PathExecutorTick tick = executor.Tick(engine.State);
    if (tick.State == PathExecutorState.Failed)
    {
        break;
    }

    engine.SetRotation(tick.Output.TargetYaw, tick.Output.TargetPitch);
    engine.Step(tick.Output.Input);
}
```

## Things that catch people out

The planner works on a frozen snapshot, so a path is only as fresh as the capture. Terrain that changes after `Capture` is invisible to the search. For a moving target, use `EntityGoal` and its `ShouldReplan` test. Capture the region again every time you replan.

`margin` on `Capture` is not a nicety. The snapshot is a box, and the planner cannot route outside it. A route that has to detour around a wall needs the box to contain the detour, and a margin that is too small turns a solvable problem into `PathStatus.Failed`. `PathDiagnostics.UnloadedChunkHits` counting up is the signal that the search kept walking into the edge of what it could see.

`PathStatus.Partial` is a real answer, not a failure. It means the planner ran out of budget or time and is handing you the best prefix it found. Walk the partial path. Then plan again from where the walk ended. Treating `Partial` as failure gives up on long routes that would have worked in two hops.

`PathResult.Path` and `PathResult.Moves` are parallel lists, and `PathNode.MoveUsed` says the same thing per node. Both are the move used to arrive at that node, not the move used to leave it.

`PathNode` is a mutable class with public fields used by the search (`GCost`, `HCost`, `Parent`, `HeapIndex`, `IsOpen`, `IsClosed`). Do not mutate the nodes in a returned path. The nodes are the search's own objects.

The executor is not fire and forget. It compares the physics state you hand it against where the current template expected the player to be, and reports `DeviationExceeded` when they drift apart past the threshold (1.5 blocks by default, settable in the `PathExecutor` constructor). That is your cue to replan, not to keep pushing inputs at a player who has fallen off the route.

`PathExecutorTick.Output.Input` is a `MovementInput` for one tick. Several templates steer by facing rather than by strafing, so the order matters. For each tick:

1. Apply `Output.TargetYaw` and `Output.TargetPitch` to the physics engine.
2. Pass `Output.Input` to `PlayerPhysics.Step`.

Execution templates are era-sensitive through the `PhysicsProfile` you put in the `PathExecutionContext`. A jump that clears a gap on 1.21 may not clear it on 1.8. Use the profile for the version you are actually connected to, not `PhysicsProfile.Modern`. See [movement and pathfinding](/guides/movement-and-pathfinding).

`PathfinderOptions.BlocksToAvoid` is a set of `Identifier`, matched against block identifiers. It makes the planner route around, not refuse: a goal only reachable through an avoided block still fails.
