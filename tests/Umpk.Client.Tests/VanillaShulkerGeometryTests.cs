using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class VanillaShulkerGeometryTests
{
    public static IEnumerable<object[]> NativeScenarios()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
        {
            JsonElement group = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()];
            for (int index = 0; index < group.GetArrayLength(); index++)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index];
        }
    }

    [Theory]
    [MemberData(nameof(NativeScenarios))]
    public void PeekMetadataDimensionsAndMovementMatchNativePhases(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        Assert.Equal(protocol, release.GetProperty("protocol").GetInt32());
        JsonElement scenario = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()][index];
        JsonElement input = scenario.GetProperty("input");
        var position = new Vec3d(input[0].GetDouble(), input[1].GetDouble(), input[2].GetDouble());
        var geometry = new VanillaShulkerGeometry(protocol, position, scenario.GetProperty("scale").GetSingle(), Face(scenario.GetProperty("face")));
        AssertState(scenario.GetProperty("initial"), geometry);
        foreach (JsonElement step in scenario.GetProperty("steps").EnumerateArray())
        {
            switch (step[0].GetInt32())
            {
                case 0:
                    geometry.ObservePeek((sbyte)step[1].GetInt32());
                    break;
                case 1:
                    Assert.Equal(step[1].GetBoolean(), geometry.TickPeek());
                    break;
                case 2:
                    geometry.SetPosition(new Vec3d(position.X + 2, position.Y + .7, position.Z - 3));
                    break;
                case 3:
                    geometry.ObservePeek(100);
                    for (int count = 0; count < step[1].GetInt32(); count++)
                        geometry.TickPeek();
                    break;
                case 4:
                    geometry.SetScale(step[1].GetSingle());
                    break;
                case 5:
                    geometry.SetAttachFace(Face(step[1]));
                    break;
                default:
                    throw new InvalidOperationException("Unknown native shulker operation.");
            }
            AssertState(step[2], geometry);
        }
    }

    private static Direction Face(JsonElement value) => Enum.Parse<Direction>(value.GetString()!, ignoreCase: true);

    private static void AssertState(JsonElement row, VanillaShulkerGeometry geometry)
    {
        Assert.Equal(new Vec3d(row[0].GetDouble(), row[1].GetDouble(), row[2].GetDouble()), geometry.Position);
        Assert.Equal(new Aabb(row[3].GetDouble(), row[4].GetDouble(), row[5].GetDouble(), row[6].GetDouble(), row[7].GetDouble(), row[8].GetDouble()), geometry.Bounds);
        Assert.Equal(row[9].GetSingle(), geometry.Peek);
        Assert.Equal(row[10].GetSingle(), geometry.PreviousPeek);
        Assert.Equal((sbyte)row[11].GetInt32(), geometry.RawPeek);
        Assert.Equal(row[12].GetSingle(), geometry.Width);
        Assert.Equal(row[13].GetSingle(), geometry.Height);
        Assert.Equal(row[14].GetSingle(), geometry.PhysicalPeek);
    }

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(VanillaShulkerGeometryTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.ShulkerGeometry.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
