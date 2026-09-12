using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Collision axis cases, step-up, sneak edge back-off, and entity colliders.</summary>
public sealed class CollisionTests
{
    private static PlayerPhysics NewEngine(FixtureWorld world, Vec3d start, float yaw = 0f)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(start, yaw, 0f);
        return engine;
    }

    [Fact]
    public void FlatGround_NoVerticalOscillation()
    {
        // Walking on flat ground must not bounce on Y.
        var world = new FixtureWorld().Floor(-5, 5, -5, 30, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(0.5, 64.0, 0.5));

        // Settle one tick, then walk forward and assert Y stays put.
        engine.Step(MovementInput.None);
        double restY = engine.State.Position.Y;
        Assert.Equal(64.0, restY, 5);

        var walk = new MovementInput { Forward = true };
        for (int i = 0; i < 40; i++)
        {
            engine.Step(walk);
            Assert.True(engine.State.OnGround, $"lost ground at tick {i}");
            Assert.Equal(64.0, engine.State.Position.Y, 5);
        }

        Assert.True(engine.State.Position.Z > 1.0, "player did not advance forward");
    }

    [Fact]
    public void WallStopsHorizontalMovement()
    {
        var world = new FixtureWorld()
            .Floor(-5, 5, -5, 5, 63, BlockKind.Stone)
            .Fill(0, 64, 3, 0, 66, 3, BlockKind.Stone); // wall ahead (+Z)

        var engine = NewEngine(world, new Vec3d(0.5, 64.0, 0.5));
        var walk = new MovementInput { Forward = true, Sprint = true };
        for (int i = 0; i < 60; i++)
            engine.Step(walk);

        // The player box front face (z + 0.3) must stop before the wall at z=3.
        Assert.True(engine.State.Position.Z < 3.0, $"walked into wall, z={engine.State.Position.Z}");
        Assert.True(engine.State.HorizontalCollision);
    }

    [Fact]
    public void StepUp_ClimbsOneBlock()
    {
        // A single stone step (top at y=65) in front; player should step up onto it.
        var world = new FixtureWorld()
            .Floor(-5, 5, -5, 20, 63, BlockKind.Stone)
            .Fill(-5, 64, 3, 5, 64, 20, BlockKind.Slab); // 0.5-high step, within StepHeight 0.6

        var engine = NewEngine(world, new Vec3d(0.5, 64.0, 0.5));
        var walk = new MovementInput { Forward = true };
        for (int i = 0; i < 60; i++)
            engine.Step(walk);

        Assert.True(engine.State.Position.Y >= 64.5 - 1e-6, $"did not step up, y={engine.State.Position.Y}");
        Assert.True(engine.State.Position.Z > 3.0, $"did not advance past step, z={engine.State.Position.Z}");
    }

    [Fact]
    public void SneakEdge_DoesNotWalkOffCliff()
    {
        // Floor ends at z=2; sneaking forward must not fall past the edge.
        var world = new FixtureWorld().Floor(-5, 5, -5, 2, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(0.5, 64.0, 2.0));

        // Settle onto the ground first (ground state is observed the tick after gravity engages).
        engine.Step(MovementInput.None);
        engine.Step(MovementInput.None);
        Assert.True(engine.State.OnGround);

        var sneakWalk = new MovementInput { Forward = true, Sneak = true };
        for (int i = 0; i < 80; i++)
        {
            engine.Step(sneakWalk);
            Assert.True(engine.State.OnGround, $"fell off while sneaking at tick {i}");
        }

        // Vanilla lets the box overhang until it would fully leave the floor; the invariant is that the player never falls (stays on ground) and never crosses fully past the block (center z<3.5).
        Assert.True(engine.State.OnGround);
        Assert.True(engine.State.Position.Z < 3.5, $"sneaked fully off edge, z={engine.State.Position.Z}");
    }

    [Fact]
    public void EntityCollider_BlocksMovement()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        world.AddEntityCollider(new Aabb(-1, 64, 2, 1, 66, 2.5)); // hard box ahead

        var engine = NewEngine(world, new Vec3d(0.5, 64.0, 0.5));
        var walk = new MovementInput { Forward = true, Sprint = true };
        for (int i = 0; i < 40; i++)
            engine.Step(walk);

        Assert.True(engine.State.Position.Z + 0.3 <= 2.0 + 1e-3, $"passed through entity collider, z={engine.State.Position.Z}");
    }

    [Fact]
    public void UnloadedChunk_DriftsDownSlowly()
    {
        var world = new FixtureWorld { AllChunksLoaded = false };
        var engine = NewEngine(world, new Vec3d(0.5, 100.0, 0.5));

        StepResult r = engine.Step(MovementInput.None);
        // With no chunk loaded, vertical velocity clamps toward -0.1 rather than full gravity.
        Assert.True(r.State.Velocity.Y <= 0.0);
        Assert.True(r.State.Velocity.Y >= -0.11, $"drift too fast, vy={r.State.Velocity.Y}");
    }

    [Fact]
    public void Reset_ClearsPoseAndVelocity()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(0.5, 70.0, 0.5));
        for (int i = 0; i < 10; i++)
            engine.Step(new MovementInput { Forward = true });

        engine.Reset(new Vec3d(0.5, 64.0, 0.5), 0f, 0f);
        PhysicsState s = engine.State;
        Assert.Equal(Vec3d.Zero, s.Velocity);
        Assert.Equal(EntityPose.Standing, s.Pose);
        Assert.Equal(0.0, s.FallDistance, 6);
    }
}
