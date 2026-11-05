using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Data.Java;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class WorldOffsetCollisionNativeTests
{
    [Theory]
    [InlineData(498, 84)]
    [InlineData(735, 84)]
    [InlineData(755, 224)]
    [InlineData(777, 364)]
    public void OriginalDelegatesAndPositionModelsReproduceNativeShapes(int protocol, int expectedCount)
    {
        using JsonDocument fixture = ReadFixture();
        JsonElement release = fixture.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("protocol").GetInt32() == protocol);
        var data = new RegistryBlockDataSource(JavaGameData.Registries(protocol).Blocks, isLegacy: false);
        var shapes = Assert.IsAssignableFrom<Umpk.Game.Registries.IBlockCollisionGridSource>(JavaGameData.BlockShapes(protocol));
        int count = 0;
        foreach (JsonElement observation in release.GetProperty("observations").EnumerateArray())
        {
            var state = new Umpk.Game.Blocks.BlockState(data, observation.GetProperty("state_id").GetInt32());
            Umpk.Game.Blocks.BlockCollisionGrid grid = Assert.IsType<Umpk.Game.Blocks.BlockCollisionGrid>(shapes.GetCollisionGrid(state));
            JsonElement at = observation.GetProperty("block");
            Vec3d offset = grid.OffsetAt(new BlockPos(at[0].GetInt32(), at[1].GetInt32(), at[2].GetInt32()));
            JsonElement expectedOffset = observation.GetProperty("offset");
            Assert.Equal(expectedOffset[0].GetDouble(), offset.X);
            Assert.Equal(expectedOffset[1].GetDouble(), offset.Y);
            Assert.Equal(expectedOffset[2].GetDouble(), offset.Z);
            JsonElement measured = observation.GetProperty("grid");
            for (int axis = 0; axis < 3; axis++)
            {
                ReadOnlySpan<double> coordinates = axis == 0 ? grid.XCoordinates : axis == 1 ? grid.YCoordinates : grid.ZCoordinates;
                JsonElement expected = measured.GetProperty("planes")[axis];
                Assert.Equal(expected.GetArrayLength(), coordinates.Length);
                for (int index = 0; index < coordinates.Length; index++)
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expected[index].GetDouble()), BitConverter.DoubleToInt64Bits(coordinates[index] + offset.Get(axis)));
            }
            Assert.Equal(measured.GetProperty("cells").EnumerateArray().Select(value => value.GetInt32()), grid.OccupiedCells.ToArray());
            count++;
        }
        Assert.Equal(expectedCount, count);
    }

    [Theory]
    [InlineData(498, 294)]
    [InlineData(735, 294)]
    [InlineData(755, 4858)]
    [InlineData(777, 9422)]
    public void PositionDependentCollisionQueriesMatchNative(int protocol, int expectedCount)
    {
        using JsonDocument fixture = ReadFixture();
        JsonElement release = fixture.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("protocol").GetInt32() == protocol);
        var client = new ClientState(new ClientFeatures { Terrain = true });
        client.Registries = JavaGameData.Registries(protocol);
        WorldFactory.InstallWorld(client, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        World world = client.World;
        var query = new PhysicsCollisionQuery(new WorldPhysicsView(world, JavaGameData.BlockShapes(protocol), WorldBorderState.ContainmentEraForProtocol(protocol), PhysicsProfile.BlockCollisionQueryForProtocol(protocol)));
        var failures = new List<string>();
        int count = 0;
        foreach (JsonElement row in release.GetProperty("cases").EnumerateArray())
        {
            var position = new BlockPos(row[2][0].GetInt32(), row[2][1].GetInt32(), row[2][2].GetInt32());
            world.LoadColumn(ChunkPos.Containing(position));
            world.SetBlockStateId(position, row[1].GetInt32());
            JsonElement values = row[3];
            var box = new Aabb(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble(), values[3].GetDouble(), values[4].GetDouble(), values[5].GetDouble());
            bool actual = query.NoCollision(new Vec3d(.5, 65, .5), box);
            if (actual != row[4].GetBoolean())
                failures.Add(row[0].GetString()!);
            world.SetBlockStateId(position, 0);
            count++;
        }
        Assert.Equal(expectedCount, count);
        Assert.True(failures.Count == 0, $"Protocol {protocol}: {failures.Count}/{count} mismatches; first: {string.Join(", ", failures.Take(8))}");
    }

    private static JsonDocument ReadFixture()
    {
        using Stream stream = typeof(WorldOffsetCollisionNativeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.OffsetCollision.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
