using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Umpk.TestKit.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

public sealed class NativePlayerDimensionsTests
{
    public static IEnumerable<object[]> Versions() => NativePlayerDimensions.Versions();

    [Theory]
    [MemberData(nameof(Versions))]
    public void ProfileDimensionsMatchNativeScalars(string version, int protocol)
    {
        PhysicsProfile profile = PhysicsProfile.ForProtocol(protocol);
        foreach (NativePlayerDimension row in NativePlayerDimensions.ForVersion(version).Where(row => row.Position == Vec3d.Zero))
        {
            double height = row.Pose switch
            {
                EntityPose.Crouching => profile.CrouchHeight,
                EntityPose.Swimming or EntityPose.FallFlying => profile.SwimHeight,
                _ => profile.StandingHeight,
            };
            Assert.Equal(row.Box.XSize, profile.Width);
            Assert.Equal(row.Box.YSize, height);
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void BodyBoxesMatchNativePoseAndPosition(string version, int protocol)
    {
        var engine = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.ForProtocol(protocol));
        foreach (NativePlayerDimension row in NativePlayerDimensions.ForVersion(version))
        {
            engine.LoadState(new PhysicsState { Position = row.Position, Pose = row.Pose });
            Assert.Equal(row.Box, engine.State.BoundingBox);
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void EyePositionsMatchNativePoseAndPosition(string version, int protocol)
    {
        var engine = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.ForProtocol(protocol));
        foreach (NativePlayerDimension row in NativePlayerDimensions.ForVersion(version))
        {
            engine.LoadState(new PhysicsState { Position = row.Position, Pose = row.Pose });
            Assert.Equal(row.EyeHeight, engine.State.EyeHeight);
            Assert.Equal(row.Position.Add(0, row.EyeHeight, 0), engine.State.EyePosition);
        }
    }

    [Theory]
    [InlineData(47, 1.5399999618530273)]
    [InlineData(107, 1.5399999618530273)]
    [InlineData(404, 1.5399999618530273)]
    [InlineData(477, 1.2699999809265137)]
    [InlineData(777, 1.2699999809265137)]
    public void LoadedSnapshotsAndPoseCopiesUseTheEngineEra(int protocol, double crouchEyeHeight)
    {
        var modern = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.Modern);
        modern.LoadState(new PhysicsState { Position = new Vec3d(.5, 64, .5), Pose = EntityPose.Crouching });
        var engine = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.ForProtocol(protocol));
        engine.LoadState(modern.State);

        PhysicsState state = engine.State;
        PhysicsState standing = state with { Pose = EntityPose.Standing };
        Assert.Equal(crouchEyeHeight, state.EyeHeight);
        Assert.Equal(1.6200000047683716, standing.EyeHeight);
        Assert.Equal(crouchEyeHeight, (standing with { Pose = EntityPose.Crouching }).EyeHeight);
    }

    [Fact]
    public void ManuallyCreatedSnapshotsRetainTheirModernDefault()
    {
        var state = new PhysicsState { Pose = EntityPose.Crouching };
        Assert.Equal(1.2699999809265137, state.EyeHeight);
    }
}
