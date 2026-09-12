using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Water buoyancy/friction, swim ascend/descend, lava, slime bounce/step-on, ladders.</summary>
public sealed class FluidTests
{
    private static PlayerPhysics NewEngine(FixtureWorld world, Vec3d start, PhysicsConditions? c = null, float yaw = 0f, float pitch = 0f)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(c ?? PhysicsConditions.Default);
        engine.Reset(start, yaw, pitch);
        engine.SetRotation(yaw, pitch);
        return engine;
    }

    [Fact]
    public void Water_SlowsFallComparedToAir()
    {
        var air = new FixtureWorld();
        var water = new FixtureWorld().Fill(-2, 60, -2, 2, 80, 2, BlockKind.Water);

        double FallVelocityAfter(FixtureWorld w, int ticks)
        {
            var e = NewEngine(w, new Vec3d(0.5, 78, 0.5));
            for (int i = 0; i < ticks; i++)
                e.Step(MovementInput.None);

            return e.State.Velocity.Y;
        }

        double airVy = FallVelocityAfter(air, 8);
        double waterVy = FallVelocityAfter(water, 8);
        Assert.True(waterVy > airVy, $"water did not slow the fall: air {airVy}, water {waterVy}");
    }

    [Fact]
    public void Water_JumpGivesUpwardImpulse()
    {
        var world = new FixtureWorld().Fill(-2, 60, -2, 2, 80, 2, BlockKind.Water);
        var engine = NewEngine(world, new Vec3d(0.5, 70, 0.5));
        engine.Step(MovementInput.None);
        double vyBefore = engine.State.Velocity.Y;
        engine.Step(new MovementInput { Jump = true });
        Assert.True(engine.State.Velocity.Y > vyBefore, "water jump gave no upward impulse");
    }

    [Fact]
    public void SwimAscend_WhenLookingUpAndSprinting()
    {
        // Underwater, sprinting (swimming), looking up should climb. Pitch -90 looks straight up.
        var world = new FixtureWorld().Fill(-2, 40, -2, 2, 90, 2, BlockKind.Water);
        var engine = NewEngine(world, new Vec3d(0.5, 60, 0.5), pitch: -90f);
        engine.SetRotation(0f, -90f);

        double startY = engine.State.Position.Y;
        var swimUp = new MovementInput { Forward = true, Sprint = true, Jump = true };
        for (int i = 0; i < 30; i++)
            engine.Step(swimUp);

        Assert.True(engine.State.Position.Y > startY, $"did not swim up, y {startY} -> {engine.State.Position.Y}");
    }

    [Fact]
    public void Lava_SlowsMovementStrongly()
    {
        var world = new FixtureWorld().Fill(-2, 60, -2, 2, 80, 2, BlockKind.Lava);
        var engine = NewEngine(world, new Vec3d(0.5, 70, 0.5));
        engine.Step(MovementInput.None);
        // Lava horizontal damping is 0.5, so speed decays fast.
        for (int i = 0; i < 5; i++)
            engine.Step(new MovementInput { Forward = true });

        Assert.True(Math.Abs(engine.State.Velocity.Z) < 0.05, $"lava did not damp movement, vz={engine.State.Velocity.Z}");
    }

    [Fact]
    public void SlimeBlock_BouncesFallingPlayer()
    {
        // Drop onto slime; the player should rebound upward (bounce).
        var world = new FixtureWorld()
            .Floor(-5, 5, -5, 5, 63, BlockKind.SlimeBlock);
        var engine = NewEngine(world, new Vec3d(0.5, 70, 0.5));

        bool bounced = false;
        double maxVyUp = 0;
        for (int i = 0; i < 40; i++)
        {
            StepResult r = engine.Step(MovementInput.None);
            if (r.Events.Bounced)
                bounced = true;

            maxVyUp = Math.Max(maxVyUp, r.State.Velocity.Y);
        }

        Assert.True(bounced, "no bounce event fired");
        Assert.True(maxVyUp > 0.1, $"player did not rebound upward, maxVyUp={maxVyUp}");
    }

    [Fact]
    public void SlimeBlock_SneakingSuppressesBounce()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.SlimeBlock);
        var engine = NewEngine(world, new Vec3d(0.5, 70, 0.5));

        bool bounced = false;
        for (int i = 0; i < 40; i++)
        {
            StepResult r = engine.Step(new MovementInput { Sneak = true });
            if (r.Events.Bounced)
                bounced = true;

        }

        Assert.False(bounced, "sneaking should suppress the slime bounce");
    }

    [Fact]
    public void Ladder_LettsPlayerClimbUp()
    {
        // Ladder column against a wall; holding forward into it should climb.
        var world = new FixtureWorld()
            .Floor(-5, 5, -5, 5, 63, BlockKind.Stone)
            .Fill(0, 64, 1, 0, 75, 1, BlockKind.Stone)  // wall at z=1
            .Fill(0, 64, 0, 0, 75, 0, BlockKind.Ladder); // ladder at player column z=0

        var engine = NewEngine(world, new Vec3d(0.5, 64, 0.5));
        engine.Step(MovementInput.None);
        double startY = engine.State.Position.Y;

        var climb = new MovementInput { Forward = true };
        for (int i = 0; i < 40; i++)
            engine.Step(climb);

        Assert.True(engine.State.Position.Y > startY + 1.0, $"did not climb ladder, y {startY} -> {engine.State.Position.Y}");
    }

    [Fact]
    public void Ladder_SneakingHoldsPosition()
    {
        var world = new FixtureWorld()
            .Fill(0, 60, 1, 0, 75, 1, BlockKind.Stone)
            .Fill(0, 60, 0, 0, 75, 0, BlockKind.Ladder);

        var engine = NewEngine(world, new Vec3d(0.5, 68, 0.5));
        engine.Step(MovementInput.None);
        double y = engine.State.Position.Y;

        // Sneaking on a ladder must stop downward sliding.
        for (int i = 0; i < 20; i++)
            engine.Step(new MovementInput { Sneak = true });

        Assert.True(engine.State.Position.Y >= y - 0.01, $"slid down while sneaking on ladder: {y} -> {engine.State.Position.Y}");
    }
}
