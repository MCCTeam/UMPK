using Umpk.Game.Players;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Game.Tests.Players;

/// <summary><see cref="PlayerReach"/> names vanilla's interaction-reach rule instead of a single unexplained "distance &lt;= 25" at every call site. There are three distinct rules pinned here: the modern (1.20.5+) box-based client/server rule, the legacy (pre-1.20.5) 6.0-to-centre server rule, and the eye height both are measured from.</summary>
public sealed class PlayerReachTests
{
    [Theory]
    [InlineData(GameMode.Survival, PlayerReach.SurvivalBlockReach)]
    [InlineData(GameMode.Creative, PlayerReach.CreativeBlockReach)]
    [InlineData(GameMode.Spectator, PlayerReach.CreativeBlockReach)]
    public void BlockReachFor_MatchesVanillasAttributeDefaults(GameMode mode, double expected)
        => Assert.Equal(expected, PlayerReach.BlockReachFor(mode));

    [Theory]
    [InlineData(GameMode.Survival, PlayerReach.SurvivalEntityReach)]
    [InlineData(GameMode.Creative, PlayerReach.CreativeEntityReach)]
    public void EntityReachFor_MatchesVanillasAttributeDefaults_AndMeasuresToTheEntityBox(GameMode mode, double expected)
    {
        Assert.Equal(expected, PlayerReach.EntityReachFor(mode));

        // IsWithinEntityReach shares IsWithinBlockReach's box-not-centre measurement; exercised here so it is not left untested. An entity box whose nearest face is just inside the reach is in range, and just past it is not.
        Aabb entityBox = Aabb.BlockAt(0, 0, 0);
        var justInside = new Vec3d(1 + expected - 0.1, 0.5, 0.5);
        var justBeyond = new Vec3d(1 + expected + 0.1, 0.5, 0.5);

        Assert.True(PlayerReach.IsWithinEntityReach(justInside, entityBox, mode));
        Assert.False(PlayerReach.IsWithinEntityReach(justBeyond, entityBox, mode));
    }

    /// <summary>The point of measuring to the box: an eye 4.8 blocks from the block's CENTRE, which a naive centre-based check would reject under survival's 4.5 reach, is only 4.3 blocks from the block's nearest FACE, which is in range.</summary>
    [Fact]
    public void IsWithinBlockReach_MeasuresToTheBlockBox_NotItsCentre()
    {
        var target = new BlockPos(0, 0, 0);
        var eye = new Vec3d(5.3, 0.5, 0.5);

        Assert.True(PlayerReach.IsWithinBlockReach(eye, target, GameMode.Survival));

        Vec3d center = target.Center;
        double dx = eye.X - center.X;
        double dy = eye.Y - center.Y;
        double dz = eye.Z - center.Z;
        double centreDistance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        Assert.True(centreDistance > PlayerReach.SurvivalBlockReach);
    }

    [Theory]
    [InlineData(4.51)]
    [InlineData(10.0)]
    public void IsWithinBlockReach_RejectsBeyondSurvivalRange(double faceDistance)
    {
        var target = new BlockPos(0, 0, 0);
        var eye = new Vec3d(1 + faceDistance, 0.5, 0.5);

        Assert.False(PlayerReach.IsWithinBlockReach(eye, target, GameMode.Survival));
    }

    /// <summary>Vanilla rejects only STRICTLY beyond 36.0 (<c>&gt; MAX_INTERACTION_DISTANCE</c>), so exactly 6.0 blocks to the centre still passes and anything past it does not.</summary>
    [Fact]
    public void LegacyServerReach_IsSixBlocksToTheCentre_ExactBoundaryIsAccepted()
    {
        var target = new BlockPos(0, 0, 0);
        Vec3d center = target.Center;
        var atBoundary = new Vec3d(center.X + PlayerReach.LegacyServerBlockReach, center.Y, center.Z);
        var justBeyond = new Vec3d(center.X + PlayerReach.LegacyServerBlockReach + 0.0001, center.Y, center.Z);

        Assert.True(PlayerReach.IsWithinLegacyServerReach(atBoundary, target));
        Assert.False(PlayerReach.IsWithinLegacyServerReach(justBeyond, target));
    }

    /// <summary>5.5 blocks from the block's centre clears the legacy server's 6.0-block bound but is beyond survival's client-side box reach even measured to the nearest face (5.0). A server still running the pre-1.20.5 rule accepts a dig the modern client-side check would refuse to even attempt.</summary>
    [Fact]
    public void LegacyServerReach_AcceptsWhatTheClientPickRangeRefuses()
    {
        var target = new BlockPos(0, 0, 0);
        Vec3d center = target.Center;
        var eye = new Vec3d(center.X + 5.5, center.Y, center.Z);

        Assert.False(PlayerReach.IsWithinBlockReach(eye, target, GameMode.Survival));
        Assert.True(PlayerReach.IsWithinLegacyServerReach(eye, target));
    }

    [Fact]
    public void StandingEyeHeight_IsVanillasDefault()
    {
        Assert.Equal(1.62, PlayerReach.StandingEyeHeight);
        Assert.Equal(new Vec3d(0.5, 65.62, 0.5), PlayerReach.EyePosition(new Vec3d(0.5, 64.0, 0.5)));
    }

    /// <summary>A single unexplained "5, to the centre, in every game mode" is neither rule this class models: the modern rule's survival bound is 4.5 measured to the BOX, and the legacy rule's bound is 6.0 measured to the centre. A point sitting exactly 5.0 from the centre lands exactly on the survival box's boundary (4.5 to the nearest face) and vanilla's comparison there is strict, so it is rejected, while the same point sits comfortably inside the legacy 6.0-to-centre bound.</summary>
    [Fact]
    public void MccsInlineFiveToCentre_IsNeitherVanillaRule()
    {
        Assert.NotEqual(5.0, PlayerReach.SurvivalBlockReach);
        Assert.NotEqual(5.0, PlayerReach.LegacyServerBlockReach);

        var target = new BlockPos(0, 0, 0);
        Vec3d center = target.Center;
        var eye = new Vec3d(center.X + 5.0, center.Y, center.Z);

        Assert.False(PlayerReach.IsWithinBlockReach(eye, target, GameMode.Survival));
        Assert.True(PlayerReach.IsWithinLegacyServerReach(eye, target));
    }
}
