using System.Text.Json;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Game.Tests.World;

public sealed class WorldBorderCollisionNativeTests
{
    public static IEnumerable<object[]> NativeScenarios()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
        {
            JsonElement scenarios = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()];
            for (int index = 0; index < scenarios.GetArrayLength(); index++)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index];
        }
    }

    [Theory]
    [MemberData(nameof(NativeScenarios))]
    public void VoxelCollisionMatchesNativeProximityRoundingAndEpsilon(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        Assert.Equal(protocol, release.GetProperty("protocol").GetInt32());
        JsonElement scenario = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()][index];
        double size = scenario.GetProperty("size").GetDouble();
        var border = new WorldBorderState(scenario.GetProperty("center_x").GetDouble(), scenario.GetProperty("center_z").GetDouble(), size, size, 0, 5, 15)
            .WithAbsoluteMaxSize(scenario.GetProperty("absolute").GetInt32());
        Assert.Equal(new[] { border.MinX, border.MinZ, border.MaxX, border.MaxZ }, scenario.GetProperty("bounds").EnumerateArray().Select(value => value.GetDouble()));
        AssertQueries(version, protocol, index, scenario.GetProperty("queries"), border);
    }

    public static IEnumerable<object[]> MotionScenarios()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("motion_releases").EnumerateArray())
        {
            JsonElement scenarios = document.RootElement.GetProperty("motion_groups")[release.GetProperty("group").GetInt32()].GetProperty("cases");
            for (int index = 0; index < scenarios.GetArrayLength(); index++)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index];
        }
    }

    [Theory]
    [MemberData(nameof(MotionScenarios))]
    public void MovingBorderCollisionMatchesNativeClockAndWorldTickBounds(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("motion_releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        Assert.Equal(protocol, release.GetProperty("protocol").GetInt32());
        JsonElement group = document.RootElement.GetProperty("motion_groups")[release.GetProperty("group").GetInt32()];
        Assert.Equal(group.GetProperty("clock").GetString() == "world_ticks", WorldBorderState.InterpolationClockForProtocol(protocol) == WorldBorderInterpolationClock.WorldTicks);
        WorldBorderNativeTests.ReplayScenario(group.GetProperty("cases")[index], protocol, (state, border) => AssertQueries(version, protocol, index, state.GetProperty("collision_queries"), border));
    }

    private static void AssertQueries(string version, int protocol, int index, JsonElement queries, WorldBorderState border)
    {
        foreach (JsonElement query in queries.EnumerateArray())
        {
            JsonElement coordinates = query.GetProperty("box");
            var box = new Aabb(coordinates[0].GetDouble(), coordinates[1].GetDouble(), coordinates[2].GetDouble(), coordinates[3].GetDouble(), coordinates[4].GetDouble(), coordinates[5].GetDouble());
            Vec3d? source = query.TryGetProperty("source", out JsonElement point) ? new Vec3d(point[0].GetDouble(), point[1].GetDouble(), point[2].GetDouble()) : null;

            bool clear = !border.CollidesWith(box, source, WorldBorderState.ContainmentEraForProtocol(protocol));

            Assert.True(clear == query.GetProperty("clear").GetBoolean(), $"{version} scenario {index}: source={source}, box={box}, expected clear={query.GetProperty("clear")}, actual clear={clear}");
        }
    }

    private static JsonDocument ReadNative() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.Game.Tests", "Fixtures", "WorldBorderCollision.native.json")));
}
