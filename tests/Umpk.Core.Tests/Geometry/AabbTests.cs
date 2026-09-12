using Umpk.Geometry;
using Xunit;

namespace Umpk.Tests.Geometry;

public class AabbTests
{
    // Player dimensions used by the compatibility fixtures.
    private const double PlayerWidth = 0.6;
    private const double PlayerHeight = 1.8;

    /// <summary>Half width uses float32(0.6)/2 widened to double across the supported range. Halving the double 0.6 instead gives 0.29999999999999998889..., which is 1.1920929e-8 too narrow per side and was visible on the wire: a vanilla server read 3.8100000119209287 where the client read 3.81.</summary>
    private const double VanillaHalfWidth = 0.300000011920928955078125;

    /// <summary>The standing height is float32(1.8) widened to double, 4.76837e-8 shorter than double 1.8.</summary>
    private const double VanillaStandingHeight = 1.7999999523162841796875;

    [Fact]
    public void Constructor_NormalizesMinMax()
    {
        var box = new Aabb(2, 3, 4, -1, -2, -3);
        Assert.Equal(-1, box.MinX);
        Assert.Equal(-2, box.MinY);
        Assert.Equal(-3, box.MinZ);
        Assert.Equal(2, box.MaxX);
        Assert.Equal(3, box.MaxY);
        Assert.Equal(4, box.MaxZ);
    }

    /// <summary>The player box is asserted exactly, with no tolerance, because the tidy decimals are wrong. Constructing it from single-precision <c>width=0.6F, height=1.8F</c> values gives minX/minZ 0.199999988079071044921875, maxX/maxZ 0.800000011920928955078125, maxY 65.7999999523162841796875, xSize/zSize 0.60000002384185791015625 and ySize 1.7999999523162841796875. Rounded decimal assertions would miss the required float-width behavior.</summary>
    [Fact]
    public void OfSize_BuildsPlayerBox()
    {
        var box = Aabb.OfSize(0.5, 64, 0.5, PlayerWidth, PlayerHeight);
        Assert.Equal(0.5 - VanillaHalfWidth, box.MinX);
        Assert.Equal(64, box.MinY);
        Assert.Equal(0.5 - VanillaHalfWidth, box.MinZ);
        Assert.Equal(0.5 + VanillaHalfWidth, box.MaxX);
        Assert.Equal(64 + VanillaStandingHeight, box.MaxY);
        Assert.Equal(0.5 + VanillaHalfWidth, box.MaxZ);
        Assert.Equal(VanillaHalfWidth * 2.0, box.XSize);
        Assert.Equal(VanillaStandingHeight, box.YSize);
        Assert.Equal(VanillaHalfWidth * 2.0, box.ZSize);
    }

    /// <summary>The discriminator for the above: halving in double instead of float must NOT reproduce the box. Without this a future "simplification" back to <c>width / 2.0</c> would go unnoticed by every tolerance-based assertion in the suite, since the whole error is 1.19e-8.</summary>
    [Fact]
    public void OfSize_DoesNotHalveInDouble()
    {
        var box = Aabb.OfSize(0.5, 64, 0.5, PlayerWidth, PlayerHeight);

        Assert.NotEqual(0.5 - (PlayerWidth / 2.0), box.MinX);
        Assert.NotEqual(0.5 + (PlayerWidth / 2.0), box.MaxX);
        Assert.NotEqual(64 + PlayerHeight, box.MaxY);

        // And the sign of the error is fixed: vanilla's box is WIDER and SHORTER than the naive one.
        Assert.True(box.MaxX > 0.5 + (PlayerWidth / 2.0));
        Assert.True(box.MinX < 0.5 - (PlayerWidth / 2.0));
        Assert.True(box.MaxY < 64 + PlayerHeight);
        Assert.InRange(box.MaxX - (0.5 + (PlayerWidth / 2.0)), 1.19E-08, 1.20E-08);
    }

    [Fact]
    public void BlockAt_IsUnitCube()
    {
        var box = Aabb.BlockAt(-2, 5, 7);
        Assert.Equal(new Aabb(-2, 5, 7, -1, 6, 8), box);
    }

    [Fact]
    public void MinMax_ByAxisIndex()
    {
        var box = new Aabb(1, 2, 3, 4, 5, 6);
        Assert.Equal(1, box.Min(0));
        Assert.Equal(2, box.Min(1));
        Assert.Equal(3, box.Min(2));
        Assert.Equal(4, box.Max(0));
        Assert.Equal(5, box.Max(1));
        Assert.Equal(6, box.Max(2));
    }

    [Fact]
    public void ExpandTowards_GrowsOnlyInMovementDirection()
    {
        var box = new Aabb(0, 0, 0, 1, 1, 1);
        var expanded = box.ExpandTowards(0.5, -0.25, 0);
        Assert.Equal(new Aabb(0, -0.25, 0, 1.5, 1, 1), expanded);
        Assert.Equal(expanded, box.ExpandTowards(new Vec3d(0.5, -0.25, 0)));
    }

    [Fact]
    public void InflateDeflateMove_ProduceKnownBoxes()
    {
        var box = new Aabb(0, 0, 0, 1, 1, 1);
        Assert.Equal(new Aabb(-1, -1, -1, 2, 2, 2), box.Inflate(1));
        Assert.Equal(new Aabb(-1, 0, 0, 2, 1, 1), box.Inflate(1, 0, 0));
        Assert.Equal(new Aabb(0.25, 0.25, 0.25, 0.75, 0.75, 0.75), box.Deflate(0.25, 0.25, 0.25));
        Assert.Equal(new Aabb(1, 2, 3, 2, 3, 4), box.Move(1, 2, 3));
        Assert.Equal(new Aabb(1, 2, 3, 2, 3, 4), box.Move(new Vec3d(1, 2, 3)));
    }

    [Fact]
    public void Intersects_IsStrict_TouchingFacesDoNotIntersect()
    {
        var a = new Aabb(0, 0, 0, 1, 1, 1);
        Assert.False(a.Intersects(new Aabb(1, 0, 0, 2, 1, 1)));
        Assert.True(a.Intersects(new Aabb(0.999, 0, 0, 2, 1, 1)));
        Assert.True(a.Intersects(0.5, 0.5, 0.5, 2, 2, 2));
        Assert.False(a.Intersects(1.0, 1.0, 1.0, 2, 2, 2));
    }

    [Fact]
    public void Contains_IsMinInclusiveMaxExclusive()
    {
        var box = new Aabb(0, 0, 0, 1, 1, 1);
        Assert.True(box.Contains(0, 0, 0));
        Assert.True(box.Contains(0.999, 0.999, 0.999));
        Assert.False(box.Contains(1, 0.5, 0.5));
        Assert.False(box.Contains(0.5, 1, 0.5));
        Assert.False(box.Contains(0.5, 0.5, 1));
    }

    [Fact]
    public void CollideX_ClampsMovementToGap()
    {
        // Player at x-center 0.5, block occupying x 1..2. The player box is NOT 0.2..0.8: vanilla's float half width puts it at 0.199999988079071044921875..0.800000011920928955078125.
        var player = Aabb.OfSize(0.5, 0, 0.5, PlayerWidth, PlayerHeight);
        var block = Aabb.BlockAt(1, 0, 0);

        // Moving +X by 0.5: vanilla X-axis collision resolution returns other.minX - this.maxX, so the gap is 1.0 - 0.800000011920928955078125 = 0.199999988079071044921875 exactly, not 0.2.
        Assert.Equal(1.0 - (0.5 + VanillaHalfWidth), player.CollideX(block, 0.5));
        Assert.Equal(0.199999988079071044921875, player.CollideX(block, 0.5));
        // Movement smaller than the gap is unchanged.
        Assert.Equal(0.1, player.CollideX(block, 0.1), 12);
        // Moving away is unchanged.
        Assert.Equal(-0.5, player.CollideX(block, -0.5), 12);
    }

    [Fact]
    public void CollideX_IgnoresLaterallyDisjointBoxes()
    {
        var player = Aabb.OfSize(0.5, 0, 0.5, PlayerWidth, PlayerHeight);
        // Block fully above the player: no Y overlap, so X movement is unaffected.
        var above = Aabb.BlockAt(1, 2, 0);
        Assert.Equal(0.5, player.CollideX(above, 0.5), 12);
        // Touching Y ranges (block top == player bottom) also do not clip: vanilla uses <=.
        var below = Aabb.BlockAt(1, -1, 0);
        Assert.Equal(0.5, player.CollideX(below, 0.5), 12);
    }

    [Fact]
    public void CollideY_FallingLandsOnBlock()
    {
        // Player feet at y=1.5 above a block occupying y 0..1.
        var player = Aabb.OfSize(0.5, 1.5, 0.5, PlayerWidth, PlayerHeight);
        var ground = Aabb.BlockAt(0, 0, 0);
        // Falling -1.0: clamped to the 0.5 gap.
        Assert.Equal(-0.5, player.CollideY(ground, -1.0), 12);
        // Jumping up is unaffected.
        Assert.Equal(0.4, player.CollideY(ground, 0.4), 12);
    }

    [Fact]
    public void CollideZ_ClampsMovementToGap()
    {
        var player = Aabb.OfSize(0.5, 0, 0.5, PlayerWidth, PlayerHeight);
        var block = Aabb.BlockAt(0, 0, -2);
        // Moving -Z by 1.0: gap is 0.2 - (-1.0) = 1.2? No: block z -2..-1, player min z 0.2. Gap = -1 - 0.2 = -1.2; movement -1.0 stays.
        Assert.Equal(-1.0, player.CollideZ(block, -1.0), 12);
        // A nearer block clips. Vanilla Z-axis collision resolution returns other.maxZ - this.minZ, so with the float half width that is 0.0 - 0.199999988079071044921875, not -0.2.
        var near = Aabb.BlockAt(0, 0, -1);
        Assert.Equal(0.0 - (0.5 - VanillaHalfWidth), player.CollideZ(near, -1.0));
        Assert.Equal(-0.199999988079071044921875, player.CollideZ(near, -1.0));
    }

    [Fact]
    public void Collide_DispatchesByAxis()
    {
        var player = Aabb.OfSize(0.5, 0, 0.5, PlayerWidth, PlayerHeight);
        var blockX = Aabb.BlockAt(1, 0, 0);
        Assert.Equal(player.CollideX(blockX, 0.5), player.Collide(0, blockX, 0.5));
        var ground = Aabb.BlockAt(0, -1, 0);
        Assert.Equal(player.CollideY(ground, -0.5), player.Collide(1, ground, -0.5));
        var blockZ = Aabb.BlockAt(0, 0, 1);
        Assert.Equal(player.CollideZ(blockZ, 0.5), player.Collide(2, blockZ, 0.5));
        // Unknown axis returns movement unchanged (ported semantics).
        Assert.Equal(0.5, player.Collide(9, blockX, 0.5));
    }

    /// <summary>The flush-contact case that the 1e-7 contact epsilon exists for. A player teleported to x=147.7 gets maxX = 147.7 + float32(0.6)/2 = 148.00000001192091758, which OVERLAPS the block column starting at x=148 by 1.1920917586394353e-08. With vanilla's epsilon the wall still clips forward movement, and the clip is a micro push-out of at most 1e-7. Without it the strict <c>other.MinX &gt;= MaxX</c> guard is false, nothing clips, the client walks into the wall, and a real server rejects and re-teleports every tick - which is exactly how the "flush-contact Ascend stall" freezes a bot.</summary>
    [Fact]
    public void CollideX_FlushOverlap_ClipsWithMicroPushOut()
    {
        var player = Aabb.OfSize(147.7, 79.0, 380.7, PlayerWidth, PlayerHeight);
        var wall = new Aabb(148, 79, 380, 149, 80, 381);

        // The overlap is real and tiny: the box reaches 1.19e-8 into the neighbouring column.
        Assert.True(player.MaxX > 148.0);
        Assert.InRange(player.MaxX - 148.0, 1.19E-08, 1.20E-08);

        double clipped = player.CollideX(wall, 0.0196);

        Assert.Equal(148.0 - player.MaxX, clipped);
        Assert.InRange(clipped, -1.0E-07, 0.0);
    }

    /// <summary>The other half of the epsilon: a 1.19e-8 cross-axis sliver must NOT connect the shape to the falling box. The player stands clear ABOVE the wall's top (feet y=80.5, wall top y=80) and is only "next to" it by the same 1.19e-8 X overlap. Vanilla's index window opens only when the cross-axis overlap EXCEEDS 1e-7, so the fall is unclipped. Without the epsilon the sliver counts as an overlap and the player phantom-lands on the wall's side face, 0.5 blocks above the surface it should slide past.</summary>
    [Fact]
    public void CollideY_CrossAxisSliverBelowEpsilon_DoesNotClip()
    {
        var player = Aabb.OfSize(147.7, 80.5, 380.7, PlayerWidth, PlayerHeight);
        var wall = new Aabb(148, 79, 380, 149, 80, 381);

        Assert.InRange(player.MaxX - 148.0, 1.19E-08, 1.20E-08);

        Assert.Equal(-0.6, player.CollideY(wall, -0.6));
    }

    /// <summary>Pin: an EXACT cross-axis touch (overlap 0) still does not clip, on either side, under the epsilon rules. <c>MinY + 1e-7 &gt;= other.MaxY</c> catches the block-below case and <c>other.MinY &gt; MaxY - 1e-7</c> the block-above case, matching the pre-epsilon <c>&lt;=</c>/<c>&gt;=</c> behaviour this suite has always asserted.</summary>
    [Fact]
    public void Collide_ExactCrossAxisTouch_DoesNotClip()
    {
        var box = new Aabb(0, 0, 0, 1, 1, 1);

        // Y touches from below (block top == box bottom) and from above (block bottom == box top).
        Assert.Equal(5.0, box.CollideX(new Aabb(2, -1, 0, 3, 0, 1), 5.0));
        Assert.Equal(5.0, box.CollideX(new Aabb(2, 1, 0, 3, 2, 1), 5.0));

        // Z touches, same two sides.
        Assert.Equal(5.0, box.CollideX(new Aabb(2, 0, -1, 3, 1, 0), 5.0));
        Assert.Equal(5.0, box.CollideX(new Aabb(2, 0, 1, 3, 1, 2), 5.0));

        // And the mirrored axes: X touches do not let a Y clip through.
        Assert.Equal(-5.0, box.CollideY(new Aabb(-1, -3, 0, 0, -2, 1), -5.0));
        Assert.Equal(-5.0, box.CollideY(new Aabb(1, -3, 0, 2, -2, 1), -5.0));
    }

    /// <summary>The negative-movement mirror: a face up to 1e-7 past the trailing edge still clips, and the clip is a micro push-OUT in the opposite direction. Feet embedded 5e-8 below a floor top must be lifted by +5e-8, not allowed to keep falling. The old strict <c>other.MaxY &lt;= MinY</c> guard is false here, so the floor was invisible and the movement passed through unclipped.</summary>
    [Fact]
    public void CollideY_FloorTopMarginallyAboveFeet_ClipsWithMicroPushUp()
    {
        const double Embed = 5.0E-08;
        var player = new Aabb(0.2, 64.0 - Embed, 0.2, 0.8, 65.8 - Embed, 0.8);
        var floor = new Aabb(0, 63, 0, 1, 64, 1);

        double clipped = player.CollideY(floor, -0.1);

        Assert.Equal(floor.MaxY - player.MinY, clipped);
        Assert.InRange(clipped, 0.0, 1.0E-07);
    }

    [Fact]
    public void Centers_AreKnownPoints()
    {
        var box = new Aabb(0, 0, 0, 2, 4, 6);
        Assert.Equal(new Vec3d(1, 2, 3), box.GetCenter());
        Assert.Equal(new Vec3d(1, 0, 3), box.GetBottomCenter());
    }
}
