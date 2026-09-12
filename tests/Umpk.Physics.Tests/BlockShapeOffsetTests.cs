using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Tests the game's per-position horizontal shape offset.</summary>
/// <remarks>
/// <para>The position-dependent offset is calculated as follows:</para>
/// <code>
/// long seed = position-seed calculation(pos.getX(), 0, pos.getZ()); float maxHorizontalOffset = block.getMaxHorizontalOffset(); double x = clamping(((double)((float)(seed &amp; 15L) / 15.0F) - 0.5) * 0.5, -maxHorizontalOffset, maxHorizontalOffset); double z = clamping(((double)((float)(seed &gt;&gt; 8 &amp; 15L) / 15.0F) - 0.5) * 0.5, -maxHorizontalOffset, maxHorizontalOffset);
/// </code>
/// <para>using this seed calculation:</para>
/// <code>
/// long seed = (long)(x * 3129871) ^ (long)z * 116129781L ^ (long)y; seed = seed * seed * 42317861L + seed * 11L; return seed &gt;&gt; 16;
/// </code>
/// <para>Two details of that arithmetic were checked rather than assumed, and they came out differently:</para>
/// <list type="bullet">
/// <item><description>
/// The <b>float</b> division (<c>(float)(seed &amp; 15L) / 15.0F</c>, widened to double afterwards) IS observable: it moves every non-clamped offset by about 1.2e-8 against a double divide. Pinned by <c>TheDivisionIsDoneInFloat</c> with exact values.
/// </description></item>
/// <item><description>
/// The <b>32-bit wrap</b> in <c>(long)(x * 3129871)</c> is <b>NOT</b> observable here, and there is deliberately no test for it. A wrapped and an unwrapped product differ only above bit 32; the square that follows propagates that difference no lower than bit 33; and the two sampled nibbles are bits 16-19 and 24-27 of the pre-shift value. Swept every <c>x</c> from 600 to 40,000,000 whose int product overflows, against both widths: <b>zero</b> disagreements. A test asserting the wrap would therefore pass whichever width the port used, which is not a test. The production code still wraps, because matching vanilla's spelling costs nothing and the property could stop holding if the sampled bits ever move.
/// </description></item>
/// </list>
/// <para>The cases use two positions with distinct outcomes: <c>(-359, 407)</c> reaches the low clamp on both axes, while <c>(-361, 412)</c> differs by 0.175 on Z.</para>
/// </remarks>
public sealed class BlockShapeOffsetTests
{
    /// <summary>Pointed dripstone moves by at most its base-shape inset of 2/16.</summary>
    private const double DripstoneMaxOffset = 0.125;

    /// <summary>The default maximum offset used by bamboo.</summary>
    private const double DefaultMaxOffset = 0.25;

    private static readonly FixtureBlockData Data = new();

    private static BlockState State(BlockKind kind) => new(Data, (int)kind);

    /// <summary>Both components come from the position-dependent expression. The Z component distinguishes this position from the low-clamp control by 0.175 blocks.</summary>
    [Fact]
    public void Dripstone_AtTheStallBlock_MatchesVanillaOffset()
    {
        Vec3d offset = BlockShapeOffset.For(State(BlockKind.PointedDripstone), new BlockPos(-361, 5, 412));

        Assert.Equal(-0.125, offset.X);
        Assert.Equal(0.0, offset.Y);

        // Not 0.05: the game divides in float and widens, so the exact double is 0.050000011920928955. Pinned exactly, because a port that divides in double produces 0.05 and would silently pass an approximate assertion while diverging from the server by 1.2e-8 on every offset block.
        Assert.Equal(0.050000011920928955, offset.Z);
    }

    /// <summary>At this control position, both hash components land on the low clamp.</summary>
    [Fact]
    public void Dripstone_AtTheAgreeingBlock_IsTheCornerUmpkBaked()
    {
        Vec3d offset = BlockShapeOffset.For(State(BlockKind.PointedDripstone), new BlockPos(-359, 2, 407));

        Assert.Equal(-0.125, offset.X, 9);
        Assert.Equal(-0.125, offset.Z, 9);
    }

    /// <summary>The offset must not depend on Y: vanilla passes a literal <c>0</c> as the seed's Y, so a whole stalagmite column leans the same way. Baking it per state would have hidden this.</summary>
    [Fact]
    public void Offset_IsIndependentOfY()
    {
        BlockState state = State(BlockKind.PointedDripstone);

        Vec3d low = BlockShapeOffset.For(state, new BlockPos(-361, 5, 412));
        Vec3d high = BlockShapeOffset.For(state, new BlockPos(-361, 91, 412));

        Assert.Equal(low.X, high.X, 12);
        Assert.Equal(low.Z, high.Z, 12);
    }

    /// <summary>Every offset is inside the block's own clamp, on a spread of positions and both signs.</summary>
    [Theory]
    [InlineData(BlockKind.PointedDripstone, DripstoneMaxOffset)]
    [InlineData(BlockKind.Bamboo, DefaultMaxOffset)]
    public void Offset_IsAlwaysInsideTheBlocksClamp(BlockKind kind, double maxOffset)
    {
        BlockState state = State(kind);

        for (int x = -400; x <= 400; x += 7)
            for (int z = -400; z <= 400; z += 11)
            {
                Vec3d offset = BlockShapeOffset.For(state, new BlockPos(x, 4, z));

                Assert.InRange(offset.X, -maxOffset, maxOffset);
                Assert.InRange(offset.Z, -maxOffset, maxOffset);
                Assert.Equal(0.0, offset.Y, 12);
            }

    }

    /// <summary>A block with no offset function gets nothing. This is the guard that stops the rule leaking onto ordinary terrain: if it fired on stone, every floor in the game would move.</summary>
    [Theory]
    [InlineData(BlockKind.Stone)]
    [InlineData(BlockKind.Air)]
    [InlineData(BlockKind.Scaffolding)]
    [InlineData(BlockKind.Slab)]
    public void BlocksWithoutAnOffsetFunction_GetZero(BlockKind kind)
    {
        Vec3d offset = BlockShapeOffset.For(State(kind), new BlockPos(-361, 5, 412));

        Assert.Equal(Vec3d.Zero, offset);
    }

    /// <summary>The clamp is REACHED on both sides, so the two-sided <c>clamping</c> is doing work. Without this an implementation that clamped only the low side, or that skipped the clamp entirely (raw range is +/-0.25, which for dripstone is twice the legal travel), would still pass every test above that samples an interior value.</summary>
    [Fact]
    public void TheClampIsReachedOnBothSides()
    {
        BlockState state = State(BlockKind.PointedDripstone);
        bool sawLow = false;
        bool sawHigh = false;

        for (int x = -200; x <= 200 && !(sawLow && sawHigh); x++)
        {
            Vec3d offset = BlockShapeOffset.For(state, new BlockPos(x, 4, 0));
            sawLow |= offset.X == -DripstoneMaxOffset;
            sawHigh |= offset.X == DripstoneMaxOffset;
        }

        Assert.True(sawLow, "the low clamp was never reached");
        Assert.True(sawHigh, "the high clamp was never reached");
    }

    /// <summary>The float division, isolated. <c>(float)(seed &amp; 15L) / 15.0F</c> is a FLOAT divide widened to double; doing it in double instead shifts every non-clamped offset by about 1.2e-8. That is far too small to matter on its own, but it is free to get right and it is the difference between "matches the server exactly" and "matches the server nearly".</summary>
    [Fact]
    public void TheDivisionIsDoneInFloat()
    {
        Vec3d offset = BlockShapeOffset.For(State(BlockKind.Bamboo), new BlockPos(-361, 5, 412));

        Assert.Equal(-0.21666666492819786, offset.X);
        Assert.Equal(0.050000011920928955, offset.Z);
    }
}
