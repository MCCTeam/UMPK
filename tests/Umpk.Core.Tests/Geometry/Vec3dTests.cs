using Umpk.Geometry;
using Xunit;

namespace Umpk.Tests.Geometry;

public class Vec3dTests
{
    [Fact]
    public void ArithmeticOperations_ProduceKnownValues()
    {
        var a = new Vec3d(1, 2, 3);
        var b = new Vec3d(4, -5, 6);

        Assert.Equal(new Vec3d(5, -3, 9), a.Add(b));
        Assert.Equal(new Vec3d(5, -3, 9), a + b);
        Assert.Equal(new Vec3d(-3, 7, -3), a.Subtract(b));
        Assert.Equal(new Vec3d(-3, 7, -3), a - b);
        Assert.Equal(new Vec3d(2, 4, 6), a.Scale(2));
        Assert.Equal(new Vec3d(2, 4, 6), a * 2);
        Assert.Equal(new Vec3d(4, -10, 18), a.Multiply(b));
        Assert.Equal(new Vec3d(4, -10, 18), a.Multiply(4, -5, 6));
        Assert.Equal(new Vec3d(-1, -2, -3), -a);
        Assert.Equal(new Vec3d(2, 4, 6), a.Add(1, 2, 3));
        Assert.Equal(new Vec3d(0, 0, 0), a.Subtract(1, 2, 3));
    }

    [Fact]
    public void Length_MatchesKnownValues()
    {
        var v = new Vec3d(3, 4, 0);
        Assert.Equal(25, v.LengthSqr());
        Assert.Equal(5, v.Length());
        Assert.Equal(9, v.HorizontalDistanceSqr());
    }

    [Fact]
    public void Normalize_UnitizesOrCollapsesToZero()
    {
        var v = new Vec3d(0, 3, 4).Normalize();
        Assert.Equal(new Vec3d(0, 0.6, 0.8), v);
        Assert.Equal(Vec3d.Zero, new Vec3d(1e-8, 0, 0).Normalize());
    }

    /// <summary>Vanilla's collapse threshold, and it is not a rounding detail: the only caller that can land inside the disputed window is <c>PlayerPhysics.ApplyCurrent</c>'s minimum-push floor, which takes the normalized impulse and scales it back up to 0.0045 - above the 0.003 velocity-zeroing threshold. Getting this wrong is a MOVING bot where vanilla has a stationary one.</summary>
    /// <remarks>The threshold is <c>1.0E-4</c> from 1.14.4 through 1.21.1 and <c>1.0E-5F</c> from 1.21.2 onward, so the modern value is exact and the legacy eras are still under-collapsed by a factor of ten. UMPK's own <c>1.0E-7</c> was wrong on BOTH eras by three and two orders of magnitude respectively; making it era-aware would mean threading a protocol into a <c>Umpk.Core</c> geometry primitive, which is a dataset-axis decision and not this change.</remarks>
    [Theory]
    [InlineData(1e-8, true)]
    [InlineData(1e-6, true)]
    [InlineData(9.9e-6, true)]
    [InlineData(1.1e-5, false)]
    [InlineData(1e-4, false)]
    public void Normalize_CollapsesBelowOneEMinusFive(double length, bool collapses)
    {
        Vec3d normalized = new Vec3d(length, 0, 0).Normalize();
        Assert.Equal(collapses, normalized == Vec3d.Zero);
    }

    [Fact]
    public void GetAndWith_UseAxisIndices()
    {
        var v = new Vec3d(1, 2, 3);
        Assert.Equal(1, v.Get(0));
        Assert.Equal(2, v.Get(1));
        Assert.Equal(3, v.Get(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => v.Get(3));

        Assert.Equal(new Vec3d(9, 2, 3), v.With(0, 9));
        Assert.Equal(new Vec3d(1, 9, 3), v.With(1, 9));
        Assert.Equal(new Vec3d(1, 2, 9), v.With(2, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => v.With(-1, 0));
    }

    [Fact]
    public void Equality_IsExact()
    {
        Assert.True(new Vec3d(1, 2, 3) == new Vec3d(1, 2, 3));
        Assert.True(new Vec3d(1, 2, 3) != new Vec3d(1, 2, 3.0000001));
        Assert.Equal(new Vec3d(1, 2, 3).GetHashCode(), new Vec3d(1, 2, 3).GetHashCode());
    }
}
