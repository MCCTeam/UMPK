using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Scaffolding is the one block in the game whose collision shape answers differently depending on where the body asking is. This file pins both answers against a body that actually walks.</summary>
/// <remarks>
/// <para>A body whose feet are at or above the cell's top face, and not sneaking, meets <c>SHAPE_STABLE</c> and stands on the 14/16-to-16/16 top plate; a body inside the column, or one sneaking on top of it, meets <b>nothing at all</b> - which is how a player climbs a scaffold and how shift-descending through one works.</para>
/// <para><b>What the dataset records</b> is the context-free <c>SHAPE_STABLE</c> because a shape table has no entity to ask. Applying it to a body inside the column incorrectly retains the top plate at 0.875. Stepping over it is a full 1.0 and the auto-step is 0.6, so the lateral entry <c>MoveHelper.CanWalkThrough</c> promises is then refused by the engine. The unstable bottom plate is not involved because <c>SHAPE_UNSTABLE_BOTTOM</c> needs <c>BOTTOM</c> and a non-zero <c>DISTANCE</c>, and a column standing on stone has neither.</para>
/// </remarks>
public sealed class ScaffoldingCollisionTests
{
    /// <summary>Facing +X (east): vanilla yaw 0 looks +Z, and -90 rotates the look vector to +X.</summary>
    private const float YawEast = -90f;

    /// <summary>A stone floor whose top is y=100, with a scaffolding column rising from y=100 at x=650.</summary>
    private static FixtureWorld NewWorld()
    {
        var world = new FixtureWorld().Floor(646, 653, 70, 77, 99, BlockKind.Stone);
        for (int y = 100; y <= 104; y++)
            world.Set(650, y, 74, BlockKind.Scaffolding);

        return world;
    }

    private static PlayerPhysics NewEngine(FixtureWorld world, Vec3d at)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(at, YawEast, 0f);
        return engine;
    }

    /// <summary>A body on the floor beside the column, pressed toward it, must end up inside the column's base cell. That is what a climb starts with, and it is what <c>CanWalkThrough</c> promises when it admits a climbable cell.</summary>
    [Fact]
    public void BodyOnTheFloor_WalksIntoTheColumnsBaseCell()
    {
        var engine = NewEngine(NewWorld(), new Vec3d(648.5, 100.0, 74.5));
        var walk = new MovementInput { Forward = true };

        int entered = -1;
        for (int tick = 0; tick < 60 && entered < 0; tick++)
        {
            engine.Step(walk);
            if (engine.State.Position.X >= 650.0)
                entered = tick;

        }

        Assert.True(
            entered >= 0,
            $"the body stopped short of the column at x={engine.State.Position.X:R}");

        // Inside the column, on the floor the column stands on, not lifted onto a plate.
        Assert.Equal(100.0, engine.State.Position.Y, 3);
    }

    /// <summary>The other half of the same rule, and the reason the shape is not simply deleted: a body dropped onto the column's top comes to rest ON it, at the cell's top face, because from up there the shape is <c>SHAPE_STABLE</c> and its plate tops out at 1.0.</summary>
    [Fact]
    public void BodyAboveTheColumn_RestsOnItsTopFace()
    {
        var engine = NewEngine(NewWorld(), new Vec3d(650.5, 106.0, 74.5));

        for (int tick = 0; tick < 60; tick++)
            engine.Step(default);

        Assert.Equal(105.0, engine.State.Position.Y, 3);
        Assert.True(engine.State.OnGround, "the body did not come to rest on the scaffolding's top face");
    }

    /// <summary>Sneaking on top of a scaffold drops the body through it: the not-descending predicate guard is the whole of shift-descending a scaffold, and without it a sneaking player is welded to the top plate.</summary>
    [Fact]
    public void SneakingOnTop_FallsThroughTheTopPlate()
    {
        var engine = NewEngine(NewWorld(), new Vec3d(650.5, 105.0, 74.5));
        var sneak = new MovementInput { Sneak = true };

        for (int tick = 0; tick < 60; tick++)
            engine.Step(sneak);

        Assert.True(
            engine.State.Position.Y < 105.0,
            $"a sneaking body stayed on the plate at y={engine.State.Position.Y:R}");
    }
}
