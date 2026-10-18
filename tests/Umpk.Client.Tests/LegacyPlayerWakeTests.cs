using System.IO.Compression;
using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyPlayerWakeTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument fixture = ReadNative();
        foreach (JsonElement release in fixture.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), release.GetProperty("case").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public void WakeGeometryAndExitOrderMatchEveryOriginalClient(string version, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(version, out JavaVersion? release));
        Assert.Equal(protocol, release.Version.Protocol);
        using JsonDocument fixture = ReadNative();
        JsonElement native = fixture.RootElement.GetProperty("cases")[caseIndex];
        int count = 0;
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            JsonElement original = native.GetProperty("positions")[row.GetProperty("position").GetInt32()];
            var geometry = new VanillaLegacyPlayerGeometry(protocol, Point(original));
            BlockPos bed = Cell(row.GetProperty("bed"));
            JsonElement vector = native.GetProperty("facings")[row.GetProperty("facing").GetInt32()];
            int dx = vector[0].GetInt32(), dz = vector[1].GetInt32();
            Direction facing = (dx, dz) switch { (0, -1) => Direction.North, (0, 1) => Direction.South, (-1, 0) => Direction.West, (1, 0) => Direction.East, _ => throw new InvalidOperationException() };
            geometry.ObserveFlags(row.GetProperty("flags").GetByte());
            geometry.BeginSleep(bed, facing);
            if (row.GetProperty("sequence").GetInt32() == 2)
                geometry.HandleDeath();
            int scene = row.GetProperty("scene").GetInt32();
            List<BlockPos> queries = [];
            BlockPos? exit = geometry.WakeFromIntactBed(bed, facing, CanStand);
            JsonElement expected = row.GetProperty("exit");
            Assert.Equal(expected.ValueKind == JsonValueKind.Null ? (BlockPos?)null : Cell(expected), exit);
            Assert.Equal(row.GetProperty("candidates").EnumerateArray().Select(Cell), queries);
            Assert.Equal(Point(row.GetProperty("wake_position")), geometry.Position);
            JsonElement box = row.GetProperty("box");
            Assert.Equal(new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()), geometry.Bounds);
            Assert.Equal(row.GetProperty("size")[0].GetSingle(), geometry.Width);
            Assert.Equal(row.GetProperty("size")[1].GetSingle(), geometry.Height);
            Assert.False(geometry.Sleeping);
            count++;

            bool CanStand(BlockPos cell)
            {
                Assert.Equal(.6F, geometry.Width);
                Assert.Equal(1.8F, geometry.Height);
                Assert.True(geometry.Sleeping);
                queries.Add(cell);
                // These controlled stone/air/material inputs are independently checked against the original native predicate. Direct block reads retain absolute bounds only through 1.9.2.
                if (protocol < 110 && (cell.X is < -30_000_000 or >= 30_000_000 || cell.Z is < -30_000_000 or >= 30_000_000))
                    return false;
                bool support = scene is 1 or 2 or 4 || (scene == 3 && cell.X == bed.X - 2 * dx && cell.Z == bed.Z - 2 * dz);
                bool blocked = cell == bed || scene == 2 || (scene == 4 && cell.X == bed.X - 1 && cell.Z == bed.Z - 1);
                return support && !blocked;
            }
        }
        Assert.Equal(protocol == 47 ? 480 : 960, count);
    }

    private static BlockPos Cell(JsonElement value) => new(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32());
    private static Vec3d Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static JsonDocument ReadNative()
    {
        using Stream source = typeof(LegacyPlayerWakeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyPlayerWake.native.json.gz")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
