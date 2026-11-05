using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class EntityTeleportControllerReplayTests
{
    public static IEnumerable<object[]> Releases
    {
        get
        {
            using JsonDocument document = ReadNative();
            foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
                yield return [row.GetProperty("version").GetString()!];
        }
    }

    [Fact]
    public void OracleCoversEveryModernTeleportRelease()
    {
        using Stream stream = typeof(EntityTeleportControllerReplayTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.EntityPacketPosition.native.json")!;
        using JsonDocument catalogue = JsonDocument.Parse(stream);
        using JsonDocument document = ReadNative();
        string[] expected = catalogue.RootElement.GetProperty("releases").EnumerateArray().Where(row => row.GetProperty("protocol").GetInt32() >= 768).Select(row => row.GetProperty("version").GetString()!).Order(StringComparer.Ordinal).ToArray();
        string[] actual = document.RootElement.GetProperty("releases").EnumerateArray().Select(row => row.GetProperty("version").GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(15, expected.Length);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void RemotePlayerFactoryPreservesNativeCurrentPose(string release)
    {
        using JsonDocument document = ReadNative();
        JsonElement native = Release(document, release), cases = document.RootElement.GetProperty("cases"), states = document.RootElement.GetProperty("states");
        foreach (JsonElement index in native.GetProperty("cases").EnumerateArray())
        {
            JsonElement row = cases[index.GetInt32()];
            if (row.GetProperty("phase").GetInt32() != 0 || row.GetProperty("interpolate").GetBoolean())
                continue;
            JsonElement pose = states[row.GetProperty("after").GetInt32()];

            var position = VanillaEntityInterpolation.CreateRemotePlayer(native.GetProperty("protocol").GetInt32(), Vector(pose), pose[6].GetSingle(), pose[7].GetSingle(), 0.6F, 1.8F);

            AssertVector(Vector(pose), position.Position);
            Assert.Equal(BitConverter.SingleToInt32Bits(pose[6].GetSingle()), BitConverter.SingleToInt32Bits(position.Yaw));
            Assert.Equal(BitConverter.SingleToInt32Bits(pose[7].GetSingle()), BitConverter.SingleToInt32Bits(position.Pitch));
            AssertBox(pose, position.Bounds, 8);
            Assert.False(position.HasActiveInterpolation);
        }
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void TeleportCoreAndEveryFollowingTickMatchNative(string release)
    {
        using JsonDocument document = ReadNative();
        JsonElement native = Release(document, release), cases = document.RootElement.GetProperty("cases"), states = document.RootElement.GetProperty("states");
        int protocol = native.GetProperty("protocol").GetInt32();
        Assert.Equal(80, native.GetProperty("cases").GetArrayLength());
        foreach (JsonElement index in native.GetProperty("cases").EnumerateArray())
        {
            JsonElement row = cases[index.GetInt32()], initial = states[row.GetProperty("initial").GetInt32()];
            var position = VanillaEntityInterpolation.CreateRemotePlayer(protocol, Vector(initial), initial[6].GetSingle(), initial[7].GetSingle(), 0.6F, 1.8F);
            var velocity = new VanillaEntityVelocity(3);
            velocity.SetCurrent(Vector(initial, 3));
            velocity.Observe(Vector(document.RootElement.GetProperty("initial_motion_target")));
            List<Aabb> queries = [];
            bool Collision(Aabb box) { queries.Add(box); return row.GetProperty("collision_clear").GetBoolean(); }
            AssertState(position, velocity, initial);
            if (row.GetProperty("phase").GetInt32() != 0)
                position.SetTarget(new Vec3d(40.5, 67, -1.5), -170, 35);
            if (row.GetProperty("phase").GetInt32() == 2)
            {
                position.Tick(Collision);
                velocity.Tick();
            }
            AssertState(position, velocity, states[row.GetProperty("before").GetInt32()]);
            EntityTeleportProfile profile = EntityTeleportProfile.ForProtocol(protocol);
            PositionMoveRotation source = profile.GetSource(position, velocity.Value);
            AssertValues(row.GetProperty("source"), source);

            bool interpolated = profile.Apply(position, velocity, velocity.Value, Values(row.GetProperty("change")), row.GetProperty("flags").GetInt32(), row.GetProperty("interpolate").GetBoolean());

            Assert.Equal(row.GetProperty("was_interpolated").GetBoolean(), interpolated);
            Assert.Empty(row.GetProperty("core_collision_queries").EnumerateArray());
            AssertState(position, velocity, states[row.GetProperty("after").GetInt32()]);
            foreach (JsonElement tick in row.GetProperty("ticks").EnumerateArray())
            {
                queries.Clear();
                position.Tick(Collision);
                velocity.Tick();
                AssertState(position, velocity, states[tick.GetProperty("state").GetInt32()]);
                JsonElement expectedQueries = tick.GetProperty("collision_queries");
                Assert.Equal(expectedQueries.GetArrayLength(), queries.Count);
                for (int i = 0; i < queries.Count; i++)
                    AssertBox(expectedQueries[i], queries[i]);
            }
        }
    }

    [Theory]
    [InlineData("1.21.4", true)]
    [InlineData("1.21.5", false)]
    public void PendingRotationWitnessRejectsTheOtherSourcePolicy(string release, bool usesPendingRotation)
    {
        using JsonDocument document = ReadNative();
        JsonElement native = Release(document, release), cases = document.RootElement.GetProperty("cases"), states = document.RootElement.GetProperty("states");
        JsonElement row = native.GetProperty("cases").EnumerateArray().Select(index => cases[index.GetInt32()]).First(row => row.GetProperty("phase").GetInt32() == 1);
        JsonElement initial = states[row.GetProperty("initial").GetInt32()];
        var position = VanillaEntityInterpolation.CreateRemotePlayer(native.GetProperty("protocol").GetInt32(), Vector(initial), initial[6].GetSingle(), initial[7].GetSingle(), 0.6F, 1.8F);
        position.SetTarget(new Vec3d(40.5, 67, -1.5), -170, 35);
        EntityTeleportProfile profile = EntityTeleportProfile.ForProtocol(native.GetProperty("protocol").GetInt32()) with { UsesPendingRotation = usesPendingRotation };

        PositionMoveRotation wrong = profile.GetSource(position, Vector(initial, 3));

        Assert.NotEqual(BitConverter.SingleToInt32Bits(row.GetProperty("source")[6].GetSingle()), BitConverter.SingleToInt32Bits(wrong.YRot));
    }

    [Theory]
    [InlineData("1.21.2")]
    [InlineData("1.21.3")]
    [InlineData("1.21.4")]
    public void PitchIsClampedByTheEntitySetterBeforeTheTeleportArithmeticBoundary(string release)
    {
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == release);
        JsonElement cases = document.RootElement.GetProperty("cases"), states = document.RootElement.GetProperty("states");
        JsonElement scenario = native.GetProperty("cases").EnumerateArray().Select(index => cases[index.GetInt32()])
            .Single(row => row.GetProperty("phase").GetInt32() == 0 && row.GetProperty("interpolate").GetBoolean() && row.GetProperty("distance").GetInt32() == 0 && row.GetProperty("flags").GetInt32() == 0);
        JsonElement initial = states[scenario.GetProperty("initial").GetInt32()], after = states[scenario.GetProperty("after").GetInt32()];
        var position = VanillaEntityInterpolation.CreateRemotePlayer(native.GetProperty("protocol").GetInt32(), Vector(initial), initial[6].GetSingle(), initial[7].GetSingle(), 0.6F, 1.8F);
        position.SetTarget(Vector(after, 14), after[17].GetSingle(), after[18].GetSingle());
        Assert.Equal(110, after[18].GetSingle());
        foreach (JsonElement tick in scenario.GetProperty("ticks").EnumerateArray())
        {
            position.Tick(_ => true);
            JsonElement expected = states[tick.GetProperty("state").GetInt32()];
            Assert.Equal(BitConverter.SingleToInt32Bits(expected[7].GetSingle()), BitConverter.SingleToInt32Bits(position.Pitch));
        }
        Assert.Equal(90, position.Pitch);
    }

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(EntityTeleportControllerReplayTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.EntityTeleportController.native.json")!;
        return JsonDocument.Parse(stream);
    }

    private static JsonElement Release(JsonDocument document, string release)
        => document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == release);

    private static PositionMoveRotation Values(JsonElement row) => new(Vector(row), Vector(row, 3), row[6].GetSingle(), row[7].GetSingle());

    private static void AssertValues(JsonElement expected, PositionMoveRotation actual)
    {
        AssertVector(Vector(expected), actual.Position);
        AssertVector(Vector(expected, 3), actual.DeltaMovement);
        Assert.Equal(BitConverter.SingleToInt32Bits(expected[6].GetSingle()), BitConverter.SingleToInt32Bits(actual.YRot));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected[7].GetSingle()), BitConverter.SingleToInt32Bits(actual.XRot));
    }

    private static void AssertState(VanillaEntityInterpolation position, VanillaEntityVelocity velocity, JsonElement expected)
    {
        AssertVector(Vector(expected), position.Position);
        AssertVector(Vector(expected, 3), velocity.Value);
        Assert.Equal(BitConverter.SingleToInt32Bits(expected[6].GetSingle()), BitConverter.SingleToInt32Bits(position.Yaw));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected[7].GetSingle()), BitConverter.SingleToInt32Bits(position.Pitch));
        AssertBox(expected, position.Bounds, 8);
        AssertVector(Vector(expected, 14), position.TargetPosition);
        Assert.Equal(BitConverter.SingleToInt32Bits(expected[17].GetSingle()), BitConverter.SingleToInt32Bits(position.TargetYaw));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected[18].GetSingle()), BitConverter.SingleToInt32Bits(position.TargetPitch));
        Assert.Equal(expected[19].GetBoolean(), position.HasActiveInterpolation);
    }

    private static void AssertBox(JsonElement expected, Aabb actual, int offset = 0)
    {
        AssertVector(Vector(expected, offset), new Vec3d(actual.MinX, actual.MinY, actual.MinZ));
        AssertVector(Vector(expected, offset + 3), new Vec3d(actual.MaxX, actual.MaxY, actual.MaxZ));
    }

    private static void AssertVector(Vec3d expected, Vec3d actual)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.X), BitConverter.DoubleToInt64Bits(actual.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Y), BitConverter.DoubleToInt64Bits(actual.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Z), BitConverter.DoubleToInt64Bits(actual.Z));
    }

    private static Vec3d Vector(JsonElement row, int offset = 0) => new(row[offset].GetDouble(), row[offset + 1].GetDouble(), row[offset + 2].GetDouble());
}
