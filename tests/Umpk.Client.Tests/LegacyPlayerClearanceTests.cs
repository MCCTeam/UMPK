using System.IO.Compression;
using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyPlayerClearanceTests
{
    [Theory]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    public void NativeCompositeResizeQueriesMatchTheTrackedWorld(int protocol)
    {
        using JsonDocument document = ReadNative("LegacyCompositeClearance");
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("protocol").GetInt32() == protocol);
        var state = new ClientState(new ClientFeatures { Terrain = true }) { Registries = JavaGameData.Registries(protocol) };
        WorldFactory.InstallWorld(state, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        World world = state.World;
        Assert.True(world.BlockData.Blocks.TryGet(Identifier.Minecraft("stone"), out var stone));
        int stoneId = world.BlockData.GetDefaultStateId(stone.NetworkId);
        BlockPos[] cells = [.. release.GetProperty("cells").EnumerateArray().Select(cell => new BlockPos(cell[0].GetInt32(), cell[1].GetInt32(), cell[2].GetInt32()))];
        var query = new LegacyPlayerResizeClearance(world, JavaGameData.BlockShapes(protocol), protocol);
        List<int> failures = [];
        int index = 0;
        foreach (JsonElement row in release.GetProperty("rows").EnumerateArray())
        {
            int mask = row[1].GetInt32();
            for (int cell = 0; cell < cells.Length; cell++)
                world.SetBlockStateId(cells[cell], (mask & (1 << cell)) != 0 ? stoneId : 0);
            JsonElement bounds = row[0];
            var box = new Aabb(bounds[0].GetDouble(), bounds[1].GetDouble(), bounds[2].GetDouble(), bounds[3].GetDouble(), bounds[4].GetDouble(), bounds[5].GetDouble());
            if (query.NoCollision(box) != row[2].GetBoolean())
                failures.Add(index);
            index++;
        }
        Assert.Equal(4488, index);
        Assert.True(failures.Count == 0, $"Protocol {protocol}: {failures.Count}/{index} native composite query mismatches; first: {string.Join(", ", failures.Take(8))}");
    }

    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), release.GetProperty("case").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public void NativeResizeQueriesMatchTheTrackedWorld(string version, int protocol, int caseIndex)
    {
        using JsonDocument document = ReadNative();
        JsonElement cases = document.RootElement.GetProperty("cases")[caseIndex];
        var state = new ClientState(new ClientFeatures { Terrain = true }) { Registries = JavaGameData.Registries(protocol) };
        WorldFactory.InstallWorld(state, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        World world = state.World;
        Assert.True(world.BlockData.Blocks.TryGet(Identifier.Minecraft("stone"), out var stone));
        int stoneId = world.BlockData.GetDefaultStateId(stone.NetworkId);
        var query = new LegacyPlayerResizeClearance(world, JavaGameData.BlockShapes(protocol), protocol);
        List<int> failures = [];
        BlockPos? previous = null;
        int index = 0;
        foreach (JsonElement row in cases.EnumerateArray())
        {
            if (previous is { Y: >= 0 and < 256 } old)
                world.SetBlockStateId(old, 0);
            JsonElement occupied = row[1];
            previous = occupied.GetArrayLength() == 0 ? null : new BlockPos(occupied[0].GetInt32(), occupied[1].GetInt32(), occupied[2].GetInt32());
            if (previous is { Y: >= 0 and < 256 } cell)
                world.SetBlockStateId(cell, stoneId);
            JsonElement coordinates = row[0];
            var box = new Aabb(coordinates[0].GetDouble(), coordinates[1].GetDouble(), coordinates[2].GetDouble(), coordinates[3].GetDouble(), coordinates[4].GetDouble(), coordinates[5].GetDouble());
            bool clear = query.NoCollision(box);
            if (clear != row[2].GetBoolean())
                failures.Add(index);
            index++;
        }
        Assert.True(failures.Count == 0, $"{version}: {failures.Count}/{index} native query mismatches; first: {string.Join(", ", failures.Take(8))}");
    }

    private static JsonDocument ReadNative(string name = "LegacyPlayerClearance")
    {
        using Stream stream = typeof(LegacyPlayerClearanceTests).Assembly.GetManifestResourceStream($"Umpk.Client.Tests.Fixtures.{name}.native.json.gz")!;
        using var decompressed = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(decompressed);
    }
}
