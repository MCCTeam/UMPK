using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Pose transitions: standing, crouch, swim, crawl under low ceilings, elytra fall-flying.</summary>
public sealed class PoseTests
{
    private static PlayerPhysics NewEngine(FixtureWorld world, Vec3d start, PhysicsProfile? profile = null, PhysicsConditions? c = null, float pitch = 0f)
    {
        var engine = new PlayerPhysics(world, profile ?? PhysicsProfile.Modern);
        engine.SetConditions(c ?? PhysicsConditions.Default);
        engine.Reset(start, 0f, pitch);
        engine.SetRotation(0f, pitch);
        return engine;
    }

    [Fact]
    public void Sneak_EntersCrouchPose()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(0.5, 64, 0.5));

        StepResult r = engine.Step(new MovementInput { Sneak = true });
        Assert.Equal(EntityPose.Crouching, r.State.Pose);
        Assert.True(r.Events.PoseChanged);
        Assert.Equal(EntityPose.Standing, r.Events.PreviousPose);
        Assert.Equal(PhysicsConstants.PlayerCrouchHeightModern, r.State.BoundingBox.YSize, 6);
    }

    [Fact]
    public void LowCeiling_ForcesCrawlPose()
    {
        // A 1-block-tall tunnel forces the swimming (crawl) pose on land.
        var world = new FixtureWorld()
            .Floor(-5, 5, -5, 20, 63, BlockKind.Stone) // floor at y=63, feet at y=64
            .Floor(-5, 5, -5, 20, 65, BlockKind.Stone); // ceiling at y=65 -> 1 block gap

        var engine = NewEngine(world, new Vec3d(0.5, 64, 0.5));
        StepResult r = engine.Step(MovementInput.None);
        Assert.Equal(EntityPose.Swimming, r.State.Pose);
        Assert.Equal(PhysicsConstants.PlayerSwimHeight, r.State.BoundingBox.YSize, 6);
    }

    [Fact]
    public void StandingClears_ReturnsToStandingPose()
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(0.5, 64, 0.5));

        engine.Step(new MovementInput { Sneak = true });
        Assert.Equal(EntityPose.Crouching, engine.State.Pose);
        StepResult r = engine.Step(MovementInput.None);
        Assert.Equal(EntityPose.Standing, r.State.Pose);
    }

    [Fact]
    public void SwimSprintUnderwater_EntersSwimPose()
    {
        var world = new FixtureWorld().Fill(-2, 40, -2, 2, 90, 2, BlockKind.Water);
        var engine = NewEngine(world, new Vec3d(0.5, 60, 0.5));
        StepResult r = engine.Step(new MovementInput { Forward = true, Sprint = true });
        Assert.Equal(EntityPose.Swimming, r.State.Pose);
        Assert.True(r.State.IsSwimming);
    }

    [Fact]
    public void ElytraFlying_EntersFallFlyingPose()
    {
        var world = new FixtureWorld();
        var engine = NewEngine(world, new Vec3d(0.5, 100, 0.5), pitch: 0f);

        // First tick not gliding, then the host pushes ElytraFlying and we observe the transition.
        engine.Step(MovementInput.None);
        engine.SetConditions(PhysicsConditions.Default with { ElytraEquipped = true, ElytraFlying = true });
        StepResult r = engine.Step(MovementInput.None);
        Assert.Equal(EntityPose.FallFlying, r.State.Pose);
        Assert.True(r.State.IsGliding);
        Assert.True(r.Events.StartedGliding);
    }

    [Fact]
    public void LegacyProfile_UsesTallerCrouchBox()
    {
        // Pre-1.14 crouch height is 1.65, not 1.5.
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        var legacy = PhysicsProfile.ForProtocol(47);
        var engine = NewEngine(world, new Vec3d(0.5, 64, 0.5), profile: legacy);

        StepResult r = engine.Step(new MovementInput { Sneak = true });
        Assert.Equal(EntityPose.Crouching, r.State.Pose);
        Assert.Equal(PhysicsConstants.PlayerCrouchHeightLegacy, r.State.BoundingBox.YSize, 6);
    }
}
