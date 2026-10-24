using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Umpk.TestKit.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

public sealed class PlayerCorrectionTests
{
    public static IEnumerable<object[]> Versions() => NativePlayerCorrections.Versions();

    [Theory]
    [MemberData(nameof(Versions))]
    public void CorrectionMatchesNativePoseContactAndFallState(string version, int protocol)
    {
        var engine = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.ForProtocol(protocol));
        foreach (NativePlayerCorrection row in NativePlayerCorrections.ForVersion(version))
        {
            NativePlayerCorrectionState before = row.Before;
            engine.LoadState(new PhysicsState
            {
                Position = before.Position,
                Velocity = before.Velocity,
                Yaw = before.Yaw,
                Pitch = before.Pitch,
                Pose = before.Pose,
                OnGround = before.OnGround,
                HorizontalCollision = before.HorizontalCollision,
                VerticalCollision = before.VerticalCollision,
                FallDistance = before.FallDistance,
            });
            AssertState(before, engine.State);

            NativePlayerCorrectionState after = row.After;
            engine.ApplyPositionCorrection(after.Position, after.Yaw, after.Pitch, after.Velocity);

            AssertState(after, engine.State);
        }
    }

    [Theory]
    [InlineData(47)]
    [InlineData(404)]
    [InlineData(498)]
    [InlineData(777)]
    public void CorrectionPreservesTheHeldJumpDelay(int protocol)
    {
        var engine = new PlayerPhysics(new FixtureWorld().Floor(-1, 1, -1, 1, 63, BlockKind.Stone), PhysicsProfile.ForProtocol(protocol));
        var feet = new Vec3d(.5, 64, .5);
        engine.Reset(feet, 0, 0);
        engine.Step(MovementInput.None);
        engine.Step(MovementInput.None);
        Assert.True(engine.State.OnGround);
        var held = new MovementInput { Jump = true };
        engine.Step(held);
        Assert.True(engine.State.Velocity.Y > 0);

        engine.ApplyPositionCorrection(feet, 0, 0, Vec3d.Zero);

        for (int tick = 0; tick < 3; tick++)
        {
            engine.Step(held);
            Assert.Equal(64, engine.State.Position.Y);
        }
    }

    [Fact]
    public void ResetStillClearsPlayerState()
    {
        var engine = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.Modern);
        engine.LoadState(new PhysicsState
        {
            Position = new Vec3d(1, 2, 3),
            Velocity = new Vec3d(4, 5, 6),
            Pose = EntityPose.Swimming,
            OnGround = true,
            HorizontalCollision = true,
            VerticalCollision = true,
            FallDistance = 8,
            InWater = true,
            IsUnderWater = true,
            InLava = true,
            OnClimbable = true,
        });
        var destination = new Vec3d(-10, 100, 20);

        engine.Reset(destination, 12, 34);

        PhysicsState actual = engine.State;
        Assert.Equal(destination, actual.Position);
        Assert.Equal(Vec3d.Zero, actual.Velocity);
        Assert.Equal(EntityPose.Standing, actual.Pose);
        Assert.Equal(0, actual.FallDistance);
        Assert.False(actual.OnGround);
        Assert.False(actual.HorizontalCollision);
        Assert.False(actual.VerticalCollision);
        Assert.False(actual.InWater);
        Assert.False(actual.IsUnderWater);
        Assert.False(actual.InLava);
        Assert.False(actual.OnClimbable);
    }

    private static void AssertState(NativePlayerCorrectionState expected, PhysicsState actual)
    {
        Assert.Equal(expected.Position, actual.Position);
        Assert.Equal(expected.Velocity, actual.Velocity);
        Assert.Equal(expected.Yaw, actual.Yaw);
        Assert.Equal(expected.Pitch, actual.Pitch);
        Assert.Equal(expected.Pose, actual.Pose);
        Assert.Equal(expected.Box, actual.BoundingBox);
        Assert.Equal(expected.EyeHeight, actual.EyeHeight);
        Assert.Equal(expected.Position.Add(0, expected.EyeHeight, 0), actual.EyePosition);
        Assert.Equal(expected.OnGround, actual.OnGround);
        Assert.Equal(expected.HorizontalCollision, actual.HorizontalCollision);
        Assert.Equal(expected.VerticalCollision, actual.VerticalCollision);
        Assert.Equal(expected.FallDistance, actual.FallDistance);
    }
}
