using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyTeleportInterpolationTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), release.GetProperty("group").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public void TeleportTargetsAndCancellationMatchNativePlayerStates(string release, int protocol, int group)
    {
        using JsonDocument document = ReadNative();
        Assert.Equal(protocol, document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == release).GetProperty("protocol").GetInt32());
        JsonElement cases = document.RootElement.GetProperty("groups")[group];
        Assert.Equal(18, cases.GetArrayLength());
        foreach (JsonElement row in cases.EnumerateArray())
        {
            var position = VanillaEntityInterpolation.CreateRemotePlayer(protocol, new Vec3d(1.5, 64, 1.5), 170, -25, 0.6F, 1.8F);
            int phase = row.GetProperty("phase").GetInt32();
            if (phase > 0)
            {
                position.SetTarget(new Vec3d(4.5, 67, -1.5), 90, 30);
                if (phase == 2)
                    position.Tick(static _ => true);
            }
            AssertState(position, row.GetProperty("before"));

            if (row.GetProperty("steps").GetInt32() == 0)
                position.Cancel();
            else
                position.SetTarget(Vector(row.GetProperty("destination")), -90, -45);

            AssertState(position, row.GetProperty("after_setter"));
            foreach (JsonElement tick in row.GetProperty("ticks").EnumerateArray())
            {
                AssertState(position, tick.GetProperty("before"));
                position.Tick(static _ => true);
                AssertState(position, tick.GetProperty("after"));
            }
        }
    }

    private static void AssertState(VanillaEntityInterpolation position, JsonElement state)
    {
        AssertVector(position.Position, state.GetProperty("position"));
        JsonElement rotation = state.GetProperty("rotation");
        AssertFloat(position.Yaw, rotation[0]);
        AssertFloat(position.Pitch, rotation[1]);
        Assert.Equal(state.GetProperty("active").GetBoolean(), position.HasActiveInterpolation);
        JsonElement target = state.GetProperty("target");
        AssertVector(position.TargetPosition, target);
        AssertFloat(position.TargetYaw, target[3]);
        AssertFloat(position.TargetPitch, target[4]);
        JsonElement box = state.GetProperty("box");
        double[] actual = [position.Bounds.MinX, position.Bounds.MinY, position.Bounds.MinZ, position.Bounds.MaxX, position.Bounds.MaxY, position.Bounds.MaxZ];
        for (int axis = 0; axis < actual.Length; axis++)
            Assert.Equal(BitConverter.DoubleToInt64Bits(box[axis].GetDouble()), BitConverter.DoubleToInt64Bits(actual[axis]));
    }

    private static void AssertVector(Vec3d actual, JsonElement expected)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected[0].GetDouble()), BitConverter.DoubleToInt64Bits(actual.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected[1].GetDouble()), BitConverter.DoubleToInt64Bits(actual.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected[2].GetDouble()), BitConverter.DoubleToInt64Bits(actual.Z));
    }

    private static void AssertFloat(float actual, JsonElement expected)
        => Assert.Equal(BitConverter.SingleToInt32Bits(expected.GetSingle()), BitConverter.SingleToInt32Bits(actual));

    private static Vec3d Vector(JsonElement row) => new(row[0].GetDouble(), row[1].GetDouble(), row[2].GetDouble());

    private static JsonDocument ReadNative()
    {
        using Stream source = typeof(LegacyTeleportInterpolationTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyTeleportInterpolation.native.json")
            ?? throw new InvalidOperationException("The native legacy teleport fixture is missing.");
        return JsonDocument.Parse(source);
    }
}
