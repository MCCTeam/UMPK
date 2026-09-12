# Umpk.Physics.Tests

Hermetic tests for the physics engine, plus the parity-trace replay pipeline.

## What is tested

- **Collision** (`CollisionTests`): flat-ground stability, wall stop, step-up, sneak-edge back-off, entity colliders, unloaded-chunk drift, transfer reset.
- **Movement** (`MovementTests`): steady walk speed vs vanilla, sprint via the resolved speed attribute, ice momentum retention, jump height, jump boost, honey jump factor, sprint-jump boost, soul-sand slowdown.
- **Fluids** (`FluidTests`): water fall damping, water jump, swim ascend, lava damping, slime bounce and sneak-suppressed bounce, ladder climb and sneak-hold.
- **Poses** (`PoseTests`): crouch, forced crawl under a low ceiling, swim pose, elytra fall-flying pose, legacy 1.65 crouch height.
- **Elytra** (`ElytraTests`): level-glide lift, dive speed gain, climbable stops gliding.
- **Conditions** (`ConditionsTests`): slow falling, levitation, creative fly ascend/descend and no gravity fall. These prove that host-provided conditions affect the movement step.
- **Simulator** (`SimulatorTests`): `PhysicsSimulator.Run` matches step-by-step, determinism, purity, `PredictLanding`.
- **Allocation** (`AllocationTests`): zero bytes per `Step` after warmup (also proven by the BenchmarkDotNet `MemoryDiagnoser` in `tests/Umpk.Benchmarks/PhysicsTickBenchmark.cs`).

## Fixtures

- `Fixtures/FixtureWorld` is a hand-built voxel `IPhysicsWorldView` over an in-memory block map with a per-kind shape/flag table (`FixtureBlockData`, `BlockKind`). It uses no generated data.
- `Parity/Pr3076Course` builds the parity course in the fixture world, including the start, flat, stairs, slime, ladder, crawl, water, and goal sections.

## Parity / characterization traces

The trace format (`Parity/PhysicsTrace`) is a line-oriented text file: a `start` state, then one `tick` line per step carrying the input bits and the resulting position/velocity/pose/on-ground. `Parity/TraceReplay` records a trace by running the engine over an input script and replays a committed trace by driving the same script through a fresh engine, comparing within epsilon.

`Parity/CharacterizationTraceTests` commits self-characterization traces under `Parity/Traces/*.umpktrace` for six scenarios (flat sprint, stair ascend, slime bounce, ladder climb, crawl tunnel, jump arc). Each is replayed and must reproduce **bit-identically** because the engine is deterministic. A freshly recorded run must also byte-match the committed file, so any physics regression fails the build. If a trace file is missing it is regenerated into the source tree for commit.

### Recording a vanilla parity trace (pending manual session)

The characterization traces above are UMPK's own engine output (the characterization "floor"). A true **vanilla parity trace** requires a one-time, manual, offline instrumented-recording session that CI cannot perform because the vanilla client has no headless mode and recording requires a licensed account. Use this procedure so the trace works with `TraceReplay.Replay` unchanged:

1. On a **licensed account**, start a real vanilla client for the target physics era with a client-side per-tick logger. A Fabric mod is the sanctioned tooling shape. Log position/velocity/pose/on-ground each client tick.
2. Load the `Pr3076Course` geometry on a local **offline** server.
3. Play the **same scripted input sequence** the characterization traces use (drive the documented input script deterministically, for example via an autoclicker/macro or the mod itself).
4. Export the per-tick log into the `PhysicsTrace` text format (`meta profile <name>`, `start ...`, one `tick <bits> px py pz vx vy vz pose onGround` line per tick), commit it under `Parity/Traces/vanilla_<era>_<scenario>.umpktrace`.
5. Add a replay test that loads the committed vanilla trace, rebuilds the same course geometry, pushes the era's `PhysicsProfile` and the survival `PhysicsConditions`, and asserts `TraceReplay.Replay` keeps `MaxPositionDeviation`/`MaxVelocityDeviation` under the parity epsilon.

Until that session is performed, the vanilla-recorded parity trace is a tracked deviation. The full pipeline and the self-characterization floor are in place and green.
