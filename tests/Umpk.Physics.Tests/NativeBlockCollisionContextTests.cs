using System.Text.Json;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

public sealed class NativeBlockCollisionContextTests
{
    public static IEnumerable<object[]> Releases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("version").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void ScaffoldAboveBoundaryMatchesTheNativeFloatTolerance(string version)
        => AssertNativeQueries(version, "SCAFFOLDING", BlockKind.Scaffolding, false);

    [Theory]
    [MemberData(nameof(Releases))]
    public void PowderSnowAboveBoundaryMatchesTheNativeFloatTolerance(string version)
        => AssertNativeQueries(version, "POWDER_SNOW", BlockKind.PowderSnow, true);

    private static void AssertNativeQueries(string version, string block, BlockKind kind, bool walkable)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        var world = new FixtureWorld().Set(0, 64, 0, kind);
        var scratch = new ColliderBuffer();
        var body = new BodyCollisionContext(false, 0, walkable);
        int exercised = 0;
        foreach (JsonElement row in document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()].EnumerateArray())
        {
            if (row.GetProperty("block").GetString() != block)
                continue;
            JsonElement coordinates = row.GetProperty("query");
            var query = new Aabb(coordinates[0].GetDouble(), coordinates[1].GetDouble(), coordinates[2].GetDouble(), coordinates[3].GetDouble(), coordinates[4].GetDouble(), coordinates[5].GetDouble());

            bool clear = CollisionResolver.NoCollision(world, query, row.GetProperty("entity_bottom").GetDouble(), body, scratch);

            Assert.Equal(row.GetProperty("clear").GetBoolean(), clear);
            exercised++;
        }
        Assert.Equal(5, exercised);
    }

    private static JsonDocument ReadNative()
    {
        using Stream source = typeof(NativeBlockCollisionContextTests).Assembly.GetManifestResourceStream("Umpk.Physics.Tests.Fixtures.BlockCollisionContext.native.json")
            ?? throw new InvalidOperationException("The native block collision context fixture is missing.");
        return JsonDocument.Parse(source);
    }
}
