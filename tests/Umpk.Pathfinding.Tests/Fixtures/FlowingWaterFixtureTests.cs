using Umpk.Geometry;
using Umpk.Physics;
using Xunit;

namespace Umpk.Pathfinding.Tests.Fixtures;

/// <summary>Fixture data for <c>FlowingWaterFixtureTests</c>.</summary>
public sealed class FlowingWaterFixtureTests
{
    /// <summary>Fixture data for <c>AFlowingChannel_PushesAFloatingBody</c>.</summary>
    [Fact]
    public void AFlowingChannel_PushesAFloatingBody()
    {
        var world = new FixtureWorld();
        // A 2-high walled channel running +x, floor at y=63, water in y=64..65.
        world.Fill(-2, 62, -2, 12, 62, 2, FixtureWorld.Stone);
        world.Fill(-2, 63, -2, 12, 68, 2, FixtureWorld.Stone);
        world.Fill(-1, 63, -1, 11, 66, 1, FixtureWorld.Air);
        world.FlowingRun(0, 64, 0, length: 9, stepX: 1, stepZ: 0, layers: 2);

        PlanningWorldView view = world.Capture(new BlockPos(-2, 60, -2), new BlockPos(12, 70, 2));
        var engine = new PlayerPhysics(view, PhysicsProfile.ForProtocol(772));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(4.5, 64.0, 0.5), 0f, 0f);

        for (int i = 0; i < 40; i++)
            engine.Step(MovementInput.None);

        double drift = engine.State.Position.X - 4.5;
        Assert.True(
            drift > 0.5,
            $"a body left to float in the fixture's flowing channel drifted {drift:F4} blocks downstream, "
            + "so the fixture produces no current at all");
    }

    /// <summary>A falling column pushes DOWN, which is the deep-water case: every cell of an enclosed waterfall shaft carries the same downward unit flow, so a swimmer inside one is fighting the current over its whole body rather than only at the surface.</summary>
    [Fact]
    public void AFallingColumn_PushesABodyDown()
    {
        var world = new FixtureWorld();
        world.Fill(-2, 40, -2, 2, 92, 2, FixtureWorld.Stone);
        world.Fill(0, 41, 0, 0, 91, 0, FixtureWorld.Air);
        world.FallingColumn(0, 42, 0, height: 48);

        PlanningWorldView view = world.Capture(new BlockPos(-2, 38, -2), new BlockPos(2, 94, 2));
        var engine = new PlayerPhysics(view, PhysicsProfile.ForProtocol(772));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(0.5, 50.0, 0.5), 0f, 0f);

        double before = engine.State.Position.Y;
        for (int i = 0; i < 40; i++)
            engine.Step(new MovementInput { Jump = true });

        double climbed = engine.State.Position.Y - before;
        // The same shaft filled with plain sources climbs 6.3251 blocks over the same 40 ticks; the downward current costs 2.3123 of them, which is the 0.014 push run through WaterYDamping 0.8.
        Assert.True(
            climbed < 5.0,
            $"holding Jump inside the fixture's falling column climbed {climbed:F4} blocks in 40 ticks, "
            + "against 6.3251 in still water, so the column exerts no downward current");
    }
}
