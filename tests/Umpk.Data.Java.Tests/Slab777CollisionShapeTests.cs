using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class Slab777CollisionShapeTests
{
    private static readonly string[] NewSlabs = [
        "white_wool_slab",
        "orange_wool_slab",
        "magenta_wool_slab",
        "light_blue_wool_slab",
        "yellow_wool_slab",
        "lime_wool_slab",
        "pink_wool_slab",
        "gray_wool_slab",
        "light_gray_wool_slab",
        "cyan_wool_slab",
        "purple_wool_slab",
        "blue_wool_slab",
        "brown_wool_slab",
        "green_wool_slab",
        "red_wool_slab",
        "black_wool_slab",
        "poplar_slab",
        "white_concrete_slab",
        "orange_concrete_slab",
        "magenta_concrete_slab",
        "light_blue_concrete_slab",
        "yellow_concrete_slab",
        "lime_concrete_slab",
        "pink_concrete_slab",
        "gray_concrete_slab",
        "light_gray_concrete_slab",
        "cyan_concrete_slab",
        "purple_concrete_slab",
        "blue_concrete_slab",
        "brown_concrete_slab",
        "green_concrete_slab",
        "red_concrete_slab",
        "black_concrete_slab"
    ];

    public static IEnumerable<object[]> Cases()
    {
        foreach (string name in NewSlabs)
            foreach (int offset in Enumerable.Range(0, 6))
                yield return [777, name, offset];
        foreach (int protocol in new[] { 776, 777 })
            foreach (int offset in Enumerable.Range(0, 6))
                yield return [protocol, "oak_slab", offset];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryNativeSlabStateUsesItsExactHalfOrFullCollisionInGeneralLookup(int protocol, string name, int offset)
    {
        var entry = JavaGameData.Registries(protocol).Blocks[Identifier.Minecraft(name)];
        int stateId = entry.Value.MinStateId + offset;
        Assert.Equal(6, entry.Value.StateCount);
        Assert.True(entry.Value.TryGetPropertyValue(stateId, "type", out string? type));
        Assert.Equal(offset < 2 ? "top" : offset < 4 ? "bottom" : "double", type);
        Assert.True(entry.Value.TryGetPropertyValue(stateId, "waterlogged", out string? waterlogged));
        Assert.Equal(offset % 2 == 0 ? "true" : "false", waterlogged);
        Aabb expected = offset < 2 ? new(0, .5, 0, 1, 1, 1) : offset < 4 ? new(0, 0, 0, 1, .5, 1) : new(0, 0, 0, 1, 1, 1);
        var shapes = (JavaBlockShapes)JavaGameData.BlockShapes(protocol);
        Assert.Equal(expected, Assert.Single(shapes.GetCollisionShapes(stateId).ToArray()));
        Assert.True(shapes.Covers(stateId), $"protocol {protocol}, {name}, offset {offset} uses an unmeasured flag fallback");
    }
}
