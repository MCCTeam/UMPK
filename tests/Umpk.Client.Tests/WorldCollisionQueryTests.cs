using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class WorldCollisionQueryTests
{
    public static IEnumerable<object[]> NativeScenarios()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
        {
            JsonElement cases = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()];
            for (int index = 0; index < cases.GetArrayLength(); index++)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index];
        }
    }

    [Theory]
    [MemberData(nameof(NativeScenarios))]
    public void NativeBorderQueriesMatchThroughTheTrackedWorldAdapter(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        Assert.Equal(protocol, release.GetProperty("protocol").GetInt32());
        JsonElement scenario = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()][index];
        World world = CreateWorld(protocol);
        double size = scenario.GetProperty("size").GetDouble();
        world.SetBorder(new WorldBorderState(scenario.GetProperty("center_x").GetDouble(), scenario.GetProperty("center_z").GetDouble(), size, size, 0, 5, 15).WithAbsoluteMaxSize(scenario.GetProperty("absolute").GetInt32()));
        var query = new PhysicsCollisionQuery(new WorldPhysicsView(world, JavaGameData.BlockShapes(protocol), WorldBorderState.ContainmentEraForProtocol(protocol)));
        int exercised = 0;
        foreach (JsonElement row in scenario.GetProperty("queries").EnumerateArray())
        {
            if (!row.TryGetProperty("source", out JsonElement source))
                continue;
            JsonElement coordinates = row.GetProperty("box");
            var box = new Aabb(coordinates[0].GetDouble(), coordinates[1].GetDouble(), coordinates[2].GetDouble(), coordinates[3].GetDouble(), coordinates[4].GetDouble(), coordinates[5].GetDouble());
            var position = new Vec3d(source[0].GetDouble(), source[1].GetDouble(), source[2].GetDouble());
            bool clear = query.NoCollision(position, box);
            Assert.True(clear == row.GetProperty("clear").GetBoolean(), $"{version} scenario {index} query {exercised}: expected clear={row.GetProperty("clear")}, actual clear={clear}");
            exercised++;
        }
        Assert.Equal(736, exercised);
    }

    [Fact]
    public void BorderReplacementIsVisibleToAnExistingQuery()
    {
        const int protocol = 777;
        World world = CreateWorld(protocol);
        world.SetBorder(new WorldBorderState(0, 0, 10, 10, 0, 5, 15));
        var query = new PhysicsCollisionQuery(new WorldPhysicsView(world, JavaGameData.BlockShapes(protocol), WorldBorderState.ContainmentEraForProtocol(protocol)));
        var source = new Vec3d(4.9, 65, 0);
        var box = new Aabb(4.8, 64, 0, 5.8, 66, .6);
        Assert.False(query.NoCollision(source, box));
        world.SetBorder(world.Border.WithSize(20));
        Assert.True(query.NoCollision(source, box));
        world.SetBorder(world.Border.WithSize(10));
        Assert.False(query.NoCollision(source, box));
    }

    public static IEnumerable<object[]> BorderSources() =>
        from protocol in new[] { 765, 766, 767, 770, 774, 777 }
        from input in new[] { (X: 0d, Clear: true), (X: 4.9d, Clear: false), (X: 5.1d, Clear: false), (X: 10d, Clear: true) }
        select new object[] { protocol, input.X, input.Clear };

    [Theory]
    [MemberData(nameof(BorderSources))]
    public void TrackedBorderUsesTheCurrentSourceRatherThanTheProposedBox(int protocol, double sourceX, bool clear)
    {
        World world = CreateWorld(protocol);
        world.SetBorder(new WorldBorderState(0, 0, 10, 10, 0, 5, 15));
        var query = new PhysicsCollisionQuery(new WorldPhysicsView(world, JavaGameData.BlockShapes(protocol), WorldBorderState.ContainmentEraForProtocol(protocol)));
        var box = new Aabb(4.8, 64, 0, 5.8, 66, .6);

        Assert.Equal(clear, query.NoCollision(new Vec3d(sourceX, 65, 0), box));
    }

    private static World CreateWorld(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var harness = new ApplierHarness(version);
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol));
        return harness.State.World;
    }

    private static JsonDocument ReadNative() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.Game.Tests", "Fixtures", "WorldBorderCollision.native.json")));
}
