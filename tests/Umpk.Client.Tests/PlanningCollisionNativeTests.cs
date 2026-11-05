using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Goals;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class PlanningCollisionNativeTests
{
    [Theory]
    [InlineData(47, BlockCollisionQueryEra.BoundingBoxes)]
    [InlineData(340, BlockCollisionQueryEra.BoundingBoxes)]
    [InlineData(393, BlockCollisionQueryEra.QueryFirstStrict)]
    [InlineData(401, BlockCollisionQueryEra.QueryFirstStrict)]
    [InlineData(404, BlockCollisionQueryEra.QueryFirstStrict)]
    [InlineData(477, BlockCollisionQueryEra.QueryFirst)]
    [InlineData(490, BlockCollisionQueryEra.QueryFirst)]
    [InlineData(498, BlockCollisionQueryEra.QueryFirst)]
    [InlineData(735, BlockCollisionQueryEra.BlockFirst)]
    [InlineData(755, BlockCollisionQueryEra.Modern)]
    [InlineData(777, BlockCollisionQueryEra.Modern)]
    public void PlanCapturesUseTheSessionsCollisionEra(int protocol, BlockCollisionQueryEra expected)
    {
        (PhysicsEngineHolder holder, ClientState state) = CreateHolder(protocol);
        state.Self.Position = new Vec3d(.5, 65, .5);
        PlanCapture capture = Assert.IsType<PlanCapture>(holder.CapturePlan(new GoalBlock(new BlockPos(0, 65, 0))));

        Assert.Equal(expected, ((IPhysicsWorldView)capture.Planning).CollisionQueryEra);
    }

    [Theory]
    [InlineData(498, false, 2838)]
    [InlineData(735, false, 2838)]
    [InlineData(755, false, 2838)]
    [InlineData(777, false, 2838)]
    [InlineData(498, true, 294)]
    [InlineData(735, true, 294)]
    [InlineData(755, true, 4858)]
    [InlineData(777, true, 9422)]
    public void RuntimePlanCapturesPreserveNativeCollisionAnswers(int protocol, bool offsets, int expectedCount)
    {
        using JsonDocument fixture = ReadFixture(offsets);
        JsonElement release = fixture.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("protocol").GetInt32() == protocol);
        int[] states = offsets ? [] : [.. release.GetProperty("shapes").EnumerateArray().Select(row => row.GetProperty("state_id").GetInt32())];
        (PhysicsEngineHolder holder, ClientState state) = CreateHolder(protocol);
        World world = state.World;
        var failures = new List<string>();
        int count = 0;
        foreach (JsonElement row in release.GetProperty("cases").EnumerateArray())
        {
            var position = new BlockPos(row[2][0].GetInt32(), row[2][1].GetInt32(), row[2][2].GetInt32());
            if (world.GetColumn(position) is null)
                world.LoadColumn(ChunkPos.Containing(position));
            world.SetBlockStateId(position, offsets ? row[1].GetInt32() : states[row[1].GetInt32()]);
            state.Self.Position = new Vec3d(position.X + .5, position.Y + 1, position.Z + .5);
            PlanCapture capture = Assert.IsType<PlanCapture>(holder.CapturePlan(new GoalBlock(position)));
            Assert.True(capture.Planning.Region.IsDemandFaulted);
            Assert.Equal(0, capture.Planning.Region.SectionsCaptured);
            JsonElement values = row[3];
            var box = new Aabb(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble(), values[3].GetDouble(), values[4].GetDouble(), values[5].GetDouble());
            bool actual = new PhysicsCollisionQuery(capture.Planning).NoCollision(new Vec3d(.5, 65, .5), box);
            if (actual != row[4].GetBoolean())
                failures.Add(row[0].GetString()!);
            world.SetBlockStateId(position, 0);
            count++;
        }
        Assert.Equal(expectedCount, count);
        Assert.True(failures.Count == 0, $"Protocol {protocol}, offsets={offsets}: {failures.Count}/{count} mismatches; first: {string.Join(", ", failures.Take(8))}");
    }

    [Theory]
    [InlineData(776)]
    [InlineData(777)]
    public void PlanningOffsetInventoryIncludesNativeSulfurSpikes(int protocol)
    {
        (PhysicsEngineHolder holder, ClientState state) = CreateHolder(protocol);
        Assert.True(JavaGameData.Registries(protocol).Blocks.TryGetValue(Identifier.Minecraft("sulfur_spike"), out BlockDefinition? block));
        var position = new BlockPos(0, 64, 0);
        state.World.LoadColumn(ChunkPos.Containing(position));
        state.World.SetBlockStateId(position, block.DefaultStateId);
        state.Self.Position = new Vec3d(.5, 65, .5);
        PlanCapture capture = Assert.IsType<PlanCapture>(holder.CapturePlan(new GoalBlock(position)));

        Assert.True(capture.Planning.MayContainShapeOffset);
    }

    private static (PhysicsEngineHolder Holder, ClientState State) CreateHolder(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var state = new ClientState(new ClientFeatures { Physics = true, Pathfinding = true, Terrain = true });
        state.Registries = JavaGameData.Registries(protocol);
        WorldFactory.InstallWorld(state, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = new ChannelSessionScheduler(),
        };
        return (new PhysicsEngineHolder(services, JavaGameData.BlockShapes(protocol), NullLogger.Instance), state);
    }

    private static JsonDocument ReadFixture(bool offsets)
    {
        string name = offsets ? "OffsetCollision" : "BlockCollisionGrid";
        using Stream stream = typeof(PlanningCollisionNativeTests).Assembly.GetManifestResourceStream($"Umpk.Client.Tests.Fixtures.{name}.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
