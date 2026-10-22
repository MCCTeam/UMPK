using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Umpk.TestKit.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

public sealed class HostBodyGeometryPhysicsTests
{
    private static readonly Vec3d ObservedPosition = new(1.5, 63.6875, 1.100000023841858);
    private static readonly Aabb WakeBounds = new(1.3999999985098839, 63.6875, 1.0000000223517418, 2.000000022351742, 65.48749995231628, 1.6000000461935997);

    [Fact]
    public void ApplyingCachedBodyPreservesIndependentPositionVelocityAndEye()
    {
        var engine = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.ForProtocol(107));
        engine.Reset(ObservedPosition, 12, 34);
        var velocity = new Vec3d(.125, .25, -.375);
        engine.SetVelocity(velocity);
        var dimensions = new PhysicsBodyDimensions(.2F, .2F, 1.62F);
        Aabb box = Aabb.OfSize(7, 8, 9, .2F, .2F);
        engine.ApplyBodyGeometry(box, dimensions);
        Assert.Equal(ObservedPosition, engine.State.Position);
        Assert.Equal(velocity, engine.State.Velocity);
        Assert.Equal(box, engine.State.BoundingBox);
        Assert.Equal(dimensions, engine.State.BodyDimensions);
        Assert.Equal((double)1.62F, engine.State.EyeHeight);
        Assert.True(engine.State.EyeHeight > engine.State.BoundingBox.YSize);
        PhysicsState stepped = engine.Step(new MovementInput { Sneak = true }).State;
        Assert.Equal(EntityPose.Crouching, stepped.Pose);
        Assert.Equal(dimensions, stepped.BodyDimensions);
        Assert.Equal((double).2F, stepped.BoundingBox.YSize);
        Assert.Equal((double)1.62F, stepped.EyeHeight);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(315)]
    [InlineData(404)]
    public void ZeroAirMoveDerivesPositionFromTheActualCachedBounds(int protocol)
    {
        var engine = Create(protocol);
        PhysicsState state = engine.Step(MovementInput.None).State;
        Assert.Equal(WakeBounds, state.BoundingBox);
        Assert.Equal(new Vec3d((WakeBounds.MinX + WakeBounds.MaxX) / 2, WakeBounds.MinY, (WakeBounds.MinZ + WakeBounds.MaxZ) / 2), state.Position);
        Assert.Equal((double)1.62F, state.EyeHeight);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(315)]
    [InlineData(404)]
    public void SameCoordinateExplicitCorrectionRecentersUsingNativeFloatDimensions(int protocol)
    {
        var engine = Create(protocol);
        var velocity = new Vec3d(.125, .25, -.375);
        engine.ApplyPositionCorrection(ObservedPosition, 12, 34, velocity);
        Assert.Equal(Aabb.OfSize(ObservedPosition.X, ObservedPosition.Y, ObservedPosition.Z, .6F, 1.8F), engine.State.BoundingBox);
        Assert.Equal(ObservedPosition, engine.State.Position);
        Assert.Equal(velocity, engine.State.Velocity);
        Assert.Equal(new PhysicsBodyDimensions(.6F, 1.8F, 1.62F), engine.State.BodyDimensions);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(315)]
    [InlineData(404)]
    public void SnapshotAndForwardSimulationKeepTheCachedBody(int protocol)
    {
        var engine = Create(protocol);
        PhysicsState start = engine.State;
        var copy = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.ForProtocol(protocol));
        copy.LoadState(start);
        Assert.Equal(start, copy.State);
        MovementInput[] inputs = [MovementInput.None, MovementInput.None];
        PhysicsState prediction = PhysicsSimulator.Run(start, PhysicsConditions.Default, new FixtureWorld(), PhysicsProfile.ForProtocol(protocol), inputs);
        foreach (MovementInput input in inputs)
            engine.Step(input);
        Assert.Equal(engine.State, prediction);
        Assert.Equal(start.BodyDimensions, prediction.BodyDimensions);
        Assert.Equal((double)1.62F, prediction.EyeHeight);
    }

    [Fact]
    public void CollisionUsesTheOffsetBodyRatherThanTheObservedPosition()
    {
        var world = new HardBoxWorld();
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(107));
        engine.Reset(ObservedPosition, 0, 0);
        engine.ApplyBodyGeometry(WakeBounds, new PhysicsBodyDimensions(.6F, 1.8F, 1.62F));
        ((IPistonPushTarget)engine).MoveByPiston(new Vec3d(.3, 0, 0));
        Assert.Equal(2.25, engine.State.BoundingBox.MaxX);
        Assert.True(engine.State.HorizontalCollision);
        Assert.Equal((engine.State.BoundingBox.MinX + engine.State.BoundingBox.MaxX) / 2, engine.State.Position.X);
        Assert.True(world.Queries > 0);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(777)]
    public void NullOverridePreservesEveryExistingDefaultStep(int protocol)
    {
        var world = new FixtureWorld();
        var control = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        var cleared = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        control.Reset(ObservedPosition, 0, 0);
        cleared.Reset(ObservedPosition, 0, 0);
        cleared.ApplyBodyGeometry(WakeBounds, new PhysicsBodyDimensions(.6F, 1.8F, .2F));
        cleared.ApplyBodyGeometry(default, null);
        Assert.Equal(control.State, cleared.State);
        MovementInput[] inputs = [MovementInput.None, new() { Forward = true }, new() { Sneak = true }, new() { Jump = true }];
        foreach (MovementInput input in inputs)
            Assert.Equal(control.Step(input), cleared.Step(input));
    }

    [Fact]
    public void ResetAndProfileReplacementClearHostBodyState()
    {
        var engine = Create(107);
        engine.Reset(ObservedPosition, 0, 0);
        Assert.Null(engine.State.BodyDimensions);
        engine.ApplyBodyGeometry(WakeBounds, new PhysicsBodyDimensions(.6F, 1.8F, .2F));
        engine.Profile = PhysicsProfile.ForProtocol(477);
        Assert.Null(engine.State.BodyDimensions);
        Assert.Equal(EntityPose.Standing, engine.State.Pose);
    }

    private static PlayerPhysics Create(int protocol)
    {
        var engine = new PlayerPhysics(new FixtureWorld(), PhysicsProfile.ForProtocol(protocol));
        engine.Reset(ObservedPosition, 0, 0);
        engine.ApplyBodyGeometry(WakeBounds, new PhysicsBodyDimensions(.6F, 1.8F, 1.62F));
        return engine;
    }

    private sealed class HardBoxWorld : IPhysicsWorldView
    {
        private readonly FixtureWorld _blocks = new();
        public int Queries { get; private set; }
        public Umpk.Game.Blocks.BlockState GetBlock(BlockPos pos) => _blocks.GetBlock(pos);
        public ReadOnlySpan<Aabb> GetCollisionShapes(Umpk.Game.Blocks.BlockState state) => _blocks.GetCollisionShapes(state);
        public bool IsChunkLoaded(BlockPos pos) => true;
        public void CollectEntityColliders(in Aabb region, ICollection<Aabb> into)
        {
            Queries++;
            var collider = new Aabb(2.25, 63, 0, 3.25, 66, 3);
            if (region.Intersects(collider)) into.Add(collider);
        }
    }
}
