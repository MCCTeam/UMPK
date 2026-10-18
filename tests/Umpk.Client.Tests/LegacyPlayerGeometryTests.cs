using System.IO.Compression;
using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyPlayerGeometryTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        foreach (string resource in new[] { "LegacyPlayerLifecycle", "LegacyPlayerSizePhase" })
        {
            using JsonDocument document = ReadNative(resource);
            foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), release.GetProperty("case").GetInt32(), resource];
        }
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public void ExplicitCallbacksAndSizePhasesMatchTheOriginalClient(string version, int protocol, int caseIndex, string resource)
    {
        Assert.True(JavaVersions.TryGetByName(version, out JavaVersion? release));
        Assert.Equal(protocol, release.Version.Protocol);
        using JsonDocument document = ReadNative(resource);
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        var state = new ClientState(new ClientFeatures { Terrain = true }) { Registries = JavaGameData.Registries(protocol) };
        WorldFactory.InstallWorld(state, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        World world = state.World;
        LegacyPlayerResizeClearance? clearance = protocol == 47 ? null : new LegacyPlayerResizeClearance(world, JavaGameData.BlockShapes(protocol), protocol);
        if (resource == "LegacyPlayerLifecycle")
            ReplayLifecycle(native, protocol, world, clearance);
        else
            ReplaySizePhases(native, protocol, world, clearance!);
    }

    private static void ReplayLifecycle(JsonElement native, int protocol, World world, LegacyPlayerResizeClearance? clearance)
    {
        Identifier bedName = Identifier.Minecraft(protocol < 393 ? "bed" : "red_bed");
        Assert.True(world.BlockData.Blocks.TryGet(bedName, out var bed));
        int bedState = world.BlockData.GetDefaultStateId(bed.NetworkId);
        int snapshots = 0;
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            var geometry = new VanillaLegacyPlayerGeometry(protocol, Point(native.GetProperty("positions")[row.GetProperty("position").GetInt32()]));
            BlockPos cell = Cell(row.GetProperty("bed"));
            geometry.ObserveFlags(row.GetProperty("flags").GetByte());
            geometry.ApplySizePhase(Clear);
            foreach (JsonElement step in row.GetProperty("transitions").EnumerateArray())
            {
                switch (step.GetProperty("operation").GetString())
                {
                    case "sleep":
                        world.SetBlockStateId(cell, bedState);
                        JsonElement facing = native.GetProperty("facings")[row.GetProperty("facing").GetInt32()];
                        geometry.BeginSleep(cell, Facing(facing));
                        break;
                    case "sleep_size_phase" or "death_size_phase" or "wake_size_phase":
                        geometry.ApplySizePhase(Clear);
                        break;
                    case "death_event":
                        geometry.HandleDeath();
                        break;
                    case "wake_after_bed_removal":
                        world.SetBlockStateId(cell, 0);
                        geometry.WakeAfterBedRemoval();
                        break;
                    case "explicit_position":
                        geometry.SetPosition(geometry.Position);
                        break;
                }
                Assert.Equal(Point(step.GetProperty("position")), geometry.Position);
                Assert.Equal(step.GetProperty("size")[0].GetSingle(), geometry.Width);
                Assert.Equal(step.GetProperty("size")[1].GetSingle(), geometry.Height);
                Assert.Equal(Box(step.GetProperty("box")), geometry.Bounds);
                Assert.Equal(step.GetProperty("sleeping").GetBoolean(), geometry.Sleeping);
                Assert.Equal(step.GetProperty("flags").GetByte(), geometry.SharedFlags);
                snapshots++;
            }
            world.SetBlockStateId(cell, 0);
        }
        Assert.Equal(protocol == 47 ? 576 : 1728, snapshots);
        return;

        bool Clear(Aabb box)
        {
            Assert.NotNull(clearance);
            return clearance.NoCollision(box);
        }
    }

    private static void ReplaySizePhases(JsonElement native, int protocol, World world, LegacyPlayerResizeClearance clearance)
    {
        Assert.True(world.BlockData.Blocks.TryGet(Identifier.Minecraft("stone"), out var stone));
        int stoneState = world.BlockData.GetDefaultStateId(stone.NetworkId);
        int cases = 0;
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            Vec3d point = Point(native.GetProperty("positions")[row.GetProperty("position").GetInt32()]);
            var geometry = new VanillaLegacyPlayerGeometry(protocol, point);
            if (row.GetProperty("sleeping").GetBoolean())
                geometry.BeginSleep(new BlockPos(0, 63, 0), Direction.North);
            JsonElement before = row.GetProperty("before_size");
            geometry.Resize(before[0].GetSingle(), before[1].GetSingle());
            geometry.SetPosition(point);
            Assert.Equal(Box(row.GetProperty("before")), geometry.Bounds);
            geometry.ObserveFlags(row.GetProperty("flags").GetByte(), row.GetProperty("spin").GetBoolean());
            JsonElement occupied = row.GetProperty("stone");
            BlockPos? cell = occupied.GetArrayLength() == 0 ? null : Cell(occupied);
            if (cell is { } position)
                world.SetBlockStateId(position, stoneState);
            int queries = 0;
            geometry.ApplySizePhase(box =>
            {
                queries++;
                Assert.Equal(Box(row.GetProperty("query_box")), box);
                bool clear = clearance.NoCollision(box);
                Assert.Equal(protocol >= 393 ? row.GetProperty("query_result").GetBoolean() : !row.GetProperty("query_result").GetBoolean(), clear);
                return clear;
            });
            Assert.Equal(row.GetProperty("query_count").GetInt32(), queries);
            Assert.Equal(row.GetProperty("after_size")[0].GetSingle(), geometry.Width);
            Assert.Equal(row.GetProperty("after_size")[1].GetSingle(), geometry.Height);
            Assert.Equal(Box(row.GetProperty("after_phase")), geometry.Bounds);
            geometry.SetPosition(point);
            Assert.Equal(Box(row.GetProperty("after_position")), geometry.Bounds);
            if (cell is { } previous)
                world.SetBlockStateId(previous, 0);
            cases++;
        }
        Assert.Equal(protocol >= 393 ? 3072 : 768, cases);
    }

    private static Direction Facing(JsonElement value) => (value[0].GetInt32(), value[1].GetInt32()) switch
    {
        (0, -1) => Direction.North,
        (0, 1) => Direction.South,
        (-1, 0) => Direction.West,
        (1, 0) => Direction.East,
        _ => throw new InvalidOperationException("The native bed facing is not horizontal."),
    };

    private static Vec3d Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static BlockPos Cell(JsonElement value) => new(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32());
    private static Aabb Box(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble(), value[3].GetDouble(), value[4].GetDouble(), value[5].GetDouble());

    private static JsonDocument ReadNative(string resource)
    {
        using Stream stream = typeof(LegacyPlayerGeometryTests).Assembly.GetManifestResourceStream($"Umpk.Client.Tests.Fixtures.{resource}.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
