using Umpk.Geometry;
using Xunit;

namespace Umpk.Tests.Geometry;

/// <summary><see cref="Aabb.DistanceSqrTo"/>: per axis, the gap to the nearer face, or zero when the point already falls inside the box on that axis. Interaction reach uses this measure, never the distance to the box's centre.</summary>
public sealed class AabbDistanceTests
{
    [Fact]
    public void DistanceSqrTo_PointInsideTheBox_IsZero()
    {
        Aabb box = Aabb.BlockAt(0, 0, 0);

        Assert.Equal(0.0, box.DistanceSqrTo(new Vec3d(0.5, 0.5, 0.5)));
    }

    [Theory]
    [InlineData(2.0, 0.5, 0.5, 1.0)] // 1 block past the max-X face (2 - 1 = 1)
    [InlineData(-1.0, 0.5, 0.5, 1.0)] // 1 block short of the min-X face (0 - -1 = 1)
    [InlineData(0.5, 3.0, 0.5, 4.0)] // 2 blocks above the max-Y face, squared (2^2 = 4)
    public void DistanceSqrTo_OneAxisOutside_IsTheGapToTheNearerFaceSquared(
        double x, double y, double z, double expectedSqr)
    {
        Aabb box = Aabb.BlockAt(0, 0, 0);

        Assert.Equal(expectedSqr, box.DistanceSqrTo(new Vec3d(x, y, z)), 12);
    }

    [Fact]
    public void DistanceSqrTo_TwoAxesOutside_SumsBothGapsSquared()
    {
        Aabb box = Aabb.BlockAt(0, 0, 0);

        // 2 past the max-X face (gap 1) and 3 past the max-Z face (gap 2): 1^2 + 2^2 = 5, Y stays inside.
        Assert.Equal(5.0, box.DistanceSqrTo(new Vec3d(2, 0.5, 3)), 12);
    }

    [Fact]
    public void DistanceSqrTo_AHandComputedLiteralCase()
    {
        // Aabb.BlockAt(10, 64, -3) is [10,64,-3] -> [11,65,-2].
        Aabb box = Aabb.BlockAt(10, 64, -3);
        var point = new Vec3d(12.5, 63.0, -2.5);

        // gapX = 12.5 - 11 = 1.5 (past the max-X face); gapY = 64 - 63.0 = 1.0 (short of the min-Y face);
        // gapZ = 0 (-2.5 already falls inside [-3, -2]).
        double expected = (1.5 * 1.5) + (1.0 * 1.0) + 0.0;

        Assert.Equal(expected, box.DistanceSqrTo(point), 12);
    }
}
