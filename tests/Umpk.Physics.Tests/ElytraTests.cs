using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Elytra fall-flying travel.</summary>
public sealed class ElytraTests
{
    [Fact]
    public void Gliding_LevelFlightLosesLittleAltitudePerTick()
    {
        // Looking straight ahead (pitch 0) with forward speed: glide should convert to lift, descending far slower than free-fall gravity (0.08/tick).
        var world = new FixtureWorld();
        var start = new PhysicsState
        {
            Position = new Vec3d(0.5, 100, 0.5),
            Velocity = new Vec3d(0, 0, 1.0),
            Yaw = 0f,
            Pitch = 0f,
        };
        var conditions = PhysicsConditions.Default with { ElytraEquipped = true, ElytraFlying = true };

        PhysicsState next = PhysicsSimulator.Run(start, conditions, world, PhysicsProfile.Modern, [MovementInput.None]);
        double dropPerTick = start.Position.Y - next.Position.Y;
        Assert.True(dropPerTick < 0.08, $"level glide dropped like free-fall: {dropPerTick}");
        // And it kept moving forward.
        Assert.True(next.Position.Z > start.Position.Z + 0.5, "glider did not keep forward momentum");
    }

    [Fact]
    public void Gliding_DivingNoseDownGainsSpeed()
    {
        var world = new FixtureWorld();
        var start = new PhysicsState
        {
            Position = new Vec3d(0.5, 200, 0.5),
            Velocity = new Vec3d(0, 0, 1.0),
            Yaw = 0f,
            Pitch = 45f, // nose down
        };
        var conditions = PhysicsConditions.Default with { ElytraEquipped = true, ElytraFlying = true };

        var inputs = new MovementInput[20];
        PhysicsState next = PhysicsSimulator.Run(start, conditions, world, PhysicsProfile.Modern, inputs);
        double horizontalSpeed = Math.Sqrt(next.Velocity.HorizontalDistanceSqr());
        Assert.True(horizontalSpeed > 1.0, $"diving glide did not build speed: {horizontalSpeed}");
    }

    [Fact]
    public void Gliding_OnClimbableStopsFlying()
    {
        // Vanilla travelFallFlying: touching a climbable stops the glide and reverts to air travel.
        var world = new FixtureWorld()
            .Fill(0, 60, 1, 0, 90, 1, BlockKind.Stone)
            .Fill(0, 60, 0, 0, 90, 0, BlockKind.Ladder);

        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(PhysicsConditions.Default with { ElytraEquipped = true, ElytraFlying = true });
        engine.Reset(new Vec3d(0.5, 80, 0.5), 0f, 0f);

        StepResult r = engine.Step(MovementInput.None);
        // On a climbable the fall-flying travel branch is skipped; velocity is climb-clamped.
        Assert.True(r.State.OnClimbable);
        Assert.True(Math.Abs(r.State.Velocity.Y) <= PhysicsConstants.ClimbMaxSpeed + 1e-6);
    }
}
