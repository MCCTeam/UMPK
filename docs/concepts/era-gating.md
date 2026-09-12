---
title: "Era gating"
description: "Version-dependent behavior comes from dataset feature axes, never from a protocol-number comparison in engine code, and the tests that keep it honest."
sidebar:
  order: 5
---

Some differences between versions are structural: a packet gained a field, an id moved. Those are handled by the packet tables, and [the packet pipeline](packet-pipeline.md) covers them. This page is about the other kind, where the wire is unchanged but the game behaves differently. Water travel got a sprinting arm at 1.13. Ladders became usable while swimming at 1.14. Nothing on the wire tells you that; the client has to know.

The rule is short. Engine code never compares protocol numbers. It reads a named axis from the dataset.

## Why not just compare numbers

Because `if (protocol >= 393)` scattered through a physics engine is unreadable and unmaintainable, and those are the small problems.

The real problem is that the comparison hides the claim. Written that way, a boundary is an integer with no evidence attached and no name. Six months later nobody knows whether 393 is where sprinting changed or where the pose table changed or where somebody guessed, and the six other places that also say 393 may or may not be the same fact. When one of them turns out to be wrong, you have no way to find the others that share its cause and no way to leave the ones that only coincide.

A named axis fixes both. `waterTravel` is a claim about one behavior, stated once per protocol in the dataset and supported by independent boundary tests. If it moves, exactly the behaviors that depend on it move.

## Three real axes

`data/java/<protocol>/features.json` carries the flags. Here are three of them on protocol 776:

```json
{
  "flags": {
    "fluidMovement": "swimmingUpdate",
    "waterTravel": "sprintAware",
    "waterClimbBump": true
  }
}
```

and on protocol 47:

```json
{
  "flags": {
    "fluidMovement": "swimmingUpdate",
    "waterTravel": "legacy",
    "waterClimbBump": false
  }
}
```

`waterTravel` gates two constants in the water branch. On `legacy` the horizontal slow-down is a flat 0.8 and the vertical sink is a flat `motionY -= 0.02`. On `sprintAware` the slow-down becomes `isSprinting() ? 0.9F : 0.8F` and the sink goes through `getFluidFallingAdjustedMovement`. The boundary is 340 to 393, which is 1.12.2 to 1.13.

`waterClimbBump` is the in-water climbable bump: when you are horizontally colliding and on a ladder or a vine, the vertical velocity is replaced by 0.2 just before damping. It is what makes a ladder usable while swimming. The boundary is 404 to 477, which is 1.13.2 to 1.14.

`fluidMovement` reads `swimmingUpdate` on all 49 protocols, so it currently gates nothing. The selected depth-strider blend is unchanged across the supported range. Retaining the axis makes that verified uniformity explicit and leaves room for a future boundary without changing the engine contract.

Note also what did not happen: `waterClimbBump` was not folded into `waterTravel` even though both are about water, and not aliased onto the crawl pose flag even though both change at 477. A coincident boundary is not a shared cause. If a future correction moves the pose boundary, water travel must not move with it.

## The two patterns

Bad:

```csharp
// Nobody can tell what claim 477 encodes, or which other 477s are the same fact.
if (protocol >= 477 && horizontalCollision && onClimbable)
{
    velocity = velocity with { Y = 0.2 };
}
```

Good, and this is the real code from `PlayerPhysics.TravelInWater`:

```csharp
bool sprintAwareWater = _profile.WaterTravel == WaterTravelEra.SprintAware;
float slowDown = sprintAwareWater && _sprinting
    ? PhysicsConstants.WaterSprintSlowDown
    : PhysicsConstants.WaterSlowDown;
```

`_profile` is a `PhysicsProfile`, built once per session. `Umpk.Physics` never sees a protocol number and never reads JSON. The translation happens in one adapter, `PhysicsProfileFactory` in `Umpk.Client`, which maps the generated `ProtocolFeatures` onto the profile's flags. It sits in the client package so that neither the physics package nor the protocol package has to depend on the other.

Two profile fields have no dataset source. Crouch height follows the crawl-pose capability because their supported boundaries coincide. Position-send cadence is computed from the protocol because it is a session behavior rather than a wire feature. The adapter documents both exceptions directly.

## Pinning independent expectations

The obvious conformance test reads the dataset and checks the engine agrees:

```csharp
// BAD: reads the dataset to predict the dataset; cannot fail on a wrong value.
Assert.Equal(features.FluidMovement, profile.FluidMovement);
```

This proves that the adapter copies the field, but it cannot prove that the field is correct. A wrong dataset value flows through both sides of the assertion and the test stays green.

The fix is to write the expected values out by hand, as a literal table, covering every protocol:

```csharp
public static TheoryData<int, string> WaterTravelExpectations() => new()
{
    // Protocols 47 through 340 use fixed slow-down and sink values.
    { 47, "legacy" },    // 1.8 - 1.8.9
    { 107, "legacy" },   // 1.9
    // ...
    { 340, "legacy" },   // 1.12.2  <-- last legacy protocol
    { 393, "sprintAware" },   // 1.13   <-- first sprint-aware protocol
    { 401, "sprintAware" },   // 1.13.1
    // ...
};
```

Nothing in that table reads `features.json`, `ProtocolFeatures`, the profile factory, or the profile. It is an independent statement of the expected boundary and fails if the dataset drifts.

Two properties make it work. It covers every protocol, so a new version cannot slip in unpinned. And it encodes a real boundary, so a wrong value on either side of 340 and 393 fails. A table where every row holds the same value is degenerate: still worth having as a statement, but it can only catch a change, not a misplacement.

The maintenance cost is real. Adding a protocol means adding a row to each table by hand, and that is the point. The alternative generates the expectation from the thing under test, and an expectation you did not write yourself is not an expectation.

## Adding an axis

Changing a dataset value or adding an axis moves behavior on up to 49 protocols at once, so it is a decision to raise before making, not after. The mechanics are in [adding a version](../contributing/adding-a-version.md) and the surrounding gate is in [development](../contributing/development.md).
