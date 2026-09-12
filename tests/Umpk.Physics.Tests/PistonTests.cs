using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>The <c>minecraft:moving_piston</c> push, checked against independent geometric expectations.</summary>
/// <remarks>
/// <para>A surviving one-tick push is 0.31 of a block and the full two-tick push is 0.81. Both come from <c>min(getMovement(area, dir, entityBox), delta) + PUSH_OFFSET</c>, so a test that reproduces them from the geometry alone is what tells "the client modelled the push" apart from "the server shoved the client": no server exists here.</para>
/// <para>The game's half width uses float arithmetic, so it is 0.300000011920928955078125 and the two totals are 0.31000001192092896 and 0.81000001192092896. The totals are asserted to the last meaningful bit so double halving cannot pass within tolerance.</para>
/// </remarks>
public sealed class PistonTests
{
    /// <summary>The two collision boxes of <c>minecraft:piston_head[facing=east,short=false]</c>, in local block coordinates. The north template as <c>axis-aligned box construction</c> (the plate) OR <c>axis-aligned box construction</c> (the arm), then <c>shape rotation</c>. Rotated to EAST the plate is x in [12/16,16/16] and the arm is x in [-4/16,12/16], which reaches back into the piston base as vanilla's arm does.</summary>
    private static readonly Aabb[] PistonHeadEast =
    [
        new(0.75, 0.0, 0.0, 1.0, 1.0, 1.0),
        new(-0.25, 0.375, 0.375, 0.75, 0.625, 0.625),
    ];

    /// <summary>The exact one-surviving-tick push from <c>float32(0.6) / 2 + 0.01</c>.</summary>
    private const double VanillaOneTickPush = 0.31000001192092896;

    /// <summary>The exact full two-tick push from <c>float32(0.6) / 2 + 0.5 + 0.01</c>.</summary>
    private const double VanillaFullPush = 0.81000001192092896;

    /// <summary>The half player width is computed in float across supported eras.</summary>
    private const double VanillaHalfWidth = 0.300000011920928955078125;

    /// <summary>Slack for double reassociation only, seven orders of magnitude tighter than the double-versus-float half-width difference.</summary>
    private const double ArithmeticTolerance = 1e-15;

    private static PlayerPhysics NewEngine(FixtureWorld world, Vec3d start)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(start, 0f, 0f);
        return engine;
    }

    /// <summary>A piston at x=-2 faces east with the player standing in the block the head extends into. Two block-entity ticks must move the player exactly 0.81 blocks.</summary>
    [Fact]
    public void ExtendingPistonHead_PushesPlayer_TheFullVanillaDistance()
    {
        var world = new FixtureWorld().Floor(-8, 8, -8, 8, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(-0.5, 64.0, 0.5));
        double startX = engine.State.Position.X;

        // The head occupies the block adjacent to the base in its facing direction.
        var piston = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: true, isSourcePiston: true, PistonHeadEast);

        engine.BeginPistonTick();
        piston.Tick(engine);
        double afterFirst = engine.State.Position.X - startX;

        engine.BeginPistonTick();
        piston.Tick(engine);
        double afterSecond = engine.State.Position.X - startX;

        Assert.Equal(VanillaOneTickPush, afterFirst, ArithmeticTolerance);
        Assert.Equal(VanillaFullPush, afterSecond, ArithmeticTolerance);

        // The entity retires after two ticks and pushes nothing more once progress reaches 1.
        engine.BeginPistonTick();
        piston.Tick(engine);
        Assert.True(piston.Removed);
        Assert.Equal(VanillaFullPush, engine.State.Position.X - startX, ArithmeticTolerance);
    }

    /// <summary>The first tick moves 0.310 blocks, a partial push rather than the complete two-tick contract.</summary>
    [Fact]
    public void ExtendingPistonHead_FirstTickAlone_IsTheSweepsFailureValue()
    {
        var world = new FixtureWorld().Floor(-8, 8, -8, 8, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(-0.5, 64.0, 0.5));

        var piston = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: true, isSourcePiston: true, PistonHeadEast);
        engine.BeginPistonTick();
        piston.Tick(engine);

        Assert.Equal(VanillaOneTickPush, engine.State.Position.X + 0.5, ArithmeticTolerance);
        Assert.Equal(0.5f, piston.Progress);
    }

    /// <summary>The same fixture mirrored onto all four horizontal facings must push the same distance along the piston's own axis and nothing at all along the others.</summary>
    [Theory]
    [InlineData(Direction.East)]
    [InlineData(Direction.West)]
    [InlineData(Direction.North)]
    [InlineData(Direction.South)]
    public void PushIsAlongThePistonAxis_ForEveryHorizontalFacing(Direction facing)
    {
        var world = new FixtureWorld().Floor(-8, 8, -8, 8, 63, BlockKind.Stone);

        // Piston base at the origin; the head occupies the block one step along the facing, and the player stands in that block.
        var head = new BlockPos(facing.StepX(), 64, facing.StepZ());
        var start = new Vec3d(head.X + 0.5, 64.0, head.Z + 0.5);
        var engine = NewEngine(world, start);

        var piston = new MovingPiston(
            head, facing, extending: true, isSourcePiston: true, RotateFromEast(PistonHeadEast, facing));

        engine.BeginPistonTick();
        piston.Tick(engine);
        engine.BeginPistonTick();
        piston.Tick(engine);

        Vec3d moved = engine.State.Position.Subtract(start);
        double along = (moved.X * facing.StepX()) + (moved.Z * facing.StepZ());
        double across = (moved.X * facing.StepZ()) - (moved.Z * facing.StepX());

        Assert.Equal(VanillaFullPush, along, ArithmeticTolerance);
        Assert.Equal(0.0, across, 12);
        Assert.Equal(0.0, moved.Y, 12);
    }

    /// <summary>A player standing clear of the head's sweep is not moved. Without this the pair above would pass on a model that pushes unconditionally.</summary>
    [Fact]
    public void PlayerOutsideTheMovementArea_IsNotPushed()
    {
        var world = new FixtureWorld().Floor(-8, 8, -8, 8, 63, BlockKind.Stone);

        // Three blocks past the head: the two-tick sweep reaches x = 1.5 at most.
        var engine = NewEngine(world, new Vec3d(3.5, 64.0, 0.5));
        var piston = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: true, isSourcePiston: true, PistonHeadEast);

        engine.BeginPistonTick();
        piston.Tick(engine);
        engine.BeginPistonTick();
        piston.Tick(engine);

        Assert.Equal(3.5, engine.State.Position.X, 12);
    }

    /// <summary>A player two blocks away on the perpendicular axis is untouched, which the movement-area geometry only gets right if the non-swept axes keep the shape's own extent.</summary>
    [Fact]
    public void PlayerBesideTheHead_IsNotPushed()
    {
        var world = new FixtureWorld().Floor(-8, 8, -8, 8, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(-0.5, 64.0, 2.5));
        var piston = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: true, isSourcePiston: true, PistonHeadEast);

        engine.BeginPistonTick();
        piston.Tick(engine);
        engine.BeginPistonTick();
        piston.Tick(engine);

        Assert.Equal(-0.5, engine.State.Position.X, 12);
        Assert.Equal(2.5, engine.State.Position.Z, 12);
    }

    /// <summary>The push still collides. A wall one block past the player stops it short, which is vanilla: Piston displacement uses collision-aware movement rather than teleporting the body.</summary>
    [Fact]
    public void PushCollidesWithTheWorld()
    {
        // The free push ends at x = -0.5 + 0.81 = 0.31, so the wall has to be the block at x=0 for the player's east face to reach it at all.
        var world = new FixtureWorld()
            .Floor(-8, 8, -8, 8, 63, BlockKind.Stone)
            .Fill(0, 64, 0, 0, 66, 0, BlockKind.Stone);

        var engine = NewEngine(world, new Vec3d(-0.5, 64.0, 0.5));
        var piston = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: true, isSourcePiston: true, PistonHeadEast);

        engine.BeginPistonTick();
        piston.Tick(engine);
        engine.BeginPistonTick();
        piston.Tick(engine);

        // The wall's west face is x=0, so the player's east face (x + half width) stops there.
        Assert.True(engine.State.Position.X < -0.5 + VanillaFullPush,
            $"clipped through the wall, x={engine.State.Position.X}");
        // The stop is the wall face minus the game's float half width, not the double one.
        Assert.Equal(0.0 - VanillaHalfWidth, engine.State.Position.X, ArithmeticTolerance);
    }

    /// <summary>Piston displacement is clamped per entity and axis to 0.51 per game tick. Two pistons facing the same way in one tick must not stack past it.</summary>
    [Fact]
    public void TwoPistonsInOneTick_AreClampedToTickMovement()
    {
        var world = new FixtureWorld().Floor(-8, 8, -8, 8, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(-0.5, 64.0, 0.5));
        double startX = engine.State.Position.X;

        var a = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: true, isSourcePiston: true, PistonHeadEast);
        var b = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: true, isSourcePiston: true, PistonHeadEast);

        engine.BeginPistonTick();
        a.Tick(engine);
        b.Tick(engine);

        double moved = engine.State.Position.X - startX;
        Assert.True(moved <= MovingPiston.TickMovement + 1e-9, $"exceeded the 0.51 tick clamp: {moved}");
        Assert.True(moved > VanillaOneTickPush - ArithmeticTolerance, $"the first push was lost: {moved}");
    }

    /// <summary>The engine keeps its own accumulator per tick, so the same two pistons across two separate ticks are NOT clamped against each other. Without <see cref="PlayerPhysics.BeginPistonTick"/> being called per tick the clamp would freeze the player after 0.51 blocks forever.</summary>
    [Fact]
    public void TheTickClampResetsBetweenTicks()
    {
        var world = new FixtureWorld().Floor(-8, 8, -8, 8, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(-0.5, 64.0, 0.5));
        double startX = engine.State.Position.X;

        var piston = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: true, isSourcePiston: true, PistonHeadEast);

        engine.BeginPistonTick();
        piston.Tick(engine);
        engine.BeginPistonTick();
        piston.Tick(engine);

        Assert.True(engine.State.Position.X - startX > MovingPiston.TickMovement,
            "the second tick was swallowed by the first tick's clamp");
    }

    /// <summary>A retracting source piston pulls its head back and pushes nobody standing where the head was: Movement in the reversed direction is negative there. This is the arm the sweep never exercised, and it is where the <c>extending</c> sign flip would show.</summary>
    [Fact]
    public void RetractingPiston_DoesNotPushAPlayerStandingInFrontOfIt()
    {
        var world = new FixtureWorld().Floor(-8, 8, -8, 8, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(1.5, 64.0, 0.5));

        // triggerEvent() puts the retracting entity at the piston base itself.
        var piston = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: false, isSourcePiston: true, PistonHeadEast);

        engine.BeginPistonTick();
        piston.Tick(engine);
        engine.BeginPistonTick();
        piston.Tick(engine);

        Assert.Equal(1.5, engine.State.Position.X, 9);
    }

    /// <summary>The retracting head's own geometry and extension-progress sign.</summary>
    /// <remarks>With the entity at the base (-1,64,0), <c>direction=EAST</c>, <c>extending=false</c>, so Extension progress is one, which puts the local plate box [0.75,1.0] at world [0.75,1.0], one whole block out along the facing, which is where an extended piston's head really is. Movement is west, so the plate sweeps west through [0.25,0.75]. A player at x=0.9 has box [0.6,1.5]... in X [0.6,1.2], which overlaps it, and <c>getMovement(area, WEST, box) = box.maxX - area.minX = 1.2 - 0.25 = 0.95</c>. That is capped by <c>min(0.95, delta=0.5) + PUSH_OFFSET</c> = <b>0.51</b>, west, landing at x = 0.39. x=0.9 is chosen so the result does NOT overlap the base block [-1,0] and Base-overlap correction stays out of it; the test below exercises that path.</remarks>
    [Fact]
    public void RetractingPiston_HeadStartsOneBlockOutAlongTheFacing()
    {
        var world = new FixtureWorld().Floor(-8, 8, -8, 8, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(0.9, 64.0, 0.5));
        var piston = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: false, isSourcePiston: true, PistonHeadEast);

        engine.BeginPistonTick();
        piston.Tick(engine);

        Assert.Equal(0.39, engine.State.Position.X, 9);
    }

    /// <summary>Base-overlap correction runs only for a retracting source piston and turns a net pull into a net shove when the pull would leave the entity inside the piston base.</summary>
    /// <remarks>
    /// <para>For a player at x=0.2, the box has X [0.2-f, 0.2+f] with f vanilla's FLOAT half width. Stage one: plate area [0.25,0.75] overlaps it, <c>getMovement = box.maxX - 0.25</c>, <c>min(that, 0.5) + 0.01</c> WEST, and the box moves with it. In stage two, the box now overlaps the base cube [-1,0], <c>back = EAST</c>, <c>whole = block.maxX - box.minX + 0.01</c> and the clipped form is the same because the intersection shares that <c>minX</c>, so the guard <c>|whole - clipped| &lt; 0.01</c> passes and the entity is moved <c>min(whole, 0.5) + 0.01</c> EAST. Net: a retracting piston moves this player FORWARD, which is exactly the behaviour a naive port would get backwards.</para>
    /// <para>The rounded arithmetic is 0.5 - 0.25 = 0.25, +0.01 = 0.26 west to x = -0.06, then 0 + 0.36 + 0.01 = 0.37, +0.01 = 0.38 east, net <b>0.32</b>. With vanilla's real float half width the whole chain shifts by exactly one half-width delta and the answer is 0.320000011920928972841693394002504646778106689453125. The assertion retains enough precision to distinguish that result from double halving.</para>
    /// </remarks>
    [Fact]
    public void RetractingSourcePiston_ShovesAnEntityOutOfItsOwnBase()
    {
        var world = new FixtureWorld().Floor(-8, 8, -8, 8, 63, BlockKind.Stone);
        var engine = NewEngine(world, new Vec3d(0.2, 64.0, 0.5));
        var piston = new MovingPiston(
            new BlockPos(-1, 64, 0), Direction.East, extending: false, isSourcePiston: true, PistonHeadEast);

        engine.BeginPistonTick();
        piston.Tick(engine);

        Assert.Equal(0.320000011920928972841693394002504646778106689453125, engine.State.Position.X, ArithmeticTolerance);
        Assert.NotEqual(0.32, engine.State.Position.X, 9);
    }

    /// <summary>Rotates the EAST head boxes onto another horizontal facing, about the block centre.</summary>
    private static Aabb[] RotateFromEast(Aabb[] boxes, Direction facing)
    {
        if (facing == Direction.East)
            return boxes;

        var result = new Aabb[boxes.Length];
        for (int i = 0; i < boxes.Length; i++)
        {
            Aabb b = boxes[i];
            (double x1, double z1) = RotatePoint(b.MinX, b.MinZ, facing);
            (double x2, double z2) = RotatePoint(b.MaxX, b.MaxZ, facing);
            result[i] = new Aabb(x1, b.MinY, z1, x2, b.MaxY, z2);
        }

        return result;
    }

    private static (double X, double Z) RotatePoint(double x, double z, Direction facing) => facing switch
    {
        // EAST (+x) onto WEST (-x): mirror both horizontal axes about the block centre.
        Direction.West => (1.0 - x, 1.0 - z),

        // EAST onto SOUTH (+z): quarter turn.
        Direction.South => (1.0 - z, x),

        // EAST onto NORTH (-z): the other quarter turn.
        Direction.North => (z, 1.0 - x),
        _ => (x, z),
    };
}
