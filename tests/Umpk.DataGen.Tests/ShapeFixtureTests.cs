using Xunit;

namespace Umpk.DataGen.Tests;

/// <summary>Representative AABB fixtures per era. These assert that the shape pool carries the expected geometry for a small set of blocks.</summary>
public sealed class ShapeFixtureTests
{
    // Dataset root discovered from the test output directory.
    private static string DataRoot => LocateDataRoot();

    [Fact]
    public void Modern_stone_is_a_full_unit_cube()
    {
        Dataset dataset = DatasetLoader.Load(DataRoot);
        VersionData v770 = dataset.ByProtocol[770];
        IReadOnlyList<int> stoneStates = v770.Shapes.Collision!["minecraft:stone"];
        // Stone has one state; its collision shape is the full cube.
        IReadOnlyList<IReadOnlyList<double>> shape = v770.Shapes.Shapes[stoneStates[0]];
        AssertSingleBox(shape, 0, 0, 0, 1, 1, 1);
    }

    [Fact]
    public void Modern_air_has_empty_collision()
    {
        Dataset dataset = DatasetLoader.Load(DataRoot);
        VersionData v770 = dataset.ByProtocol[770];
        IReadOnlyList<int> airStates = v770.Shapes.Collision!["minecraft:air"];
        Assert.All(airStates, idx => Assert.Empty(v770.Shapes.Shapes[idx]));
    }

    [Fact]
    public void Legacy_stone_full_cube_and_air_empty()
    {
        Dataset dataset = DatasetLoader.Load(DataRoot);
        VersionData v47 = dataset.ByProtocol[47];
        int stoneIdx = v47.Shapes.CollisionByBlockId!["1"];
        int airIdx = v47.Shapes.CollisionByBlockId!["0"];
        AssertSingleBox(v47.Shapes.Shapes[stoneIdx], 0, 0, 0, 1, 1, 1);
        Assert.Empty(v47.Shapes.Shapes[airIdx]);
    }

    [Fact]
    public void Legacy_slab_is_bottom_half()
    {
        Dataset dataset = DatasetLoader.Load(DataRoot);
        VersionData v47 = dataset.ByProtocol[47];
        // Block id 44 is stone_slab; curated legacy shape is the bottom half-cube.
        int slabIdx = v47.Shapes.CollisionByBlockId!["44"];
        AssertSingleBox(v47.Shapes.Shapes[slabIdx], 0, 0, 0, 1, 0.5, 1);
    }

    private static void AssertSingleBox(IReadOnlyList<IReadOnlyList<double>> shape,
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Assert.Single(shape);
        IReadOnlyList<double> box = shape[0];
        Assert.Equal(6, box.Count);
        Assert.Equal(minX, box[0], 6);
        Assert.Equal(minY, box[1], 6);
        Assert.Equal(minZ, box[2], 6);
        Assert.Equal(maxX, box[3], 6);
        Assert.Equal(maxY, box[4], 6);
        Assert.Equal(maxZ, box[5], 6);
    }

    private static string LocateDataRoot()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor is not null && !Directory.Exists(Path.Combine(cursor.FullName, "data", "java")))
            cursor = cursor.Parent;

        if (cursor is null)
            throw new DirectoryNotFoundException("could not locate data/java from the test output dir");

        return Path.Combine(cursor.FullName, "data", "java");
    }
}
