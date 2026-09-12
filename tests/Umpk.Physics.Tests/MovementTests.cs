using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Friction, sprint, jump (ground/sprint boost/jump boost/block jump factor), block speed factor.</summary>
public sealed class MovementTests
{
    private static PlayerPhysics Ground(FixtureWorld world, Vec3d start, PhysicsConditions conditions, float yaw = 0f)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(conditions);
        engine.Reset(start, yaw, 0f);
        engine.Step(MovementInput.None);
        engine.Step(MovementInput.None);
        return engine;
    }

    private static double SteadyForwardSpeed(FixtureWorld world, Vec3d start, PhysicsConditions conditions, MovementInput input)
    {
        var engine = Ground(world, start, conditions);
        double lastZ = engine.State.Position.Z;
        double speed = 0;
        for (int i = 0; i < 60; i++)
        {
            engine.Step(input);
            speed = engine.State.Position.Z - lastZ;
            lastZ = engine.State.Position.Z;
        }

        return speed;
    }

    [Fact]
    public void SteadyWalkSpeed_MatchesVanilla()
    {
        // Vanilla walk speed is 4.317 m/s == 0.2158 blocks/tick at the default speed attribute.
        var world = new FixtureWorld().Floor(-5, 5, -5, 400, 63, BlockKind.Stone);
        double walk = SteadyForwardSpeed(world, new Vec3d(0.5, 64, 0.5), PhysicsConditions.Default, new MovementInput { Forward = true });
        Assert.InRange(walk, 0.210, 0.220);
    }

    /// <summary>Sprinting is faster than walking by EXACTLY the modifier, from the same conditions.</summary>
    /// <remarks>Both runs start from <see cref="PhysicsConditions.Default"/> and differ only in the input bit. The two-sided ratio catches both a missing modifier and a double application.</remarks>
    [Fact]
    public void SprintAttribute_IsFasterThanWalk()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 400, 63, BlockKind.Stone);
        double walk = SteadyForwardSpeed(world, new Vec3d(0.5, 64, 0.5), PhysicsConditions.Default, new MovementInput { Forward = true });
        double sprint = SteadyForwardSpeed(
            world,
            new Vec3d(0.5, 64, 0.5),
            PhysicsConditions.Default,
            new MovementInput { Forward = true, Sprint = true });

        // 0.21586 -> 0.28062: the ratio is (float)(0.1f * 1.300000011920929) / 0.1f, which is 1.3000001 rather than 1.3 exactly because the fold rounds to float once at the end.
        double ratio = sprint / walk;
        Assert.True(ratio > 1.29 && ratio < 1.31, $"sprint {sprint} over walk {walk} is a ratio of {ratio}, not the modifier's 1.3");
        Assert.Equal(0.28061680662163102, sprint, 12);
    }

    [Fact]
    public void Ice_RetainsMomentumLongerThanStone()
    {
        // Slipperiness is momentum retention: after releasing input, ice keeps sliding longer.
        double SlideAfterRelease(BlockKind floor)
        {
            var world = new FixtureWorld().Floor(-5, 5, -5, 400, 63, floor);
            var engine = Ground(world, new Vec3d(0.5, 64, 0.5), PhysicsConditions.Default);
            for (int i = 0; i < 40; i++)
                engine.Step(new MovementInput { Forward = true });

            double before = engine.State.Position.Z;
            for (int i = 0; i < 20; i++)
                engine.Step(MovementInput.None);

            return engine.State.Position.Z - before;
        }

        double iceSlide = SlideAfterRelease(BlockKind.Ice);
        double stoneSlide = SlideAfterRelease(BlockKind.Stone);
        Assert.True(iceSlide > stoneSlide * 1.5, $"ice slide {iceSlide} not longer than stone {stoneSlide}");
    }

    [Fact]
    public void HigherMovementSpeedAttribute_MovesFaster()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 400, 63, BlockKind.Stone);
        var normal = PhysicsConditions.Default;
        var fast = PhysicsConditions.Default with { BaseMovementSpeedAttribute = 0.15f };

        double a = SteadyForwardSpeed(world, new Vec3d(0.5, 64, 0.5), normal, new MovementInput { Forward = true });
        double b = SteadyForwardSpeed(world, new Vec3d(0.5, 64, 0.5), fast, new MovementInput { Forward = true });

        Assert.True(b > a * 1.2, $"speed attr had no effect: {a} vs {b}");
    }

    [Fact]
    public void Jump_ReachesApproxJumpHeight()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        var engine = Ground(world, new Vec3d(0.5, 64, 0.5), PhysicsConditions.Default);

        double startY = engine.State.Position.Y;
        double maxY = startY;
        var jump = new MovementInput { Jump = true };
        for (int i = 0; i < 30; i++)
        {
            engine.Step(jump);
            maxY = Math.Max(maxY, engine.State.Position.Y);
        }

        // Vanilla neutral jump peak is ~1.252 blocks.
        Assert.InRange(maxY - startY, 1.15, 1.30);
    }

    [Fact]
    public void JumpBoost_IncreasesJumpHeight()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        double PeakWith(PhysicsConditions c)
        {
            var e = Ground(world, new Vec3d(0.5, 64, 0.5), c);
            double s = e.State.Position.Y;
            double m = s;
            for (int i = 0; i < 40; i++)
            {
                e.Step(new MovementInput { Jump = true });
                m = Math.Max(m, e.State.Position.Y);
            }

            return m - s;
        }

        double normal = PeakWith(PhysicsConditions.Default);
        double boosted = PeakWith(PhysicsConditions.Default with { HasJumpBoost = true, JumpBoostAmplifier = 1 });
        Assert.True(boosted > normal + 0.5, $"jump boost had no effect: {normal} vs {boosted}");
    }

    [Fact]
    public void HoneyBlock_ReducesJumpHeight()
    {
        // Honey has jump factor 0.5, halving jump power.
        var stone = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        var honey = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.HoneyBlock);

        double PeakOn(FixtureWorld w)
        {
            var e = Ground(w, new Vec3d(0.5, 64, 0.5), PhysicsConditions.Default);
            double s = e.State.Position.Y;
            double m = s;
            for (int i = 0; i < 30; i++)
            {
                e.Step(new MovementInput { Jump = true });
                m = Math.Max(m, e.State.Position.Y);
            }

            return m - s;
        }

        Assert.True(PeakOn(honey) < PeakOn(stone) * 0.7, "honey did not reduce jump height");
    }

    [Fact]
    public void SprintJump_GivesHorizontalBoost()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 200, 63, BlockKind.Stone);

        double DistanceAfterJump(bool sprint)
        {
            var e = Ground(world, new Vec3d(0.5, 64, 0.5), PhysicsConditions.Default);
            double startZ = e.State.Position.Z;
            var input = new MovementInput { Forward = true, Jump = true, Sprint = sprint };
            // Build up speed first.
            for (int i = 0; i < 10; i++)
                e.Step(new MovementInput { Forward = true, Sprint = sprint });

            double preZ = e.State.Position.Z;
            e.Step(input); // the sprint-jump tick
            double jumpZ = e.State.Position.Z;
            return jumpZ - preZ;
        }

        double sprintStep = DistanceAfterJump(true);
        double walkStep = DistanceAfterJump(false);
        Assert.True(sprintStep > walkStep, $"sprint-jump {sprintStep} not > walk-jump {walkStep}");
    }

    [Fact]
    public void SoulSand_SlowsMovement()
    {
        var stone = new FixtureWorld().Floor(-5, 5, -5, 400, 63, BlockKind.Stone);
        var soul = new FixtureWorld().Floor(-5, 5, -5, 400, 63, BlockKind.SoulSand);
        double onStone = SteadyForwardSpeed(stone, new Vec3d(0.5, 64, 0.5), PhysicsConditions.Default, new MovementInput { Forward = true });
        double onSoul = SteadyForwardSpeed(soul, new Vec3d(0.5, 64, 0.5), PhysicsConditions.Default, new MovementInput { Forward = true });
        Assert.True(onSoul < onStone * 0.6, $"soul sand did not slow: stone {onStone}, soul {onSoul}");
    }
}
