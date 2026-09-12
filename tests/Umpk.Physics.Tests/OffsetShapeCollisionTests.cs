using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>A body must collide with an offset block where the game puts it, not where the un-offset table puts it.</summary>
/// <remarks>
/// <para>The collider is <c>pointed_dripstone[thickness=tip,vertical_direction=up]</c> at <c>(-361, 5, 412)</c>. Vanilla's The collision shape is the translated tip shape, and at that block the offset is <c>(-0.125, 0, +0.05)</c>, which puts the box's +Z face at <b>412.7375</b>. UMPK baked every dripstone at a constant <c>(-0.125, -0.125)</c>, putting the same face at <b>412.5625</b> - 0.175 of nonexistent open air and causes the client to enter the collider.</para>
/// <para>At <c>(-359, 407)</c>, the position hash lands on <c>(-0.125, -0.125)</c>. That control position ensures the implementation does not apply a constant displacement to every block.</para>
/// </remarks>
public sealed class OffsetShapeCollisionTests
{
    /// <summary>Vanilla's offset at <c>(-361, *, 412)</c>, evaluated from BlockBehaviour's own expression.</summary>
    private const double StallOffsetX = -0.125;

    /// <summary>The Z component at the disagreement position.</summary>
    private const double StallOffsetZ = 0.050000011920928955;

    /// <summary><c>SHAPE_TIP_UP</c> = <c>column-shape construction</c>: 5/16 .. 11/16 horizontally.</summary>
    private const double TipMin = 0.3125;

    private const double TipMax = 0.6875;

    /// <summary>The position-adjusted collider in world coordinates.</summary>
    private static readonly Aabb VanillaDripstone = new(
        -361 + TipMin + StallOffsetX, 5.0, 412 + TipMin + StallOffsetZ,
        -361 + TipMax + StallOffsetX, 5.6875, 412 + TipMax + StallOffsetZ);

    /// <summary>The relevant cave geometry: a dripstone-block floor at y=4, the stalagmite tip at <c>(-361, 5, 412)</c>, and the stone the body was flush against at <c>(-360, 5..6, 413)</c> plus <c>(-361, 5, 414)</c>.</summary>
    private static FixtureWorld NewWorld() =>
        new FixtureWorld()
            .Floor(-366, -356, 408, 418, 4, BlockKind.Stone)
            .Set(-361, 5, 412, BlockKind.PointedDripstone)
            .Set(-360, 5, 413, BlockKind.Stone)
            .Set(-360, 6, 413, BlockKind.Stone)
            .Set(-361, 5, 414, BlockKind.Stone);

    /// <summary>Yaw 135 looks along the negative X and Z axes.</summary>
    private const float YawSouthWest = 135f;

    /// <summary>Walking the approach into the stalagmite must stop at the position-dependent box. Using the unoffset shape leaves 0.175 of space that the server does not accept.</summary>
    [Fact]
    public void WalkingIntoAnOffsetStalagmite_StopsWhereVanillaStops()
    {
        var engine = new PlayerPhysics(NewWorld(), PhysicsProfile.Modern);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(-360.943290, 5.0, 413.056710), YawSouthWest, 0f);
        var walk = new MovementInput { Forward = true };

        for (int tick = 0; tick < 60; tick++)
        {
            engine.Step(walk);
            Aabb body = engine.State.BoundingBox;

            Assert.False(
                body.Intersects(VanillaDripstone),
                $"tick {tick}: body {body.MinX:F6}..{body.MaxX:F6} x {body.MinZ:F6}..{body.MaxZ:F6} "
                + $"entered the vanilla stalagmite at {VanillaDripstone.MinX:F6}..{VanillaDripstone.MaxX:F6} "
                + $"x {VanillaDripstone.MinZ:F6}..{VanillaDripstone.MaxZ:F6}; position {engine.State.Position}");
        }
    }

    /// <summary>At <c>(-359, 407)</c>, the computed offset is exactly the low clamp. This control prevents a constant positive-Z adjustment from passing as a position-dependent implementation.</summary>
    [Fact]
    public void AtAnAgreeingBlock_TheColliderDoesNotMove()
    {
        const double agreeingOffset = -0.125;
        var expected = new Aabb(
            -359 + TipMin + agreeingOffset, 2.0, 407 + TipMin + agreeingOffset,
            -359 + TipMax + agreeingOffset, 2.6875, 407 + TipMax + agreeingOffset);

        var world = new FixtureWorld()
            .Floor(-364, -354, 402, 412, 1, BlockKind.Stone)
            .Set(-359, 2, 407, BlockKind.PointedDripstone);

        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(PhysicsConditions.Default);

        // Walk -Z into the stalagmite's +Z face from a lane that clears it in X only by the offset.
        engine.Reset(new Vec3d(-358.5, 2.0, 408.5), 180f, 0f);
        var walk = new MovementInput { Forward = true };

        for (int tick = 0; tick < 60; tick++)
        {
            engine.Step(walk);
            Assert.False(
                engine.State.BoundingBox.Intersects(expected),
                $"tick {tick}: the collider at an ALREADY-CORRECT block moved; position {engine.State.Position}");
        }

        // And it really is blocked, rather than passing the assertion by walking somewhere else.
        Assert.True(
            engine.State.Position.Z > expected.MaxZ,
            $"the body did not stay on the +Z side of the stalagmite: {engine.State.Position}");
    }
}
