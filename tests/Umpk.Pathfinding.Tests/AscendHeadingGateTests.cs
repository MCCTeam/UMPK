using Umpk.Geometry;
using Umpk.Pathfinding.Execution.Templates;
using Xunit;

namespace Umpk.Pathfinding.Tests;

public sealed class AscendHeadingGateTests
{
    /// <summary>The gate AscendTemplate applies, in degrees.</summary>
    private const double GateDegrees = 8.0;

    [Fact]
    public void OffCentreStart_MakesPointYawAndCardinalYawDivergePastTheGate()
    {
        // The live case: standing at x=147.7, z=380.7, stepping east onto block (148, 380), whose centre is (148.5, 380.5).
        float pointYaw = SegmentGeometry.CalculateYaw(148.5 - 147.7, 380.5 - 380.7);
        double penalty = SegmentGeometry.HeadingPenaltyDegrees(pointYaw, 1, 0);

        Assert.True(
            penalty > GateDegrees,
            $"expected the two yaws to diverge past the gate, but the penalty was {penalty:0.0} degrees");
    }

    /// <summary>Turning toward the segment's own cardinal heading makes the gate reachable by construction: the value the template steers to and the value it is judged against are then the same number.</summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(0, -1)]
    public void SteeringToTheCardinalHeading_ClosesTheGate(int headingX, int headingZ)
    {
        float cardinalYaw = SegmentGeometry.CalculateYaw(headingX, headingZ);

        Assert.Equal(0.0, SegmentGeometry.HeadingPenaltyDegrees(cardinalYaw, headingX, headingZ), 3);
    }

    /// <summary>A centred start is the case that always worked, and it must keep working.</summary>
    [Fact]
    public void CentredStart_WasAlwaysInsideTheGate()
    {
        float pointYaw = SegmentGeometry.CalculateYaw(148.5 - 147.5, 380.5 - 380.5);

        Assert.True(SegmentGeometry.HeadingPenaltyDegrees(pointYaw, 1, 0) <= GateDegrees);
    }
}
