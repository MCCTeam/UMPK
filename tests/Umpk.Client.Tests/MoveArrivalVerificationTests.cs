using Umpk.Client.Navigation;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// <see cref="Navigator.Judge"/> and <see cref="Navigator.IsSubBlockRequest"/> distinguish task completion from arrival. A verified move returns a structured <see cref="MoveResult"/>.
///
/// <para>
/// A protocol-47 water fixture measured this one-block shortfall:
/// <code>
/// [debug] Navigation started over 1 segments. [trace] Segment 0/1 completed (Traverse) in 9 ticks at (16.1050, 65.0000, -27.5000). Walking to 17.00 65.00 -28.00. server: testfor @a[x=17,y=65,z=-28,dx=0,dy=0,dz=0] -> (nothing) server: testfor @a[x=16,y=65,z=-28,dx=0,dy=0,dz=0] -> Found f2w
/// </code>
/// The result must not depend on the planner being right: the navigator still falls back to the closest block it can finish in when nothing can stand in the destination (a ladder rung, a solid block), and a fallback that is reported as an arrival is incorrect. <see cref="Navigator.MoveToVerifiedAsync"/> re-reads the player's own position and <see cref="Navigator.Judge"/> compares it with the destination.
/// </para>
/// </summary>
public sealed class MoveArrivalVerificationTests
{
    /// <summary>The exact numbers from the protocol-47 run above.</summary>
    [Fact]
    public void StoppingOneBlockShortIsNotReportedAsAnArrival()
    {
        MoveResult result = Navigator.Judge(new Vec3d(17, 65, -28), new Vec3d(16.14, 65.0, -27.50), subBlockRequest: false);

        Assert.False(result.Reached);
        Assert.Equal(MoveOutcome.StoppedShort, result.Outcome);
        Assert.Equal(new Vec3d(17, 65, -28), result.Target);
        Assert.Equal(new Vec3d(16.14, 65.0, -27.50), result.StoppedAt);
        Assert.False(result.SubBlockRequest);
    }

    /// <summary>Arrival is by BLOCK. The player comes to rest wherever inside the destination block the physics leaves it (measured 2.323 then drifting to 2.453 for a destination block of x=2), so an exact-point comparison would report every real arrival as a failure.</summary>
    [Fact]
    public void ArrivingAnywhereInsideTheDestinationBlockIsAnArrival()
    {
        MoveResult result = Navigator.Judge(new Vec3d(17, 65, -28), new Vec3d(17.453, 65.0, -27.50), subBlockRequest: false);

        Assert.True(result.Reached);
        Assert.Equal(MoveOutcome.Reached, result.Outcome);
    }

    /// <summary>Negative coordinates are where a truncating comparison goes wrong, and every coordinate in the reproduction was negative. -27.50 is inside block -28, and -27.49 is not.</summary>
    [Theory]
    [InlineData(-27.50, true)]
    [InlineData(-27.01, true)]
    [InlineData(-27.49, true)]
    [InlineData(-26.99, false)]
    [InlineData(-28.01, false)]
    public void TheBlockComparisonFloorsRatherThanTruncates(double z, bool expected)
    {
        MoveResult result = Navigator.Judge(new Vec3d(17, 65, -28), new Vec3d(17.5, 65.0, z), subBlockRequest: false);

        Assert.Equal(expected, result.Reached);
    }

    /// <summary>A miss on Y alone is still a miss. A directional move resolves to the block above/below/beside the player's head or feet, and on a flat platform there is nothing to stand on there: the verdict must not report an arrival.</summary>
    [Fact]
    public void AMissOnTheVerticalAloneIsStillAMiss()
    {
        MoveResult result = Navigator.Judge(new Vec3d(15.5, 66, -27.5), new Vec3d(15.5, 65.0, -27.5), subBlockRequest: false);

        Assert.False(result.Reached);
    }

    // The SUB-BLOCK half. A move-to-a-point-inside-the-current-block request is a request for a fraction of a block, so a block comparison can be true before the move runs.

    /// <summary>A move to (100.5, 80, 100.5) can leave the player at (100.12, 80.0, 100.12) both before and after. Under a block verdict this is a PASS, which is why <see cref="MoveResult.SubBlockRequest"/> exists and is load-bearing: the same arrival is a pass under the block verdict and a fail under the sub-block one.</summary>
    [Fact]
    public void ANoOpCentreIsNotReportedAsAnArrival()
    {
        var target = new Vec3d(100.5, 80, 100.5);
        var stayedPut = new Vec3d(100.12, 80.0, 100.12);

        Assert.True(Navigator.Judge(target, stayedPut, subBlockRequest: false).Reached);

        MoveResult result = Navigator.Judge(target, stayedPut, subBlockRequest: true);

        Assert.False(result.Reached);
        Assert.Equal(MoveOutcome.StoppedShort, result.Outcome);
        Assert.Equal(0.38 * Math.Sqrt(2), result.HorizontalDistance, 10);
    }

    /// <summary>A centre reached within the library's own tolerance is an arrival. The bound is <see cref="Navigator.SubBlockArrivalTolerance"/> rather than a caller-side guess at what the approach achieves; measured worst case over five start offsets on protocols 47 and 772 is 0.021.</summary>
    [Theory]
    [InlineData(0.021, true)]
    [InlineData(0.0999, true)]
    [InlineData(0.1001, false)]
    [InlineData(0.38, false)]
    public void TheSubBlockVerdictIsTheLibrarysOwnTolerance(double offset, bool expected)
    {
        var target = new Vec3d(100.5, 80, 100.5);
        var arrival = new Vec3d(100.5 + offset, 80.0, 100.5);

        MoveResult result = Navigator.Judge(target, arrival, subBlockRequest: true);

        Assert.Equal(expected, result.Reached);
        Assert.Equal(offset, result.HorizontalDistance, 10);
    }

    /// <summary>The sub-block verdict is horizontal, matching what the approach can actually control: it walks, so it cannot change the player's Y within a block, and judging Y here would fail every honest centre.</summary>
    [Fact]
    public void TheSubBlockVerdictIgnoresTheVerticalItCannotControl()
    {
        var target = new Vec3d(100.5, 80, 100.5);
        var arrival = new Vec3d(100.52, 80.0, 100.48);

        Assert.True(Navigator.Judge(target, arrival, subBlockRequest: true).Reached);
    }

    // IsSubBlockRequest: what selects between the two verdicts above.

    [Fact]
    public void IsSubBlockRequest_IsTrue_WhenTheRequestNeverLeavesTheOccupiedBlock()
        => Assert.True(Navigator.IsSubBlockRequest(new Vec3d(0.2, 65, 0.2), new Vec3d(0.8, 65, 0.8)));

    [Fact]
    public void IsSubBlockRequest_IsFalse_WhenTheRequestCrossesIntoAnotherBlock()
        => Assert.False(Navigator.IsSubBlockRequest(new Vec3d(0.9, 65, 0.5), new Vec3d(1.1, 65, 0.5)));

    // StoppedNear: the third verdict, and the one shape it must never take.

    /// <summary>A miss at a destination block no body can occupy reads <see cref="MoveOutcome.StoppedNear"/>, which is what lets a host say "nothing can stand there" instead of "I could not compute a path". <c>Reached</c> stays false either way: getting close is not arriving.</summary>
    [Fact]
    public void AMissAtAnUnstandableDestinationIsStoppedNear()
    {
        MoveResult result = Navigator.Judge(
            new Vec3d(-295, -14, 393), new Vec3d(-295.5, -14, 393.5), subBlockRequest: false)
            with
        { DestinationUnstandable = true };

        Assert.False(result.Reached);
        Assert.Equal(MoveOutcome.StoppedNear, result.Outcome);
    }

    /// <summary>
    /// A SUB-BLOCK request never reads <see cref="MoveOutcome.StoppedNear"/>, whatever the flag says, and the case that forces the rule is the one where the answer would be a lie: such a request never leaves the block the body occupies, so the destination block IS the occupied block, and the only way it fails the occupancy test is a body already inside terrain. "The walk stopped in the nearest block that can hold a body", naming the cell the body never left, would be false twice over.
    /// <para>Pinned on the record rather than through the navigator, deliberately. Driving a live fixture into this state needs a body that cannot walk half a block inside its own cell, and the offline fixture will not produce one: a body teleported inside the solid pillar walks to the centre and the verdict comes back <c>Reached</c> at 0.0061, so a navigator-level row asserting this passes with the rule removed and pins nothing. Here the rule is a total function of the record and cannot be dodged.</para>
    /// </summary>
    [Fact]
    public void ASubBlockRequestIsNeverStoppedNearEvenWithTheFlagSet()
    {
        MoveResult result = Navigator.Judge(
            new Vec3d(100.5, 80, 100.5), new Vec3d(100.12, 80, 100.12), subBlockRequest: true)
            with
        { DestinationUnstandable = true };

        Assert.False(result.Reached);
        Assert.Equal(MoveOutcome.StoppedShort, result.Outcome);
    }
}
