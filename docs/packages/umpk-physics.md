---
title: "Umpk.Physics"
description: "A tick-accurate player movement and collision engine with per-era profiles, mirroring vanilla at 20 TPS."
sidebar:
  order: 10
---

`Umpk.Physics` moves a player the way vanilla moves a player: one tick at a time, with the same friction, the same drag, the same jump impulse, the same axis-separated collision resolution and the same floating-point widths. Constants are verified against Mojang releases, which is why `PhysicsConstants.WaterWalkerSlowDownTarget` is `0.54600006` instead of a tidier number. Source comments describe the behavior while change reviews retain the detailed research.

It handles ground and air travel, water and lava, creative flight, elytra gliding, climbing, sneak edge detection, jump boost, levitation, slow falling, fluid currents and piston pushes.

## Its place in the stack

`Umpk.Physics` depends on [Umpk.Core](umpk-core.md) (geometry) and [Umpk.Game](umpk-game.md) (block states and poses). It knows nothing about packets. [Umpk.Pathfinding](umpk-pathfinding.md) builds on it, and [Umpk.Client](umpk-client.md) drives it once per tick when the physics feature is on.

## Main entry points

`PlayerPhysics` is the stateful engine. Construct it with an `IPhysicsWorldView` and a `PhysicsProfile`, seed it with `Reset(position, yaw, pitch)`, push conditions in with `SetConditions`, then call `Step(input)` once per tick. `Step` returns a `StepResult`, which is the new `PhysicsState` plus a `StepEvents` describing what happened during that tick (landed, bounced, pose changed, started or stopped gliding).

`PhysicsState` is the readonly snapshot: position, velocity, yaw, pitch, pose, bounding box, eye height, fall distance, and the flags `OnGround`, `InWater`, `InLava`, `IsUnderWater`, `IsSwimming`, `IsGliding`, `OnClimbable`, `HorizontalCollision` and `VerticalCollision`.

`MovementInput` is the button state: `Forward`, `Back`, `Left`, `Right`, `Jump`, `Sneak`, `Sprint`, `AutoJump`. `MovementInput.None` is the neutral one.

`PhysicsConditions` is everything the engine cannot work out for itself and the host must push in: `MovementSpeedAttribute`, `FlyingSpeed`, `GameMode`, `MayFly`, `CreativeFlying`, `ElytraEquipped`, `ElytraFlying`, `HasJumpBoost` and its amplifier, `HasLevitation`, `HasSlowFalling`, `HasDolphinsGrace`, `WaterMovementEfficiency` and `UltraWarmDimension`. `PhysicsConditions.Default` is survival with vanilla defaults.

`PhysicsProfile` is the era. `FluidMovement`, `WaterTravel`, `WaterClimbBumpAvailable`, `ElytraAvailable`, `SwimPoseAvailable`, `CrawlPoseAvailable`, `PoseDimensions` and `PositionSend`, plus the derived `Width`, `StandingHeight`, `CrouchHeight` and `SwimHeight`.

`IPhysicsWorldView` is the world seam: `GetBlock`, `GetCollisionShapes`, `IsChunkLoaded` and `CollectEntityColliders`.

`PhysicsSimulator` is the stateless form. `Run` applies a span of inputs to a starting state and returns the end state. `PredictLanding` holds one input and reports where and when the player lands.

`MovingPiston` and `IPistonPushTarget` model a piston shoving the player, with `PlayerPhysics.BeginPistonTick` marking the start of the tick.

## Example

```csharp
using Umpk.Geometry;
using Umpk.Physics;

// The world view comes from your host: Umpk.Client supplies one, and the test
// suites each implement the four-member interface directly.
var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
engine.SetConditions(PhysicsConditions.Default);
engine.Reset(new Vec3d(0.5, 64.0, 0.5), yaw: 0f, pitch: 0f);

// Settle onto the ground first. Physics is a per-tick simulation, not a solver.
engine.Step(MovementInput.None);
engine.Step(MovementInput.None);

var walking = new MovementInput { Forward = true };
for (int tick = 0; tick < 20; tick++)
{
    StepResult result = engine.Step(walking);
    if (result.Events.Landed)
    {
        Console.WriteLine($"Landed after falling {result.Events.LandingFallDistance:F2} blocks.");
    }
}

// Steady vanilla walk speed is 4.317 blocks per second, so after twenty ticks of
// acceleration the player is a little short of that.
Console.WriteLine(engine.State.Position);
```

To build the era-correct profile for a version, map the dataset's `ProtocolFeatures` onto `PhysicsProfile.FromFeatures`. This is exactly what `Umpk.Client` does internally:

```csharp
using Umpk.Physics;
using Umpk.Protocol.Java;

ProtocolFeatures features = version.Features;

PhysicsProfile profile = PhysicsProfile.FromFeatures(
    swimmingUpdate: features.FluidMovement != "legacy",
    sprintAwareWaterTravel: features.WaterTravel != "legacy",
    waterClimbBump: features.WaterClimbBump,
    elytraAvailable: features.Elytra,
    swimPoseAvailable: features.SwimPose,
    crawlPoseAvailable: features.CrawlPose,
    modernCrouchHeight: features.CrawlPose,
    dirtyCheckedPositionCadence:
        PhysicsProfile.PositionSendCadenceForProtocol(version.Version.Protocol)
            == PositionSendCadence.DirtyChecked);
```

## Things that catch people out

`PhysicsProfile.ForProtocol` is not the production path, and its own documentation says so. It is a curated in-code table for tests and bootstrap, covering three reference protocols (47, 770, 776) with everything newer falling through to the modern defaults. It also carries a deliberate, documented divergence from the dataset: it reports `FluidMovementEra.Legacy` for protocols 47 through 392, while the dataset correctly gives every protocol `SwimmingUpdate`, because the depth-strider blend that flag gates has been in vanilla unchanged since 1.8. Use `FromFeatures` fed from the dataset, or let `Umpk.Client` build the profile for you.

Following from that: `FluidMovementEra.Legacy` is never selected in production. The axis exists and the engine honours it, but no supported protocol asks for it. `WaterTravelEra` is the axis that really changes at 1.13, at the protocol 340 to 393 boundary: the horizontal water slow-down gains a sprinting arm and the flat vertical sink is replaced by a fluid-falling adjustment.

`MovementInput.Sprint` does not make you 30 percent faster on its own. The sprint modifier is a server side attribute modifier, and `PhysicsConditions.MovementSpeedAttribute` is the fully resolved value including sprint, speed and slowness. The engine applies the attribute directly. If you set `Sprint = true` and leave the attribute at its default 0.1, you get the air-control and sprint-jump parts of sprinting without the speed. The host owns resolving that attribute.

`PhysicsConditions` is pushed on change, not per tick. Refresh it when abilities, effects, game mode or equipment change. Rebuilding it every tick works but is wasted effort.

The engine needs two settling ticks after `Reset` before `OnGround` and the fluid flags mean anything. Every test in the repository does this. Reading `State.OnGround` immediately after `Reset` tells you nothing.

`Step` takes its argument by `in` and returns a `StepResult` you should destructure or read immediately. `PhysicsState` and `StepEvents` are readonly structs with `init` properties, so building one by hand is possible but rarely what you want; let the engine produce them.

`IPhysicsWorldView.GetCollisionShapes` is where real block geometry enters. Warning: a shape source that returns a full cube for every block makes the player walk through a staircase as though it were solid. Give the world view a real source, such as `JavaGameData.BlockShapes(protocol)` from [Umpk.Data.Java](umpk-data-java.md).

`PositionSendCadence` is not a physics behavior at all. It describes how often the client sends its position, which is a send-loop concern, and it is computed from the protocol number rather than from any dataset field. It rides on `PhysicsProfile` because that is where the rest of the per-era movement settings live.

`PhysicsConstants` exposes vanilla's constants at vanilla's widths. Several are `float` where you might expect `double` (`FrictionMultiplier`, `AirSpeed`, `BaseJumpPower`), and that matters: widening them changes results by fractions of a block over a few hundred ticks, which is enough to miss a jump.
