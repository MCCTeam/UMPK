using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class VanillaEntityInterpolationTests
{
    public static IEnumerable<object[]> NativeEdgeReleases
    {
        get
        {
            using JsonDocument document = ReadNative(edges: true);
            foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
                yield return [row.GetProperty("version").GetString()!];
        }
    }

    public static IEnumerable<object[]> NativeReleases
    {
        get
        {
            using JsonDocument document = ReadNative();
            foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
                yield return [row.GetProperty("version").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public void EveryControllerTickMatchesItsNativeTrace(string release)
    {
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == release);
        VanillaInterpolationProfile profile = Profile(native);
        JsonElement cases = document.RootElement.GetProperty("cases");
        foreach (JsonElement index in native.GetProperty("cases").EnumerateArray())
            Replay(release, profile, cases[index.GetInt32()]);
    }

    [Theory]
    [MemberData(nameof(NativeEdgeReleases))]
    public void DefaultIntervalsDimensionChangesAndSustainedStreamsMatchNative(string release)
    {
        using JsonDocument document = ReadNative(edges: true);
        JsonElement native = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == release);
        VanillaInterpolationProfile profile = Profile(native);
        JsonElement cases = document.RootElement.GetProperty("cases");
        foreach (JsonElement index in native.GetProperty("cases").EnumerateArray())
        {
            JsonElement row = cases[index.GetInt32()], initial = row.GetProperty("initial");
            JsonElement pos = initial.GetProperty("position"), rot = initial.GetProperty("rotation");
            var controller = new VanillaEntityInterpolation(profile, new Vec3d(pos[0].GetDouble(), pos[1].GetDouble(), pos[2].GetDouble()), rot[0].GetSingle(), rot[1].GetSingle(), row.GetProperty("width").GetSingle(), row.GetProperty("height").GetSingle(), row.GetProperty("length").GetInt32());
            var queries = new List<Aabb>();
            bool Collision(Aabb box) { queries.Add(box); return true; }
            AssertState(controller, initial, $"{release} {row.GetProperty("name")} initial");
            foreach (JsonElement tick in row.GetProperty("ticks").EnumerateArray())
            {
                string context = $"{release} {row.GetProperty("name")} tick {tick.GetProperty("tick")}";
                queries.Clear();
                foreach (JsonElement operation in tick.GetProperty("operations").EnumerateArray())
                {
                    switch (operation.GetProperty("kind").GetString())
                    {
                        case "target":
                            JsonElement target = operation.GetProperty("position");
                            controller.SetTarget(new Vec3d(target[0].GetDouble(), target[1].GetDouble(), target[2].GetDouble()), operation.GetProperty("yaw").GetSingle(), operation.GetProperty("pitch").GetSingle());
                            break;
                        case "rotation":
                            controller.SetRotation(operation.GetProperty("yaw").GetSingle(), operation.GetProperty("pitch").GetSingle());
                            break;
                        case "dimensions":
                            controller.RefreshDimensions(operation.GetProperty("width").GetSingle(), operation.GetProperty("height").GetSingle(), profile != VanillaInterpolationProfile.LegacyDoubleRotation);
                            break;
                        default:
                            throw new InvalidOperationException("Unknown native input operation");
                    }
                }
                AssertState(controller, tick.GetProperty("before"), context + " before");
                controller.Tick(Collision);
                AssertState(controller, tick.GetProperty("after"), context + " after");
                AssertQueries(queries, tick.GetProperty("collision_queries"), context);
            }
        }
    }

    private static VanillaInterpolationProfile Profile(JsonElement native)
        => native.GetProperty("trace_group").GetString() switch
        {
            "1.14.4" => VanillaInterpolationProfile.LegacyDoubleRotation,
            "1.17" => VanillaInterpolationProfile.LegacyFloatRotation,
            "1.20.2" => VanillaInterpolationProfile.LinearStep,
            "1.21.5" => VanillaInterpolationProfile.HandlerOffsetBox,
            "1.21.6" => VanillaInterpolationProfile.Handler,
            "1.21.9" => VanillaInterpolationProfile.DeduplicatingHandler,
            "26.3" => VanillaInterpolationProfile.Stepped,
            _ => throw new InvalidOperationException("Unknown native trace group"),
        };

    private static void Replay(string release, VanillaInterpolationProfile profile, JsonElement native)
    {
        string name = native.GetProperty("name").GetString()!;
        int length = native.GetProperty("length").GetInt32();
        bool clear = native.GetProperty("collision_clear").GetBoolean();
        float tickSpeed = native.GetProperty("relative_tick_speed").GetSingle();
        double x = name == "world_border" ? 29999990.12345 : 1.5;
        double z = name == "world_border" ? -29999990.12345 : 1.5;
        const double y = 64.0;
        var controller = new VanillaEntityInterpolation(profile, new Vec3d(x, y, z), name == "wrap_rotation" ? 170.0F : 0.0F, 0.0F, 0.6F, 1.8F, length);
        var queries = new List<Aabb>();
        bool Collision(Aabb box) { queries.Add(box); return clear; }
        foreach (JsonElement tick in native.GetProperty("ticks").EnumerateArray())
        {
            int number = tick.GetProperty("tick").GetInt32();
            string context = $"{release} {name} tick {number}";
            queries.Clear();
            if (number == 0)
            {
                if (name is "stepped_path" or "queued_path") Path(controller, x, y, z, false);
                else controller.SetTarget(new Vec3d(x + 3, y + 3, z - 3), name == "wrap_rotation" ? -170.0F : 90.0F, 30.0F);
            }
            if (number == 1)
            {
                if (name == "repeat") controller.SetTarget(new Vec3d(x + 3, y + 3, z - 3), 90.0F, 30.0F);
                if (name == "retarget") controller.SetTarget(new Vec3d(x - 4, y + 1, z + 2), -90.0F, -15.0F);
                if (name == "queued_path") Path(controller, x + 3, y + 2, z - 2, true);
                if (name == "partial_position") controller.SetTarget(new Vec3d(x - 2, y + 1, z + 4), null, null);
                if (name == "partial_rotation") controller.SetTarget(null, -90.0F, -15.0F);
                if (name == "cancel") controller.Cancel();
                if (name.StartsWith("external_", StringComparison.Ordinal) || name.StartsWith("small_external_", StringComparison.Ordinal))
                {
                    double dx = name == "small_external_low" ? 0.003 : name == "small_external_high" ? 0.0032 : 0.25;
                    controller.SetPosition(controller.Position.Add(dx, 0, 0));
                }
                if (name.StartsWith("predicted_", StringComparison.Ordinal))
                {
                    double dx = name == "predicted_small" ? 0.003 : 0.25;
                    controller.ApplyPredictedMovement(new Vec3d(dx, 0, 0), Collision);
                    controller.SetPosition(controller.Position.Add(dx, 0, 0));
                }
            }
            if (number == 2 && name == "retarget") controller.SetTarget(new Vec3d(x + 6, y - 2, z - 1), 170.0F, 45.0F);
            AssertState(controller, tick.GetProperty("before"), context + " before");
            controller.Tick(Collision, tickSpeed);
            AssertState(controller, tick.GetProperty("after"), context + " after");
            AssertQueries(queries, tick.GetProperty("collision_queries"), context);
        }
    }

    private static void AssertQueries(List<Aabb> queries, JsonElement expected, string context)
    {
        Assert.True(expected.GetArrayLength() == queries.Count, $"{context} collision query count: expected {expected.GetArrayLength()}, actual {queries.Count}");
        for (int i = 0; i < queries.Count; i++)
            Assert.True(Box(expected[i]) == queries[i], $"{context} collision query {i}: expected {expected[i]}, actual {queries[i]}");
    }

    private static void Path(VanillaEntityInterpolation controller, double x, double y, double z, bool queued)
        => controller.SetPath([new(new Vec3d(x + 1, y, z), 1), new(new Vec3d(x + 1, y + 1, z), 0), new(new Vec3d(x + 3, y + 2, z - 2), 2)], queued ? -170.0F : 90.0F, 30.0F);

    private static void AssertState(VanillaEntityInterpolation controller, JsonElement native, string context)
    {
        AssertVector(controller.Position, native.GetProperty("position"), context + " position");
        Assert.True(controller.Yaw == native.GetProperty("rotation")[0].GetSingle(), $"{context} yaw: expected {native.GetProperty("rotation")[0]}, actual {controller.Yaw:R}");
        Assert.True(controller.Pitch == native.GetProperty("rotation")[1].GetSingle(), $"{context} pitch: expected {native.GetProperty("rotation")[1]}, actual {controller.Pitch:R}");
        Assert.True(controller.HasActiveInterpolation == native.GetProperty("active").GetBoolean(), $"{context} active state");
        Assert.True(controller.Bounds == Box(native.GetProperty("box")), $"{context} box: expected {native.GetProperty("box")}, actual {controller.Bounds}");
        JsonElement target = native.GetProperty("target");
        AssertVector(controller.TargetPosition, target, context + " target");
        Assert.True(controller.TargetYaw == target[3].GetSingle(), $"{context} target yaw: expected {target[3]}, actual {controller.TargetYaw:R}");
        Assert.True(controller.TargetPitch == target[4].GetSingle(), $"{context} target pitch: expected {target[4]}, actual {controller.TargetPitch:R}");
    }

    private static void AssertVector(Vec3d actual, JsonElement expected, string context)
    {
        for (int axis = 0; axis < 3; axis++)
            Assert.True(actual.Get(axis) == expected[axis].GetDouble(), $"{context} axis {axis}: expected {expected[axis]}, actual {actual.Get(axis):R}");
    }

    private static Aabb Box(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble(), value[3].GetDouble(), value[4].GetDouble(), value[5].GetDouble());

    private static JsonDocument ReadNative(bool edges = false)
    {
        using Stream stream = typeof(VanillaEntityInterpolationTests).Assembly.GetManifestResourceStream(edges ? "Umpk.Client.Tests.Fixtures.EntityInterpolationEdges.native.json" : "Umpk.Client.Tests.Fixtures.EntityInterpolation.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
