---
title: "Movement and pathfinding"
description: "The tick-accurate physics engine, its per-era profiles, the A* planner and its execution templates, and what a caller actually drives."
sidebar:
  order: 3
---

Two packages, and they stack. `Umpk.Physics` reproduces vanilla's per-tick movement, including the parts that changed between versions. `Umpk.Pathfinding` plans a route through blocks and turns each step of it into the inputs that engine expects. `Umpk.Client` wires both together and hides them behind two async methods.

Which of those three layers you want depends on how much control you need.

## Per-era behavior, in practice

The same input produces different movement on 1.8 than on 1.21, and this is not a rounding difference. A profile carries eight axes:

```csharp
public sealed record PhysicsProfile
{
    public required FluidMovementEra FluidMovement { get; init; }
    public required WaterTravelEra WaterTravel { get; init; }
    public required bool WaterClimbBumpAvailable { get; init; }
    public required bool ElytraAvailable { get; init; }
    public required bool SwimPoseAvailable { get; init; }
    public required bool CrawlPoseAvailable { get; init; }
    public required PoseDimensionsEra PoseDimensions { get; init; }
    public required PositionSendCadence PositionSend { get; init; }
}
```

Every one of those is `required`, so an object initializer must set all eight. Concretely: on 1.8 there is no elytra and no swim pose, the crouch height is 1.65 rather than 1.5, and the ledge hop out of water does not exist. Sprint changes water travel from 1.13. Crawling arrives at 1.14 and moves the pose dimensions with it.

The engine never compares a protocol number to decide any of this. The axes come from the dataset, via the version's `ProtocolFeatures`. See [era gating](../concepts/era-gating.md) for why that matters more than it might seem.

Two computed properties fall out of the axes: `CrouchHeight` (1.5 modern, 1.65 legacy) and `SwimHeight`. `StandingHeight` is 1.8 and `Width` is 0.6 on every version.

### Getting a profile

There are two public selectors, and neither is quite the one production uses.

```csharp
public static PhysicsProfile Modern { get; }
public static PhysicsProfile ForProtocol(int protocolVersion);
public static PhysicsProfile FromFeatures(
    bool swimmingUpdate, bool sprintAwareWaterTravel, bool waterClimbBump, bool elytraAvailable,
    bool swimPoseAvailable, bool crawlPoseAvailable, bool modernCrouchHeight,
    bool dirtyCheckedPositionCadence);
```

`ForProtocol` is a curated table with four breakpoints, and its own doc comment says it exists for tests and bootstrap. It carries a deliberate divergence: protocols 47 through 392 get `FluidMovementEra.Legacy` there, while the dataset gives them `SwimmingUpdate`, which is the correct answer and the one production uses. So `ForProtocol` is an approximation, fine for a fixture, wrong for a claim about 1.8 water.

The production selector reads `ProtocolFeatures` and calls `FromFeatures`, but it is internal to `Umpk.Client`, so you cannot call it. If you are building your own engine and want the real per-version answer, call `FromFeatures` yourself with the eight flags derived from the version you negotiated.

`PositionSend` is advisory. The engine sends no packets at all.

## Driving the engine yourself

```csharp
public PlayerPhysics(IPhysicsWorldView world, PhysicsProfile profile);
public StepResult Step(in MovementInput input);
```

You supply the world through one small interface:

```csharp
public interface IPhysicsWorldView
{
    BlockState GetBlock(BlockPos pos);
    ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state);
    bool IsChunkLoaded(BlockPos pos);
    void CollectEntityColliders(in Aabb region, ICollection<Aabb> into) { }
}
```

Three required members. `CollectEntityColliders` is a default interface method whose default body does nothing, so implement it only when you want entities to be solid. `GetCollisionShapes` returns boxes in local block coordinates, 0 to 1; `CollectEntityColliders` supplies world coordinates. Getting those two frames mixed up is the easy mistake here.

`MovementInput` is a record struct of booleans: `Forward`, `Back`, `Left`, `Right`, `Jump`, `Sneak`, `Sprint`, `AutoJump`. `MovementInput.None` is `default`, and `AutoJump` is off unless you set it.

Game state that is not movement input goes in separately, as `PhysicsConditions`: game mode, creative flight, the movement-speed attribute, jump boost, slow falling, levitation, dolphin's grace, elytra state, and whether the dimension is ultrawarm. `PhysicsConditions.Default` is survival mode with a movement speed of 0.1.

A minimal loop:

```csharp
var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
engine.SetConditions(PhysicsConditions.Default);
engine.Reset(new Vec3d(0.5, 64, 0.5), yaw: 0f, pitch: 0f);

// One settle tick so OnGround and the fluid flags are populated before the real inputs start.
engine.Step(MovementInput.None);

for (int tick = 0; tick < 60; tick++)
{
    StepResult result = engine.Step(new MovementInput { Forward = true, Sprint = true });
    Console.WriteLine(result.State.Position);
}
```

That settle tick is not decoration. `Reset` zeroes velocity, pose, fall distance and every environment flag, so before the first real step the engine does not yet know it is standing on anything. There is no public way to load a `PhysicsState` into a live engine; the method that does it is internal. The public path is `Reset`, then optionally `SetVelocity` and `SetRotation`, then a throwaway step.

`Step` returns a `StepResult` of `State` and `Events`. `PhysicsState` carries position, velocity, yaw, pitch, the collision and fluid flags, `FallDistance`, `Pose`, `BoundingBox`, and computed `EyePosition` and `EyeHeight`. `StepEvents` reports `Landed` with a `LandingFallDistance`, `Bounced`, `PoseChanged` with the `PreviousPose`, and the two gliding transitions.

One trap worth stating: `PhysicsState.BoundingBox` is an init property, but the engine ignores it on input and rederives it. Do not set it and expect anything.

### Simulating without stepping

```csharp
public static PhysicsState Run(
    in PhysicsState start, in PhysicsConditions conditions, IPhysicsWorldView world,
    PhysicsProfile profile, ReadOnlySpan<MovementInput> inputs);

public static LandingPrediction PredictLanding(
    in PhysicsState start, in PhysicsConditions conditions, IPhysicsWorldView world,
    PhysicsProfile profile, MovementInput heldInput, int maxTicks);
```

`PhysicsSimulator` is pure. It answers "where would I be after these thirty ticks" without touching a live engine, which is what makes a jump decidable before you commit to it. `LandingPrediction` reports `Landed`, `LandingPosition`, `TicksSimulated` and the `FinalState`.

## Planning

The planner works on a captured snapshot of the world, not on the live one:

```csharp
public static PlanningWorldView Capture(
    World world, IBlockShapeSource shapes, BlockPos a, BlockPos b, int margin = 16);
```

`PlanningWorldView` implements `IPhysicsWorldView`, so the same object serves the planner and the executor's physics context. That is the tidy part of this design.

```csharp
public static PathResult FindPath(
    PlanningWorldView world, PathfinderOptions options, BlockPos start, IGoal goal,
    TimeProvider? timeProvider = null, CancellationToken ct = default);

public static Task<PathResult> FindPathAsync( /* same parameters */ );
```

Goals are small and implement two methods, `IsInGoal` and `Heuristic`. Four ship: `GoalBlock` (an exact block), `GoalNear` (a block and a radius), `GoalXZ` (a column, any height), `GoalComposite` (any of several), plus `EntityGoal`, which adds a `ShouldReplan(BlockPos currentTarget)` for a target that moves.

`PathfinderOptions` is a record with a `Default`. The interesting knobs are `AllowSprint`, `AllowParkour`, `AllowParkourAscend`, `AllowDiagonalDescend`, `AllowClimb`, `AllowSwim`, `AllowLadderGrabDuringFall`, `MaxFallHeight`, `MaxFallHeightIntoWater`, `MaxNodes`, `JumpPenalty`, `Timeout` and `BlocksToAvoid`. There is also a `MaxReplans`, and it is inert: nothing in `Umpk.Pathfinding` reads it, and the client hardcodes four replans.

`PathResult` reports a `Status` of `Success`, `Partial` or `Failed`, the `Path` as a list of `PathNode`, the `Moves` as a list of `MoveType`, plus `Cost`, `NodesExplored`, `ElapsedMs` and a `Diagnostics` struct that tells you whether the search timed out or exhausted its node budget. A `Partial` result is a real answer: it got as close as it could.

## Execution templates

A path is a list of blocks. An executor needs a list of moves, so segments come next:

```csharp
public static IReadOnlyList<PathSegment> FromPath(IReadOnlyList<PathNode> nodes);
```

`FromPath` produces `nodes.Count - 1` segments, which means a single-node path yields none. That case is not hypothetical: any goal that accepts the block you are already standing in gives you exactly one node, no segments, and an executor that reports `Complete` on its first tick without moving. Handle it.

Each segment carries `Start` and `End` as block centers, a `MoveType`, an `ExitTransition` and a set of `ExitHints` describing what the next segment needs (desired heading, speed window, whether footing must be stable, whether a jump must be ready). That is how one segment hands momentum to the next instead of stopping at every block.

`ActionTemplateFactory.Create(ctx, segment)` maps a segment to the template that flies it. Seven templates exist, and the mapping is not one to one with `MoveType`:

| `MoveType` | Template |
| --- | --- |
| `Traverse`, `Diagonal` | `WalkTemplate` |
| `Ascend` | `AscendTemplate` |
| `Descend` | `DescendTemplate` |
| `Fall` | `FallTemplate` |
| `Climb` | `ClimbTemplate` |
| `Parkour` | `SprintJumpTemplate` |
| `Swim` | `SwimTemplate` |

There is no `TraverseTemplate`, despite `MoveType.Traverse` being the commonest move. Every template has the same shape: a constructor taking the context and the segment, a `Tick` that returns a `TemplateState` and hands back a `TemplateOutput`, and `ExpectedStart` and `ExpectedEnd`.

## What a caller drives

`PathExecutor` computes inputs. It never touches the engine. You are the wire between them, and the loop is three lines:

```csharp
var ctx = new PathExecutionContext(view, profile);
var executor = new PathExecutor(ctx, segments);

for (int tick = 0; tick < maxTicks; tick++)
{
    PathExecutorTick step = executor.Tick(engine.State);
    if (step.State != PathExecutorState.InProgress)
    {
        break;
    }

    engine.SetRotation(step.Output.TargetYaw, step.Output.TargetPitch);
    engine.Step(step.Output.Input);
}
```

Feed it the engine's state, apply the rotation, step with the input. `PathExecutorTick` also reports `DeviationExceeded`, which goes true when the position you fed in has drifted further from the previous tick's prediction than the threshold (1.5 blocks by default, settable on the constructor). That is your replan signal.

Seed the engine from `segments[0].Start` rather than from your own position, because the planner nudges the start Y up one when your feet block is solid and your head block is not. Then burn the settle tick described above before the first executor tick.

`PathExecutor` also takes an optional `IPathExecutionObserver` for telemetry: navigation started, segment started, completed or failed, deviation detected, navigation completed. `DelegatePathExecutionObserver` wraps an `Action<string>` if all you want is a log.

## Or let the client do it

Most callers want none of the above:

```csharp
await client.Actions.Movement.MoveToAsync(new Vec3d(100.5, 64, -40.5), ct);
await client.Actions.Movement.NavigateAsync(new GoalNear(100, 64, -40, range: 3), ct);
```

Both methods forward to `client.Navigation`, which you can call directly if you prefer. `MoveToAsync` completes when the player is in the target block, or in the closest block the planner can finish in; it throws when no path exists at all. A request that stays inside one block skips planning and walks the fraction directly, which is worth knowing because the naive version of that reported an arrival without moving.

The connected client has one movement clock: the session tick. Navigation publishes the requested input for that tick, physics steps at most once, and one reporter emits changed input, resolved sprint state, and at most one appropriate position/rotation/status packet. Protocol 47 (Minecraft 1.8.x) publishes its legacy status-only movement packet even while stationary; protocol 107 and later elide a fully unchanged tick. Every era still refreshes absolute position after 20 ticks. The legacy cadence matters because 1.8 servers advance duration actions such as eating and drinking from movement-packet handling. Planning and interaction waits do not run a second timer; idle physics continues on the session tick while those waits are outstanding. Sneak and sprint requests therefore persist until explicitly changed, while `Self.Sprinting` reflects the state physics could actually apply (food, collision, water and ability rules included).

The rest of `MovementActions` is manual control that does not involve the planner: `LookAtAsync`, `SetRotationAsync`, `SetSneakingAsync`, `SetSprintingAsync`, `SwingArmAsync`, `LeaveBedAsync` and `SendPositionAsync`.

Both navigation methods take a movement lease and throw `InvalidOperationException` with a message naming the current owner if something else holds it. The only public way to acquire a lease yourself is `ClientPluginContext.TryAcquireMovement(reason)` from inside a plugin.

Pathfinding is a client feature, and features imply each other: pathfinding implies physics, physics implies terrain. Turning terrain off through `ClientFeatures` turns off all three, and `client.Navigation` then throws `FeatureDisabledException`.

## What the client does not expose

Worth knowing before you plan an architecture around it:

- No `PhysicsState` readback. The engine's state is internal. What you get is `client.State.Self`: `Position`, `Velocity`, `Yaw`, `Pitch`, `OnGround`, `Sneaking`, `Sprinting`, `WalkingSpeed`, `FlyingSpeed`, `GameMode`, `ActiveEffects` and the rest. There is no landed or pose-changed event on the client.
- No per-tick input injection. You cannot push a `MovementInput` through `UmpkClient`. The public movement surface is the nine `MovementActions` methods, two of which forward to the navigator, plus the navigator itself. Anything finer means running your own `PlayerPhysics` beside the client, which also means sending your own position packets.
- No accessor for the block shape source. You can give the client one through `UmpkClientBuilder.UseBlockShapes`, but you cannot get it back, so doing your own planning against `client.State.World` means keeping your own reference to an `IBlockShapeSource`. `JavaGameData.BlockShapes(protocol)` is where one comes from.

## Pistons

Pistons push players, and the engine models it. `MovingPiston.Tick(IPistonPushTarget? target)` moves the target, and `PlayerPhysics` implements `IPistonPushTarget` explicitly, so you have to cast:

```csharp
engine.BeginPistonTick();
piston.Tick((IPistonPushTarget)engine);
```

`BeginPistonTick` must be called once per tick, before any piston ticks. There is no public piston-tracking type; the client's tracker is internal.

## Related reading

- [Umpk.Physics](../packages/umpk-physics.md) and [Umpk.Pathfinding](../packages/umpk-pathfinding.md) for the full surface.
- [Vanilla as the oracle](../concepts/vanilla-as-the-oracle.md) for where the constants come from.
- [Limitations](../reference/limitations.md) for the known gaps.
