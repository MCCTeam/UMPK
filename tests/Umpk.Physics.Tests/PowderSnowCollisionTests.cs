using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Powder snow is the second block in the game whose collision shape depends on the body asking, and the only one where the deciding fact is what the body is wearing. These tests exercise every arm against a moving body.</summary>
/// <remarks>
/// <para>A fall distance above 2.5 presents a 0.9-tall box. Otherwise, leather boots present a full cube only when the feet are above the cell and the player is not sneaking. The rule is unchanged since 1.17, so nothing here is era-gated in code; powder snow simply does not exist below 1.17, which <c>BlockAttributeTests.PowderSnow_IsAbsentBeforeSeventeen</c> pins.</para>
/// <para><b>The fall arm is not the boots arm.</b> It comes FIRST and it applies to every entity, so a body that drops more than 2.5 blocks onto powder snow lands on a 0.9-tall box whether or not it is booted. On the next tick, landing has zeroed the fall distance and the boots arm asks <c>0.9 &gt; 0.99999</c> and gets false, and the body sinks in. Falling THROUGH is vanilla's long-drop behaviour and it is why powder snow breaks falls; a booted body does not get to land on top of one from height.</para>
/// </remarks>
public sealed class PowderSnowCollisionTests
{
    /// <summary>Facing +X (east): vanilla yaw 0 looks +Z, and -90 rotates the look vector to +X.</summary>
    private const float YawEast = -90f;

    /// <summary>The Y of the deck the body walks on, i.e. the top face of the powder-snow lane.</summary>
    private const double DeckY = 100.0;

    /// <summary>The Y a body that has sunk through the lane comes to rest at, on the stone beneath.</summary>
    private const double SunkY = 99.0;

    /// <summary>The first X of the powder-snow lane.</summary>
    private const int LaneStartX = 652;

    /// <summary>The last X of the powder-snow lane.</summary>
    private const int LaneEndX = 657;

    /// <summary>A flat deck at <see cref="DeckY"/> whose middle six columns are powder snow rather than stone. The snow sits ON stone, so a body that sinks has a floor one block down and the two outcomes are a whole block apart: rest at 100 (walked on it) or rest at 99 (fell into it).</summary>
    private static FixtureWorld NewWorld()
    {
        var world = new FixtureWorld().Fill(646, 98, 70, 660, 98, 77, BlockKind.Stone);
        for (int x = 646; x <= 660; x++)
        {
            BlockKind kind = x >= LaneStartX && x <= LaneEndX ? BlockKind.PowderSnow : BlockKind.Stone;
            for (int z = 70; z <= 77; z++)
                world.Set(x, 99, z, kind);

        }

        return world;
    }

    private static PlayerPhysics NewEngine(FixtureWorld world, Vec3d at, bool booted)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(PhysicsConditions.Default with { PowderSnowWalkable = booted });
        engine.Reset(at, YawEast, 0f);
        return engine;
    }

    /// <summary>THE CLAIM. A body with leather boots walks the lane at deck height and comes out the far side, never dropping into it.</summary>
    [Fact]
    public void BootedBody_WalksThePowderSnowLane_WithoutSinking()
    {
        var engine = NewEngine(NewWorld(), new Vec3d(648.5, DeckY, 74.5), booted: true);
        var walk = new MovementInput { Forward = true };

        double lowestY = DeckY;
        for (int tick = 0; tick < 200; tick++)
        {
            engine.Step(walk);
            lowestY = Math.Min(lowestY, engine.State.Position.Y);
            if (engine.State.Position.X > LaneEndX + 1.5)
                break;

        }

        Assert.True(
            engine.State.Position.X > LaneEndX + 1.5,
            $"the booted body did not cross the lane: it stopped at x={engine.State.Position.X}.");
        Assert.Equal(DeckY, lowestY, 6);
        Assert.True(engine.State.OnGround, "the booted body should be standing on the snow, not falling.");
    }

    /// <summary>The negative control, and the whole reason the feature is conditional: with no boots the lane is what it has always been, a hole that swallows the body.</summary>
    [Fact]
    public void BareBody_SinksIntoThePowderSnowLane()
    {
        var engine = NewEngine(NewWorld(), new Vec3d(648.5, DeckY, 74.5), booted: false);
        var walk = new MovementInput { Forward = true };

        // Step until the body is back on the ground, not merely below the deck; a mid-fall sample is not the settled lane height.
        for (int tick = 0; tick < 200; tick++)
        {
            engine.Step(walk);
            if (engine.State.OnGround && engine.State.Position.Y < DeckY - 0.5)
                break;

        }

        Assert.Equal(SunkY, engine.State.Position.Y, 6);
        Assert.True(
            engine.State.Position.X < LaneEndX + 1.0,
            $"a body that fell into the lane cannot have reached the far side; it is at x={engine.State.Position.X}.");
    }

    /// <summary>The <c>!isDescending()</c> arm, which is vanilla's way DOWN into powder snow: a booted body that holds sneak stops meeting the cube and drops in.</summary>
    /// <remarks>This case independently requires the <c>!descending</c> predicate; it complements the positive booted-body case and the equivalent scaffolding assertion.</remarks>
    [Fact]
    public void BootedBody_HoldingSneakOnTheLane_SinksIntoIt()
    {
        var engine = NewEngine(NewWorld(), new Vec3d(654.5, DeckY, 74.5), booted: true);
        var sneak = new MovementInput { Sneak = true };

        for (int tick = 0; tick < 60; tick++)
        {
            engine.Step(sneak);
            if (engine.State.OnGround && engine.State.Position.Y < DeckY - 0.5)
                break;

        }

        Assert.Equal(SunkY, engine.State.Position.Y, 6);
    }

    /// <summary>The from-above arm, and the only row that can see it. A booted body that is already inside the snow - it fell in, or a piston shoved it in - meets nothing and walks out sideways.</summary>
    /// <remarks>
    /// <para>A body resting inside a cell is not stopped by that cell's cube on the vertical axis: <c>Aabb.Collide</c> only registers a downward hit when the box's <c>MinY</c> is at or above the collider's <c>MaxY</c>, and 99.9 is neither. The term's whole job is HORIZONTAL, and it is the difference between a body that walks out of the snow it fell into and a body welded in place by a cube it is standing in the middle of.</para>
    /// <para>Vanilla needs it for exactly that reason: The from-above test against a full-block shape keeps walkable powder snow from turning the snow around a sunken player into a solid tomb.</para>
    /// </remarks>
    [Fact]
    public void BootedBody_InsideThePowderSnow_IsNotHeldByIt()
    {
        var engine = NewEngine(NewWorld(), new Vec3d(654.5, SunkY, 74.5), booted: true);
        var walk = new MovementInput { Forward = true };

        for (int tick = 0; tick < 120; tick++)
            engine.Step(walk);

        // The lane's far wall is the stone column at x=658, a full 1.0 rise the 0.6 auto-step refuses, so a body that is free to move ends flush against it rather than out the other side.
        Assert.True(
            engine.State.Position.X > LaneEndX + 0.5,
            $"a booted body inside the snow was held by it: it is at x={engine.State.Position.X}.");
        Assert.Equal(SunkY, engine.State.Position.Y, 6);
    }

    /// <summary>A booted body standing still on the lane stays on the lane. Separated from the walking row because the from-above check is an inequality about the feet, and a body at rest sits exactly on the boundary the 1e-5 epsilon defends.</summary>
    [Fact]
    public void BootedBody_StandingStillOnTheLane_DoesNotSettleThroughIt()
    {
        var engine = NewEngine(NewWorld(), new Vec3d(654.5, DeckY, 74.5), booted: true);

        for (int tick = 0; tick < 100; tick++)
            engine.Step(default);

        Assert.Equal(DeckY, engine.State.Position.Y, 6);
        Assert.True(engine.State.OnGround);
    }

    /// <summary>The FALL arm, and it is deliberately driven by a BARE body: vanilla checks <c>fallDistance &gt; 2.5</c> before it ever asks about boots, so the 0.9-tall box catches everyone. A body dropped six blocks onto the lane comes to rest at 99.9, not at 99.</summary>
    [Fact]
    public void BareBody_FallingMoreThanTwoAndAHalfBlocks_LandsOnTheNineTenthsBox()
    {
        var engine = NewEngine(NewWorld(), new Vec3d(654.5, DeckY + 6.0, 74.5), booted: false);

        double restY = double.NaN;
        for (int tick = 0; tick < 60; tick++)
        {
            engine.Step(default);
            if (engine.State.OnGround)
            {
                restY = engine.State.Position.Y;
                break;
            }
        }

        Assert.Equal(SunkY + 0.9, restY, 6);
    }

    /// <summary>The tick AFTER the one above. Landing zeroes the fall distance, so the fall arm stops firing; the boots arm then asks <c>0.9 &gt; 1.0 - 1e-5</c> and gets false even for a booted body, and the body sinks the rest of the way. This is what makes powder snow a fall-breaker rather than a trampoline, and it is why a plan must not treat a long drop onto snow as a landing.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABodyThatLandedOnTheNineTenthsBox_ThenSinks(bool booted)
    {
        var engine = NewEngine(NewWorld(), new Vec3d(654.5, DeckY + 6.0, 74.5), booted);

        for (int tick = 0; tick < 200; tick++)
            engine.Step(default);

        Assert.Equal(SunkY, engine.State.Position.Y, 6);
    }

    /// <summary>The other side of the fall arm's threshold: a SHORT drop never reaches 2.5 blocks, so a bare body meets nothing and passes straight through. Without this row the fall arm could be implemented as "always give a 0.9 box" and every test above would still pass.</summary>
    [Fact]
    public void BareBody_FallingLessThanTwoAndAHalfBlocks_PassesStraightThrough()
    {
        var engine = NewEngine(NewWorld(), new Vec3d(654.5, DeckY + 1.5, 74.5), booted: false);

        double highestRest = double.NaN;
        for (int tick = 0; tick < 60; tick++)
        {
            engine.Step(default);
            if (engine.State.OnGround)
            {
                highestRest = engine.State.Position.Y;
                break;
            }
        }

        Assert.Equal(SunkY, highestRest, 6);
    }

    /// <summary>The conditions are the ONLY thing that turns the cube on. A body on the same lane with the same geometry and <see cref="PhysicsConditions.Default"/> does not walk on powder snow.</summary>
    [Fact]
    public void DefaultConditions_DoNotWalkOnPowderSnow()
    {
        Assert.False(PhysicsConditions.Default.PowderSnowWalkable);
    }
}
