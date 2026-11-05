using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Data.Java;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class WorldCollisionGridNativeTests
{
    [Theory]
    [InlineData(498)]
    [InlineData(735)]
    [InlineData(755)]
    [InlineData(777)]
    public void GeneratedGridsReachTheTrackedWorldCollisionQuery(int protocol)
    {
        using Stream stream = typeof(WorldCollisionGridNativeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.BlockCollisionGrid.native.json")!;
        using JsonDocument fixture = JsonDocument.Parse(stream);
        JsonElement release = fixture.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("protocol").GetInt32() == protocol);
        int[] states = [.. release.GetProperty("shapes").EnumerateArray().Select(row => row.GetProperty("state_id").GetInt32())];
        var state = new ClientState(new ClientFeatures { Terrain = true });
        state.Registries = JavaGameData.Registries(protocol);
        WorldFactory.InstallWorld(state, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        World world = state.World;
        var query = new PhysicsCollisionQuery(new WorldPhysicsView(world, JavaGameData.BlockShapes(protocol), WorldBorderState.ContainmentEraForProtocol(protocol), PhysicsProfile.BlockCollisionQueryForProtocol(protocol)));
        var failures = new List<string>();
        int count = 0;
        foreach (JsonElement row in release.GetProperty("cases").EnumerateArray())
        {
            var position = new BlockPos(row[2][0].GetInt32(), row[2][1].GetInt32(), row[2][2].GetInt32());
            world.LoadColumn(ChunkPos.Containing(position));
            world.SetBlockStateId(position, states[row[1].GetInt32()]);
            JsonElement values = row[3];
            var box = new Aabb(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble(), values[3].GetDouble(), values[4].GetDouble(), values[5].GetDouble());
            bool actual = query.NoCollision(new Vec3d(.5, 65, .5), box);
            if (actual != row[4].GetBoolean())
                failures.Add(row[0].GetString()!);
            world.SetBlockStateId(position, 0);
            count++;
        }
        Assert.Equal(2838, count);
        Assert.True(failures.Count == 0, $"Protocol {protocol}: {failures.Count}/{count} mismatches; first: {string.Join(", ", failures.Take(8))}");
    }
}
