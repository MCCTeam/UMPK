using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Verifies that the engine consumes host-pushed slow-falling, levitation, and creative-flight conditions.</summary>
public sealed class ConditionsTests
{
    private static PlayerPhysics Airborne(FixtureWorld world, PhysicsConditions c)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(c);
        engine.Reset(new Vec3d(0.5, 100, 0.5), 0f, 0f);
        return engine;
    }

    [Fact]
    public void SlowFalling_ReducesDownwardAcceleration()
    {
        var world = new FixtureWorld();
        double NormalVy()
        {
            var e = Airborne(world, PhysicsConditions.Default);
            for (int i = 0; i < 10; i++)
                e.Step(MovementInput.None);

            return e.State.Velocity.Y;
        }

        double SlowVy()
        {
            var e = Airborne(world, PhysicsConditions.Default with { HasSlowFalling = true });
            for (int i = 0; i < 10; i++)
                e.Step(MovementInput.None);

            return e.State.Velocity.Y;
        }

        Assert.True(SlowVy() > NormalVy(), "slow falling did not reduce fall speed");
    }

    [Fact]
    public void Levitation_LiftsPlayerUpward()
    {
        var world = new FixtureWorld();
        var engine = Airborne(world, PhysicsConditions.Default with { HasLevitation = true, LevitationAmplifier = 1 });
        double startY = engine.State.Position.Y;
        for (int i = 0; i < 20; i++)
            engine.Step(MovementInput.None);

        Assert.True(engine.State.Position.Y > startY, $"levitation did not lift: {startY} -> {engine.State.Position.Y}");
    }

    [Fact]
    public void CreativeFly_JumpAscendsAndSneakDescends()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        var flying = PhysicsConditions.Default with { CreativeFlying = true, MayFly = true, GameMode = GameMode.Creative };

        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(flying);
        engine.Reset(new Vec3d(0.5, 80, 0.5), 0f, 0f);

        double y0 = engine.State.Position.Y;
        for (int i = 0; i < 10; i++)
            engine.Step(new MovementInput { Jump = true });

        double yUp = engine.State.Position.Y;
        Assert.True(yUp > y0, "creative fly did not ascend on jump");

        for (int i = 0; i < 20; i++)
            engine.Step(new MovementInput { Sneak = true });

        Assert.True(engine.State.Position.Y < yUp, "creative fly did not descend on sneak");
    }

    [Fact]
    public void CreativeFly_DoesNotFallUnderGravity()
    {
        var world = new FixtureWorld();
        var flying = PhysicsConditions.Default with { CreativeFlying = true, MayFly = true, GameMode = GameMode.Creative };
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(flying);
        engine.Reset(new Vec3d(0.5, 100, 0.5), 0f, 0f);

        double y0 = engine.State.Position.Y;
        for (int i = 0; i < 10; i++)
            engine.Step(MovementInput.None);

        // Creative fly damps Y strongly; the player should barely move without input.
        Assert.True(Math.Abs(engine.State.Position.Y - y0) < 0.5, $"creative fly drifted too far: {y0} -> {engine.State.Position.Y}");
    }
}
