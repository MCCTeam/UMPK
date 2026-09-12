using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>The flush-contact band: a player whose box rests exactly on a block boundary. The float half width makes the box 1.1920929e-8 wider than 0.6 per side, so a player at x=147.7 has maxX = 148.00000001192091758 and already overlaps the block column at x=148. Every case here exercises the 1e-7 contact epsilon in <see cref="Aabb.CollideX"/>/<c>CollideY</c>/<c>CollideZ</c>.</summary>
public sealed class FlushContactTests
{
    /// <summary>Facing +X (east): vanilla yaw 0 looks +Z, and -90 rotates the look vector to +X.</summary>
    private const float YawEast = -90f;

    /// <summary>The X plane the step block's west face sits on; the player box must never cross it.</summary>
    private const double WallPlaneX = 148.0;

    /// <summary>A flat stone floor with its top surface at y=79, plus one stone step at (148, 79, 380) whose top is y=80. The player spawns flush against that step's west face.</summary>
    private static FixtureWorld NewWorld() =>
        new FixtureWorld()
            .Floor(140, 155, 375, 385, 78, BlockKind.Stone)
            .Set(148, 79, 380, BlockKind.Stone);

    private static PlayerPhysics NewEngine(FixtureWorld world, float yaw)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(147.7, 79.0, 380.7), yaw, 0f);
        return engine;
    }

    /// <summary>Pressing forward into a wall that the box is already flush against must clip on the first tick. The contact epsilon accounts for the widened float half-width, where a nominally flush box extends about 1.2e-8 past the wall plane.</summary>
    [Fact]
    public void FlushWall_ForwardPressed_DoesNotPenetrate()
    {
        var engine = NewEngine(NewWorld(), YawEast);
        var walk = new MovementInput { Forward = true };

        for (int tick = 0; tick < 40; tick++)
        {
            engine.Step(walk);
            Assert.True(
                engine.State.BoundingBox.MaxX <= WallPlaneX + 1.0E-07,
                $"penetrated the wall at tick {tick}: maxX={engine.State.BoundingBox.MaxX:R}");

            if (tick == 0)
                Assert.True(engine.State.HorizontalCollision, "flush contact was not reported as a collision");

        }
    }

    /// <summary>The end-to-end recovery: from flush contact, forward + jump must actually climb the step. The horizontal clip is what makes the jump productive - the player rises, clears y=80, and the forward movement that was blocked at ground level is finally free.</summary>
    [Fact]
    public void FlushContactAscend_JumpAndForward_ReachesStepTop()
    {
        var engine = NewEngine(NewWorld(), YawEast);
        var jumpForward = new MovementInput { Forward = true, Jump = true };

        bool landed = false;
        for (int tick = 0; tick < 40 && !landed; tick++)
        {
            engine.Step(jumpForward);
            PhysicsState state = engine.State;

            if (state.Position.Y < 80.0)
                Assert.True(
                    state.BoundingBox.MaxX <= WallPlaneX + 1.0E-07,
                    $"penetrated the wall at tick {tick}: maxX={state.BoundingBox.MaxX:R}");

            landed = state.OnGround && state.Position.Y >= 80.0;
        }

        Assert.True(landed, $"never reached the step top: {engine.State.Position}");
        Assert.Equal(80.0, engine.State.Position.Y, 6);
        Assert.InRange(engine.State.Position.X, 148.0, 149.0);
    }

    /// <summary>At flush contact with no input, the player must settle on the floor. This ensures the contact epsilon does not prevent ground recovery.</summary>
    [Fact]
    public void FlushContact_AtRest_OnGroundRecovers()
    {
        var engine = NewEngine(NewWorld(), YawEast);

        for (int i = 0; i < 3; i++)
            engine.Step(MovementInput.None);

        Assert.True(engine.State.OnGround, $"did not settle on the floor: {engine.State.Position}");
        Assert.Equal(79.0, engine.State.Position.Y, 6);
    }

    /// <summary>A clipped flush wall reaches the step-up branch. The full block is taller than the 0.6 step height, so the empty candidate set must perform no step attempt and the player must stay at y=79.</summary>
    [Fact]
    public void FlushContact_StepUpPath_DoesNotMisfire()
    {
        var engine = NewEngine(NewWorld(), YawEast);
        var walk = new MovementInput { Forward = true };

        for (int tick = 0; tick < 40; tick++)
        {
            engine.Step(walk);
            Assert.True(
                engine.State.Position.Y <= 79.0 + 1.0E-07,
                $"levitated at tick {tick}: y={engine.State.Position.Y:R}");
        }
    }
}
