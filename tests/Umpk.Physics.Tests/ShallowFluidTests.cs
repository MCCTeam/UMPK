using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Tests the fluid jump gate and fluid-first travel dispatch. A player standing on the ground in a fluid no deeper than the jump threshold (0.4) performs a FULL ground jump; only deep fluid gets the 0.04 liquid impulse. A gliding player who enters water travels as a swimmer, not on elytra math.</summary>
public sealed class ShallowFluidTests
{
    private static PlayerPhysics NewEngine(FixtureWorld world, Vec3d start, PhysicsConditions? c = null, float yaw = 0f, float pitch = 0f)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(c ?? PhysicsConditions.Default);
        engine.Reset(start, yaw, pitch);
        engine.SetRotation(yaw, pitch);
        return engine;
    }

    private static FixtureWorld FloorWithFluidLayer(BlockKind fluid, int layers)
    {
        var world = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.Stone);
        world.Fill(-5, 64, -5, 5, 63 + layers, 5, fluid);
        return world;
    }

    // Settles the engine onto the floor, then jumps once; returns the post-jump-tick Y velocity.
    private static double JumpTickVelocity(FixtureWorld world)
    {
        var engine = NewEngine(world, new Vec3d(0.5, 64, 0.5));
        for (int i = 0; i < 5; i++)
            engine.Step(MovementInput.None);

        Assert.True(engine.State.OnGround, "player did not settle on the floor");
        engine.Step(new MovementInput { Jump = true });
        return engine.State.Velocity.Y;
    }

    [Fact]
    public void KneeDeepWater_GroundJump_IsFullJump()
    {
        // Flowing water level 6: fluid height 2/9 (~0.22) <= threshold 0.4, so onGround jumping calls jumpFromGround (0.42 power), not the 0.04 liquid impulse.
        double vy = JumpTickVelocity(FloorWithFluidLayer(BlockKind.FlowingWater, 1));

        // 0.42 through water travel damping: 0.42 * 0.8 - 0.08/16 = 0.331.
        Assert.True(vy > 0.25, $"knee-deep water jump was not a full ground jump, vy={vy}");
    }

    [Fact]
    public void SwimmingDepthWater_Jump_IsLiquidImpulse()
    {
        // Source water three blocks deep: fluid height >= 1.0 > threshold, so jumping applies the 0.04 jumpInLiquid impulse even while on the bottom.
        double vy = JumpTickVelocity(FloorWithFluidLayer(BlockKind.Water, 3));

        Assert.True(vy < 0.15, $"deep water jump should be the small liquid impulse, vy={vy}");
    }

    [Fact]
    public void KneeDeepLava_GroundJump_IsFullJump()
    {
        // The shallow-fluid gate applies to lava as well.
        double vy = JumpTickVelocity(FloorWithFluidLayer(BlockKind.FlowingLava, 1));

        Assert.True(vy > 0.25, $"knee-deep lava jump was not a full ground jump, vy={vy}");
    }

    [Fact]
    public void DeepLava_Jump_IsLiquidImpulse()
    {
        double vy = JumpTickVelocity(FloorWithFluidLayer(BlockKind.Lava, 3));

        Assert.True(vy < 0.15, $"deep lava jump should be the small liquid impulse, vy={vy}");
    }

    [Fact]
    public void ShallowWaterJump_UsesJumpDelay_LiquidImpulseDoesNot()
    {
        // jumpFromGround arms the 10-tick noJumpDelay; jumpInLiquid never does. Holding jump in shallow water must not re-jump on the immediately following tick.
        var engine = NewEngine(FloorWithFluidLayer(BlockKind.FlowingWater, 1), new Vec3d(0.5, 64, 0.5));
        for (int i = 0; i < 5; i++)
            engine.Step(MovementInput.None);

        engine.Step(new MovementInput { Jump = true });
        double firstJumpVy = engine.State.Velocity.Y;
        engine.Step(new MovementInput { Jump = true });
        double secondTickVy = engine.State.Velocity.Y;

        Assert.True(firstJumpVy > 0.25, $"first tick was not a full jump, vy={firstJumpVy}");
        Assert.True(secondTickVy < firstJumpVy, $"second tick should not re-jump under the 10-tick delay, vy={secondTickVy}");
    }

    // Fluid travel dispatch precedes elytra.

    private static FixtureWorld DeepWaterColumn() => new FixtureWorld().Fill(-3, 55, -3, 3, 85, 3, BlockKind.Water);

    [Fact]
    public void GlidingIntoWater_TravelsAsSwimmer_NotElytra()
    {
        // Fluid travel takes precedence over fall-flying, so a player whose glide flag is still set behaves identically to a non-gliding swimmer while submerged. Step two engines through identical inputs; every tick must match exactly.
        var gliding = NewEngine(DeepWaterColumn(), new Vec3d(0.5, 70, 0.5), PhysicsConditions.Default with { ElytraFlying = true });
        var swimming = NewEngine(DeepWaterColumn(), new Vec3d(0.5, 70, 0.5));

        var input = new MovementInput { Forward = true };
        for (int i = 0; i < 15; i++)
        {
            PhysicsState g = gliding.Step(input).State;
            PhysicsState s = swimming.Step(input).State;
            Assert.Equal(s.Position, g.Position);
            Assert.Equal(s.Velocity, g.Velocity);
        }
    }

    [Fact]
    public void GlidingInAir_StillUsesElytraTravel()
    {
        // Sanity for the reorder: out of fluid the glide flag still selects elytra math, so the gliding trajectory must diverge from the plain-air one.
        var air = new FixtureWorld();
        var gliding = NewEngine(air, new Vec3d(0.5, 120, 0.5), PhysicsConditions.Default with { ElytraFlying = true });
        var falling = NewEngine(air, new Vec3d(0.5, 120, 0.5));

        var input = new MovementInput { Forward = true };
        for (int i = 0; i < 10; i++)
        {
            gliding.Step(input);
            falling.Step(input);
        }

        Assert.NotEqual(falling.State.Velocity.Y, gliding.State.Velocity.Y);
    }

    [Fact]
    public void DeepLava_SinksAtUniformScaleTerminalVelocity()
    {
        // Deep lava scales the whole velocity uniformly by 0.5, giving terminal sink speed -0.04. Sinking through a deep column must show that behavior.
        var engine = NewEngine(new FixtureWorld().Fill(-3, 40, -3, 3, 80, 3, BlockKind.Lava), new Vec3d(0.5, 75, 0.5));
        for (int i = 0; i < 30; i++)
            engine.Step(MovementInput.None);

        double vy = engine.State.Velocity.Y;
        Assert.True(vy > -0.08 && vy < -0.02, $"deep lava terminal sink speed should be near -0.04, vy={vy}");
    }
}
