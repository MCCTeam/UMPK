using System.Text.Json;
using Umpk.Game.Blocks;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

public sealed class BlockCollisionGridNativeTests
{
    [Fact]
    public void RepeatedExactGridQueriesAllocateNothingAfterWarmup()
    {
        var world = new GridWorld
        {
            Position = BlockPos.Zero,
            CollisionQueryEra = BlockCollisionQueryEra.Modern,
            Grid = new BlockCollisionGrid([0, 1], [0, .5, 1], [0, 1], [0], [new(0, 0, 0, 1, .5, 1)]),
        };
        var query = new PhysicsCollisionQuery(world);
        var box = new Aabb(-.25, .125, .25, 2e-7, .375, .75);
        var source = new Vec3d(.5, 65, .5);
        for (int index = 0; index < 64; index++)
            query.NoCollision(source, box);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool clear = false;
        for (int index = 0; index < 1000; index++)
            clear |= query.NoCollision(source, box);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(clear);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(498)]
    [InlineData(735)]
    [InlineData(755)]
    [InlineData(777)]
    public void CollisionQueryMatchesNativeVoxelFacesAndInternalPlanes(int protocol)
    {
        using Stream stream = typeof(BlockCollisionGridNativeTests).Assembly.GetManifestResourceStream("Umpk.Physics.Tests.Fixtures.BlockCollisionGrid.native.json")!;
        using JsonDocument fixture = JsonDocument.Parse(stream);
        JsonElement release = fixture.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("protocol").GetInt32() == protocol);
        BlockCollisionGrid[] shapes = release.GetProperty("shapes").EnumerateArray().Select(Grid).ToArray();
        var world = new GridWorld { CollisionQueryEra = protocol < 735 ? BlockCollisionQueryEra.QueryFirst : protocol < 755 ? BlockCollisionQueryEra.BlockFirst : BlockCollisionQueryEra.Modern };
        var query = new PhysicsCollisionQuery(world);
        var failures = new List<string>();
        int count = 0;
        foreach (JsonElement row in release.GetProperty("cases").EnumerateArray())
        {
            world.Position = new BlockPos(row[2][0].GetInt32(), row[2][1].GetInt32(), row[2][2].GetInt32());
            world.Grid = shapes[row[1].GetInt32()];
            Aabb box = Box(row[3]);
            bool actual = query.NoCollision(new Vec3d(.5, 65, .5), box);
            bool singleBlockClear = !query.IntersectsBlock(world.Position, box);
            if (actual != row[4].GetBoolean() || singleBlockClear != row[4].GetBoolean())
                failures.Add(row[0].GetString()!);
            count++;
        }
        Assert.Equal(2838, count);
        Assert.True(failures.Count == 0, $"Protocol {protocol}: {failures.Count}/{count} mismatches; first: {string.Join(", ", failures.Take(8))}");
    }

    private static Aabb Box(JsonElement row) => new(row[0].GetDouble(), row[1].GetDouble(), row[2].GetDouble(), row[3].GetDouble(), row[4].GetDouble(), row[5].GetDouble());

    private static BlockCollisionGrid Grid(JsonElement row)
    {
        double[][] axes = row.GetProperty("planes").EnumerateArray().Select(axis => axis.EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray();
        return new BlockCollisionGrid(axes[0], axes[1], axes[2], row.GetProperty("cells").EnumerateArray().Select(value => value.GetInt32()).ToArray(), row.GetProperty("boxes").EnumerateArray().Select(Box).ToArray(), row.GetProperty("canonical_full_block").GetBoolean());
    }

    private sealed class GridWorld : IPhysicsWorldView
    {
        private readonly FixtureBlockData _data = new();
        public BlockPos Position { get; set; }
        public BlockCollisionGrid Grid { get; set; } = null!;
        public BlockCollisionQueryEra CollisionQueryEra { get; set; }
        public BlockState GetBlock(BlockPos pos) => new(_data, (int)(pos == Position ? BlockKind.Stone : BlockKind.Air));
        public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => state.IsAir ? [] : Grid.Boxes;
        public BlockCollisionGrid? GetCollisionGrid(BlockState state) => state.IsAir ? null : Grid;
        public bool IsChunkLoaded(BlockPos pos) => true;
    }
}
