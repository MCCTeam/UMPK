using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class VanillaEntityBoundsTests
{
    public static IEnumerable<object[]> NativeReleases
    {
        get
        {
            using JsonDocument document = ReadNative();
            foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
                yield return [row.GetProperty("version").GetString()!, row.GetProperty("protocol").GetInt32()];
        }
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public void DimensionRefreshAndPositionReapplicationMatchNative(string release, int protocol)
    {
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == release);
        JsonElement cases = document.RootElement.GetProperty("cases");
        foreach (JsonElement index in native.GetProperty("cases").EnumerateArray())
        {
            JsonElement row = cases[index.GetInt32()];
            JsonElement position = row.GetProperty("position"), from = row.GetProperty("from_size"), to = row.GetProperty("to_size");
            var point = new Vec3d(position[0].GetDouble(), position[1].GetDouble(), position[2].GetDouble());
            VanillaEntityBounds state = VanillaEntityBounds.Create(point, from[0].GetSingle(), from[1].GetSingle());
            Assert.Equal(Box(row.GetProperty("before")), state.Box);

            state = state.Refresh(to[0].GetSingle(), to[1].GetSingle(), protocol >= 755);
            Assert.Equal(Box(row.GetProperty("after_sync")), state.Box);
            state = state.Refresh(to[0].GetSingle(), to[1].GetSingle(), protocol >= 755);
            Assert.Equal(Box(row.GetProperty("after_refresh")), state.Box);
            state = state.AtPosition(point);
            Assert.Equal(Box(row.GetProperty("after_position")), state.Box);
            Assert.Equal(point, state.Position);
        }
    }

    private static Aabb Box(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble(), value[3].GetDouble(), value[4].GetDouble(), value[5].GetDouble());

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(VanillaEntityBoundsTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.EntityBounds.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
