using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

public sealed class PhysicsBorderSourceTests
{
    [Theory]
    [InlineData(498, 8.0, EntityPose.Crouching)]
    [InlineData(498, 9.9, EntityPose.Standing)]
    [InlineData(498, 12.0, EntityPose.Crouching)]
    [InlineData(766, 8.0, EntityPose.Crouching)]
    [InlineData(766, 9.9, EntityPose.Standing)]
    [InlineData(766, 12.0, EntityPose.Crouching)]
    [InlineData(767, 8.0, EntityPose.Crouching)]
    [InlineData(767, 9.9, EntityPose.Standing)]
    [InlineData(767, 12.0, EntityPose.Crouching)]
    [InlineData(777, 8.0, EntityPose.Crouching)]
    [InlineData(777, 9.9, EntityPose.Standing)]
    [InlineData(777, 12.0, EntityPose.Crouching)]
    public void PoseClearanceUsesTheCurrentBodyAtTheBorder(int protocol, double x, EntityPose expectedPose)
    {
        // 26.3-client-decompiled net/minecraft/world/entity/player/Player.java: canPlayerFitWithinBlocksAndEntitiesWhen supplies this entity to noCollision; an obstructed swimming box prevents a pose change. The measured border predicate keeps its native proximity guard even outside the border.
        var world = new BorderWorld(protocol);
        var position = new Vec3d(x, 64, .5);
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.Reset(position, 0, 0);

        StepResult result = engine.Step(new MovementInput { Sneak = true });

        Assert.Equal(expectedPose, result.State.Pose);
        Assert.NotEmpty(world.Queries);
        Assert.All(world.Queries, query => Assert.Equal(position, query.Source));
    }

    [Theory]
    [InlineData(498)]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(777)]
    public void PoseClearanceReadsBorderReplacementAndTheReseededBody(int protocol)
    {
        var world = new BorderWorld(protocol);
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.Reset(new Vec3d(9.9, 64, .5), 0, 0);
        Assert.Equal(EntityPose.Standing, engine.Step(new MovementInput { Sneak = true }).State.Pose);

        world.Border = world.Border.WithSize(40);
        world.Queries.Clear();
        var destination = new Vec3d(-9.9, 80, -3.5);
        engine.Reset(destination, 0, 0);

        Assert.Equal(EntityPose.Crouching, engine.Step(new MovementInput { Sneak = true }).State.Pose);
        Assert.NotEmpty(world.Queries);
        Assert.All(world.Queries, query => Assert.Equal(destination, query.Source));
    }

    private sealed class BorderWorld(int protocol) : IPhysicsWorldView
    {
        private readonly FixtureWorld _blocks = new();
        private readonly WorldBorderContainmentEra _era = WorldBorderState.ContainmentEraForProtocol(protocol);

        internal WorldBorderState Border { get; set; } = WorldBorderState.Default.WithSize(20);
        internal List<(Aabb Box, Vec3d Source)> Queries { get; } = [];

        public BlockState GetBlock(BlockPos pos) => _blocks.GetBlock(pos);
        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => _blocks.GetCollisionShapes(state);
        public bool IsChunkLoaded(BlockPos pos) => true;

        public bool CollidesWithBorder(in Aabb box, Vec3d sourcePosition)
        {
            Queries.Add((box, sourcePosition));
            return Border.CollidesWith(box, sourcePosition, _era);
        }
    }
}
