using Umpk.Client.Navigation;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.TestKit.Fixtures;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class NativePhysicsDimensionsTests
{
    public static IEnumerable<object[]> Versions() => NativePlayerDimensions.Versions();

    [Theory]
    [MemberData(nameof(Versions))]
    public void SessionPhysicsProfileMatchesNativeDimensions(string version, int protocol)
    {
        JavaVersion descriptor = JavaVersions.All.Single(version => version.Version.Protocol == protocol);
        PhysicsProfile profile = PhysicsProfileFactory.FromJavaVersion(descriptor);
        foreach (NativePlayerDimension row in NativePlayerDimensions.ForVersion(version))
        {
            double height = row.Pose switch
            {
                EntityPose.Crouching => profile.CrouchHeight,
                EntityPose.Swimming or EntityPose.FallFlying => profile.SwimHeight,
                _ => profile.StandingHeight,
            };
            Assert.Equal(row.Box, Aabb.OfSize(row.Position.X, row.Position.Y, row.Position.Z, profile.Width, height));
        }
    }
}
