using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>
/// The fluid CURRENT: vanilla <c>fluid-contact processing</c> plus <c>fluid-flow calculation</c>. Unlike knockback or a piston push, no packet carries this: the client's own engine has to derive the flow vector from the neighbouring fluid levels every tick and add it to its velocity, so a client that does not model it stands byte-still in a river.
/// <para>Every assertion below reads the POSITION, or the settled VELOCITY, after stepping the engine, never a field the push wrote. The engine recomputes its velocity from its own state each tick, so a push that is written and then overwritten is indistinguishable from a correct one at the field level.</para>
/// </summary>
public sealed class FluidCurrentTests
{
    private const int FloorY = 63;
    private const int FluidY = 64;

    /// <summary>Vanilla's water push scale, 0.014, against vanilla's in-water horizontal drag, 0.8: v(n+1) = (v(n) + 0.014) * 0.8 has the fixed point 0.056.</summary>
    private const double TerminalWaterDrift = 0.014 * 0.8 / (1.0 - 0.8);

    private static PlayerPhysics NewEngine(FixtureWorld world, Vec3d start, PhysicsConditions? conditions = null)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(conditions ?? PhysicsConditions.Default);
        engine.Reset(start, 0f, 0f);
        return engine;
    }

    /// <summary>An eastward channel: a source column, then one block per descending fluid level. The per-block step down is the height gradient <c>fluid-flow calculation</c> reads, so every interior cell has a flow of exactly +X.</summary>
    /// <param name="layers">Fluid layers stacked at the feet. Two makes the fluid height a full 1.0, so the per-cell flow is not depth-scaled; one leaves it shallow and scaled.</param>
    private static FixtureWorld WaterChannel(int layers)
    {
        var world = new FixtureWorld();
        world.Fill(-2, FloorY, -2, 8, FloorY, 2, BlockKind.Stone);
        BlockKind[] ramp =
        [
            BlockKind.Water, BlockKind.WaterLevel1, BlockKind.WaterLevel2,
            BlockKind.WaterLevel3, BlockKind.WaterLevel4,
        ];

        for (int y = FluidY; y < FluidY + layers; y++)
            for (int x = 0; x < ramp.Length; x++)
                world.Fill(x, y, -1, x, y, 1, ramp[x]);

        return world;
    }

    /// <summary>The same channel with no gradient: every cell a source. Vanilla flow is exactly zero.</summary>
    private static FixtureWorld StillPool()
    {
        var world = new FixtureWorld();
        world.Fill(-2, FloorY, -2, 8, FloorY, 2, BlockKind.Stone);
        world.Fill(0, FluidY, -1, 4, FluidY + 1, 1, BlockKind.Water);
        return world;
    }

    private static FixtureWorld LavaChannel()
    {
        var world = new FixtureWorld();
        world.Fill(-2, FloorY, -2, 8, FloorY, 2, BlockKind.Stone);
        BlockKind[] ramp = [BlockKind.Lava, BlockKind.LavaLevel1, BlockKind.LavaLevel2, BlockKind.FlowingLava];
        for (int y = FluidY; y < FluidY + 2; y++)
            for (int x = 0; x < ramp.Length; x++)
                world.Fill(x, y, -1, x, y, 1, ramp[x]);

        return world;
    }

    private static PlayerPhysics Settle(FixtureWorld world, int ticks, PhysicsConditions? conditions = null)
    {
        PlayerPhysics engine = NewEngine(world, new Vec3d(0.5, FluidY, 0.5), conditions);
        for (int tick = 0; tick < ticks; tick++)
            engine.Step(MovementInput.None);

        return engine;
    }

    [Fact]
    public void FlowingWater_CarriesAStationaryPlayerDownstream()
    {
        PhysicsState state = Settle(WaterChannel(layers: 2), 60).State;

        Assert.True(state.Position.X - 0.5 > 2.0, $"the current did not carry the player downstream: {state.Position}");
        Assert.True(Math.Abs(state.Position.Z - 0.5) < 0.05, $"the current pushed sideways: {state.Position}");
    }

    [Fact]
    public void StillWater_DoesNotMoveThePlayerAtAll()
    {
        // The control that separates "the push works" from "anything in water drifts".
        PhysicsState state = Settle(StillPool(), 60).State;

        Assert.Equal(0.5, state.Position.X, 12);
        Assert.Equal(0.5, state.Position.Z, 12);
    }

    [Fact]
    public void FlowingWater_SettlesAtVanillasTerminalDriftSpeed()
    {
        // A wrong push scale lands at a different fixed point, which no directional assertion catches.
        PhysicsState state = Settle(WaterChannel(layers: 2), 40).State;

        Assert.Equal(TerminalWaterDrift, state.Velocity.X, 4);
    }

    [Fact]
    public void ShallowWater_IsPushedMoreWeaklyThanDeepWater()
    {
        // The per-cell flow is multiplied by the fluid depth while that depth is under 0.4 (fluid-contact processing). A single sheet at levels 5, 6 and 7 is 3/9, 2/9 and 1/9 deep, all under the threshold, so the same +X gradient pushes measurably less hard than the two-layer channel, whose depth is a full 1.0.
        var shallowWorld = new FixtureWorld();
        shallowWorld.Fill(-2, FloorY, -2, 8, FloorY, 2, BlockKind.Stone);
        shallowWorld.Fill(0, FluidY, -1, 0, FluidY, 1, BlockKind.WaterLevel5);
        shallowWorld.Fill(1, FluidY, -1, 1, FluidY, 1, BlockKind.FlowingWater);
        shallowWorld.Fill(2, FluidY, -1, 2, FluidY, 1, BlockKind.WaterLevel7);

        double shallow = Settle(shallowWorld, 20).State.Velocity.X;
        double deep = Settle(WaterChannel(layers: 2), 20).State.Velocity.X;

        Assert.True(shallow > 0.0, $"shallow flowing water did not push at all: {shallow}");
        Assert.True(shallow < deep / 2.0, $"the depth scale was not applied: shallow {shallow}, deep {deep}");
    }

    [Fact]
    public void FallingWater_AgainstASolidFace_PushesDownAndAwayFromIt()
    {
        // fluid-flow calculation: a FALLING fluid state (block level 8 or more) whose neighbour presents a solid face adds (0, -6, 0) to the flow before the final normalize, so the current in a waterfall running down a wall is straight DOWN. In a 1x1 column with nothing but air beside it the horizontal gradient is zero and no solid face is found, so there is no push at all and the player sinks at plain buoyancy speed.
        static FixtureWorld Shaft(bool withWall)
        {
            var world = new FixtureWorld();
            world.Fill(-3, FloorY, -3, 3, FloorY, 3, BlockKind.Stone);
            world.Fill(0, FloorY + 1, 0, 0, FloorY + 12, 0, BlockKind.FallingWater);
            if (withWall)
                world.Fill(1, FloorY + 1, 0, 1, FloorY + 12, 0, BlockKind.Stone);

            return world;
        }

        static PhysicsState Sink(FixtureWorld world)
        {
            var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
            engine.SetConditions(PhysicsConditions.Default);
            engine.Reset(new Vec3d(0.5, FloorY + 9, 0.5), 0f, 0f);
            for (int tick = 0; tick < 40; tick++)
                engine.Step(MovementInput.None);

            return engine.State;
        }

        PhysicsState open = Sink(Shaft(withWall: false));
        PhysicsState againstWall = Sink(Shaft(withWall: true));

        Assert.True(againstWall.Position.Y < open.Position.Y - 0.05,
            $"the waterfall did not pull the player down: open {open.Position}, wall {againstWall.Position}");

        // The added term is purely vertical here, so neither run drifts horizontally.
        Assert.Equal(0.5, againstWall.Position.X, 9);
        Assert.Equal(0.5, open.Position.X, 9);
    }

    [Fact]
    public void CreativeFlyingPlayer_IsNotPushedByTheCurrent()
    {
        // pushed by fluid behavior is `!abilities.flying`.
        var conditions = PhysicsConditions.Default with
        {
            CreativeFlying = true,
            GameMode = Umpk.Game.Players.GameMode.Creative,
            MayFly = true,
        };

        PhysicsState state = Settle(WaterChannel(layers: 2), 60, conditions).State;
        Assert.Equal(0.5, state.Position.X, 12);
    }

    [Fact]
    public void Lava_UsesItsOwnScale_AndTheNetherIsStronger()
    {
        double overworld = Settle(LavaChannel(), 60).State.Velocity.X;
        double nether = Settle(LavaChannel(), 60, PhysicsConditions.Default with { UltraWarmDimension = true })
            .State.Velocity.X;
        double water = Settle(WaterChannel(layers: 2), 60).State.Velocity.X;

        // Lava's horizontal damping is 0.5 (travel in lava behavior), so v(n+1) = 0.5 * (v(n) + push) settles at exactly the push scale. In an ultra-warm dimension that is 0.007, which is above the 0.003 velocity-zeroing threshold, so the fixed point is clean.
        Assert.Equal(PhysicsConstants.LavaPushScaleUltraWarm, nether, 6);

        // Outside the nether the scale is 0.0023333333333333335, which settles BELOW the 0.003 threshold, so vanilla's own 0.0045 minimum-push floor keeps re-engaging and the speed hovers just above 0.003 instead. That is why this is not a clean 3x ratio.
        Assert.True(overworld > 0.0, $"lava did not push at all: {overworld}");
        Assert.True(nether > overworld * 2.0, $"the nether lava push was not stronger: {overworld} vs {nether}");

        Assert.True(water > overworld * 3.0, $"water did not out-push overworld lava: water {water}, lava {overworld}");
    }
}
