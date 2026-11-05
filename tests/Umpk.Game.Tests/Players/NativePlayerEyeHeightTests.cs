using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.TestKit.Fixtures;
using Xunit;

namespace Umpk.Game.Tests.Players;

public sealed class NativePlayerEyeHeightTests
{
    public static IEnumerable<object[]> Versions() => NativePlayerDimensions.Versions();

    [Theory]
    [MemberData(nameof(Versions))]
    public void StandingReachOriginMatchesTheNativeEye(string version, int _)
    {
        foreach (NativePlayerDimension row in NativePlayerDimensions.ForVersion(version).Where(row => row.Pose == EntityPose.Standing))
        {
            double eyeHeight = PlayerReach.StandingEyeHeight;
            Assert.Equal(row.EyeHeight, eyeHeight);
            Assert.Equal(row.Position.Add(0, row.EyeHeight, 0), PlayerReach.EyePosition(row.Position));
        }
    }
}
