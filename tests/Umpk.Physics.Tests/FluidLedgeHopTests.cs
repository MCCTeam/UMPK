using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>
/// Covers the fluid ledge hop: a player who is horizontally colliding while in a fluid, and whose bounding box offset by the current delta movement plus <c>0.6F</c> of the tick's net vertical travel would be free of both collision and liquid, gets a vertical velocity of exactly <c>0.3F</c>. This is what lets a swimmer climb out onto a shore.
///
/// <para>The condition and constants are identical across every supported era and both fluid arms, so there is no era gate.</para>
/// </summary>
public sealed class FluidLedgeHopTests
{
    // Vanilla's literals, transcribed rather than referenced, so an edited engine constant cannot move the expectation with it.
    private const double VanillaHopVelocity = 0.3f;

    /// <summary>A fluid pool whose surface is at y=65 with open air above, and a solid bank on the +Z side. The player stands at the bank, chest-deep, pressing forward into it: the probe box (raised by ~0.6) clears both the bank top and the water surface, so the hop condition holds.</summary>
    private static PlayerPhysics NewLedgeEngine(int protocol, BlockKind fluid)
    {
        var world = new FixtureWorld()
            .Fill(-2, 60, -2, 2, 64, 0, fluid)      // the pool, surface at the top of y=64
            .Fill(-2, 60, 1, 2, 64, 2, BlockKind.Stone);   // the bank, top face at y=65

        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, 64.5, 0.69), 0f, 0f);
        engine.SetRotation(0f, 0f);
        return engine;
    }

    /// <summary>The same pool with NO bank, so nothing is ever horizontally collided with.</summary>
    private static PlayerPhysics NewOpenEngine(int protocol, BlockKind fluid)
    {
        var world = new FixtureWorld().Fill(-2, 60, -2, 2, 64, 2, fluid);
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, 64.5, 0.69), 0f, 0f);
        engine.SetRotation(0f, 0f);
        return engine;
    }

    private static readonly MovementInput PressForward = new() { Forward = true };

    // (a) THE HOP ITSELF, water arm, every era. Vanilla sets exactly 0.3F.
    [Theory]
    [InlineData(47)]    // 1.8.9
    [InlineData(110)]   // 1.9.4
    [InlineData(340)]   // 1.12.2, the last pre-1.13 protocol
    [InlineData(404)]   // 1.13.2, the last protocol without the climbable bump
    [InlineData(477)]   // 1.14, the first protocol with the climbable bump
    [InlineData(754)]   // 1.16.5
    [InlineData(770)]   // 1.21.5, the shared-tail form
    [InlineData(776)]   // 26.2, the extracted jumpOutOfFluid
    public void WaterLedge_HopsOutWithVanillaVelocity(int protocol)
    {
        PlayerPhysics engine = NewLedgeEngine(protocol, BlockKind.Water);

        engine.Step(PressForward);

        Assert.True(engine.State.HorizontalCollision, "the fixture must actually collide with the bank");
        Assert.True(engine.State.InWater, "the fixture must actually be in water");
        Assert.Equal(VanillaHopVelocity, engine.State.Velocity.Y);
    }

    // (b) THE HOP ITSELF, lava arm, every era. Vanilla's lava branch carries the identical clause.
    [Theory]
    [InlineData(47)]
    [InlineData(110)]
    [InlineData(340)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(754)]
    [InlineData(770)]
    [InlineData(776)]
    public void LavaLedge_HopsOutWithVanillaVelocity(int protocol)
    {
        PlayerPhysics engine = NewLedgeEngine(protocol, BlockKind.Lava);

        engine.Step(PressForward);

        Assert.True(engine.State.HorizontalCollision, "the fixture must actually collide with the bank");
        Assert.True(engine.State.InLava, "the fixture must actually be in lava");
        Assert.Equal(VanillaHopVelocity, engine.State.Velocity.Y);
    }

    // (c) THE CONDITION IS NOT "IN FLUID AND COLLIDING": the free-position half matters as much as
    //     the 0.3. Submerged deep enough that the raised probe box is still inside liquid, vanilla
    //     does NOT hop, so the tick must stay on the ordinary sink trajectory.
    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(477)]
    [InlineData(776)]
    public void SubmergedAgainstAWall_DoesNotHop(int protocol)
    {
        // Deep pool: liquid all the way to y=70, so a box raised 0.6 is still full of it.
        var world = new FixtureWorld()
            .Fill(-2, 60, -2, 2, 70, 0, BlockKind.Water)
            .Fill(-2, 60, 1, 2, 70, 2, BlockKind.Stone);
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, 64.5, 0.69), 0f, 0f);
        engine.SetRotation(0f, 0f);

        engine.Step(PressForward);

        Assert.True(engine.State.HorizontalCollision);
        Assert.NotEqual(VanillaHopVelocity, engine.State.Velocity.Y);
        Assert.True(engine.State.Velocity.Y < 0.0, "a submerged player sinks instead of hopping");
    }

    // (d) NO HORIZONTAL COLLISION, NO HOP: the same pool without a bank pins the open-water and
    //     open-lava paths.
    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(477)]
    [InlineData(776)]
    public void OpenFluidWithoutACollision_NeverHops(int protocol)
    {
        PlayerPhysics water = NewOpenEngine(protocol, BlockKind.Water);
        PlayerPhysics lava = NewOpenEngine(protocol, BlockKind.Lava);

        for (int i = 0; i < 20; i++)
        {
            water.Step(PressForward);
            lava.Step(PressForward);
            Assert.False(water.State.HorizontalCollision);
            Assert.False(lava.State.HorizontalCollision);
            Assert.NotEqual(VanillaHopVelocity, water.State.Velocity.Y);
            Assert.NotEqual(VanillaHopVelocity, lava.State.Velocity.Y);
        }
    }

    // (e) ZERO-DIFF PIN FOR OPEN-FLUID PATHS. These literal outputs require collision-gated ledge hops
    //     to leave open-fluid trajectories bit-identical. Both fluid arms and both water-travel eras
    //     are covered.
    public static TheoryData<int, bool, double[], double[]> PreHopBaseline() => new()
    {
        // protocol, isLava, velocity Y at ticks {1,2,3,10,40}, position Y at the same ticks
        {
            47, false,
            [-0.02, -0.03600000023841858, -0.04880000061988831, -0.0892625854804646, -0.7350010595129539],
            [64.5, 64.48, 64.44399999976159, 63.94631289440002, 58.37450509054382]
        },
        {
            393, false,
            [-0.005, -0.009000000059604645, -0.012200000154972078, -0.02231564637011615, -0.6725318905002013],
            [64.5, 64.495, 64.4859999999404, 64.3615782236, 60.94770511144938]
        },
        {
            754, false,
            [-0.005, -0.009000000059604645, -0.012200000154972078, -0.02231564637011615, -0.6725318905002013],
            [64.5, 64.495, 64.4859999999404, 64.3615782236, 60.94770511144938]
        },
        {
            47, true,
            [-0.025, -0.045000000000000005, -0.0425, -0.040019531250000004, -0.04000000000001819],
            [64.5, 64.475, 64.42999999999999, 64.14503906249999, 62.945000000000036]
        },
        {
            393, true,
            [-0.025, -0.045000000000000005, -0.0425, -0.040019531250000004, -0.04000000000001819],
            [64.5, 64.475, 64.42999999999999, 64.14503906249999, 62.945000000000036]
        },
        {
            754, true,
            [-0.025, -0.045000000000000005, -0.0425, -0.040019531250000004, -0.04000000000001819],
            [64.5, 64.475, 64.42999999999999, 64.14503906249999, 62.945000000000036]
        },
    };

    [Theory]
    [MemberData(nameof(PreHopBaseline))]
    public void OpenFluid_IsByteIdenticalToThePreHopEngine(
        int protocol, bool isLava, double[] expectedVy, double[] expectedY)
    {
        int[] sampledTicks = [1, 2, 3, 10, 40];
        PlayerPhysics engine = NewOpenEngine(protocol, isLava ? BlockKind.Lava : BlockKind.Water);

        for (int tick = 1, sample = 0; tick <= 40; tick++)
        {
            engine.Step(PressForward);
            if (tick != sampledTicks[sample])
                continue;

            Assert.Equal(expectedVy[sample], engine.State.Velocity.Y);
            Assert.Equal(expectedY[sample], engine.State.Position.Y);
            sample++;
        }
    }

    // The collision half of IsFree is `noCollision(box) && !containsAnyLiquid(box)`. The liquid case above exercises only the second term, so this case independently requires collision clearance.
    //
    //     What can actually block the probe box is worth stating, because it is not what one first
    //     guesses: the probe box is the player box offset by the delta movement, and on a horizontal
    //     collision the collided axis is clamped flush to the block face (see the note on
    //     PlayerPhysics.ContainsAnyLiquid). Flush is touching, and AABB
    //     intersection is strict, so a bank of any height never intersects the probe box sideways -
    //     that is precisely why the hop can clear a bank at all. The only geometry that refuses the hop
    //     is something solid in the 0.6 of headroom the probe reaches into: an overhang. So this is the
    //     collision term's direct scenario: a body cannot climb out
    //     from under a rock shelf.
    private static PlayerPhysics NewOverhangEngine(int protocol, bool withOverhang)
    {
        var world = new FixtureWorld()
            .Fill(-2, 60, -2, 2, 64, 0, BlockKind.Water)          // pool, surface at the top of y=64
            .Fill(-2, 60, 1, 2, 64, 2, BlockKind.Stone);          // the bank

        if (withOverhang)
        {
            // A rock shelf over the pool. Its underside (y=67.0) sits above the player box top (64.7 + 1.8 = 66.5, no contact) but inside the probe box top (66.5 + 0.58 = 67.08), so it blocks the hop through the collision term and only through it: the probe box spans block rows y=65..67, none of which holds liquid.
            world.Fill(-2, 67, -2, 2, 67, 0, BlockKind.Stone);
        }

        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, 64.7, 0.69), 0f, 0f);
        engine.SetRotation(0f, 0f);
        return engine;
    }

    [Theory]
    [InlineData(47)]    // 1.8.9
    [InlineData(340)]   // 1.12.2
    [InlineData(477)]   // 1.14
    [InlineData(776)]   // 26.2
    public void LedgeUnderAnOverhang_RefusesTheHop(int protocol)
    {
        // Control: the identical fixture WITHOUT the overhang hops, so the only thing under test is the collision term. If this half ever fails, the fixture drifted rather than the engine.
        PlayerPhysics control = NewOverhangEngine(protocol, withOverhang: false);
        control.Step(PressForward);
        Assert.True(control.State.HorizontalCollision);
        Assert.True(control.State.InWater);
        Assert.Equal(VanillaHopVelocity, control.State.Velocity.Y);

        PlayerPhysics blocked = NewOverhangEngine(protocol, withOverhang: true);
        blocked.Step(PressForward);

        Assert.True(blocked.State.HorizontalCollision, "the fixture must still collide with the bank");
        Assert.True(blocked.State.InWater, "the fixture must still be in water");
        Assert.NotEqual(VanillaHopVelocity, blocked.State.Velocity.Y);
        Assert.True(blocked.State.Velocity.Y < 0.0, "a player under an overhang sinks instead of hopping");
    }

    // (e) HOW FAR THE HOP REACHES. The planner has to decide whether a pier is climbable out of the
    //     water, and the only lift available is this 0.3. It is re-applied every tick the condition
    //     holds, so a swimmer pressed against a bank rises steadily - but the condition needs the body
    //     to still be TOUCHING the fluid, and the moment it stops the rise becomes ballistic against
    //     gravity. That puts a hard ceiling on the exit height, and it lands between +1 and +2.
    //
    //     The pool's surface plane is y=65.0, so a +1 deck needs the feet to reach y=65.0 and a +2 deck
    //     needs y=66.0. Measured below over 300 ticks of Forward+Jump against a two-tall bank.
    [Theory]
    [InlineData(47, 65.920318328582226)]
    [InlineData(340, 65.920318328582226)]
    [InlineData(477, 65.920318328582226)]
    [InlineData(776, 65.920318328582226)]
    public void SurfaceHop_ReachesOneBlockOverTheWaterlineButNeverTwo(int protocol, double expectedApex)
    {
        var world = new FixtureWorld()
            .Fill(-2, 60, -2, 2, 64, 0, BlockKind.Water)          // pool, surface plane y=65.0
            .Fill(-2, 60, 1, 2, 65, 2, BlockKind.Stone);          // a TWO-tall bank: deck at y=66.0

        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, 64.5, 0.69), 0f, 0f);
        engine.SetRotation(0f, 0f);

        var input = new MovementInput { Forward = true, Jump = true };
        double apex = engine.State.Position.Y;
        for (int i = 0; i < 300; i++)
        {
            engine.Step(input);
            apex = Math.Max(apex, engine.State.Position.Y);
        }

        Assert.Equal(expectedApex, apex, 6);
        Assert.True(apex > 65.0, "the hop must clear a deck one block over the waterline");
        Assert.True(apex < 66.0, "the hop must NOT reach a deck two blocks over the waterline");
    }
}
