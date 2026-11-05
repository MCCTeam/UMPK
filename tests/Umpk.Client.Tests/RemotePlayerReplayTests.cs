using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class RemotePlayerReplayTests
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

    public static IEnumerable<object[]> LegacyReleases
    {
        get
        {
            using JsonDocument document = ReadNative("LegacyRemotePlayer");
            foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
                yield return [row.GetProperty("version").GetString()!];
        }
    }

    [Fact]
    public void LegacyMeasurementsCoverEveryPreMappingRelease()
    {
        using JsonDocument catalogue = ReadNative("EntityPacketPosition");
        using JsonDocument legacy = ReadNative("LegacyRemotePlayer");
        string[] expected = catalogue.RootElement.GetProperty("releases").EnumerateArray()
            .Where(row => row.GetProperty("protocol").GetInt32() < 498)
            .Select(row => row.GetProperty("version").GetString()!).Order(StringComparer.Ordinal).ToArray();
        string[] measured = legacy.RootElement.GetProperty("releases").EnumerateArray()
            .Select(row => row.GetProperty("version").GetString()!).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(31, expected.Length);
        Assert.Equal(expected, measured);
    }

    [Theory]
    [MemberData(nameof(LegacyReleases))]
    public void LegacyPositionVelocityAndBoundsMatchActualRemotePlayer(string version)
    {
        using JsonDocument document = ReadNative("LegacyRemotePlayer");
        JsonElement release = Release(document, version);
        Assert.Equal("LegacyDoubleRotation", release.GetProperty("position_profile").GetString());
        Assert.Equal(3, release.GetProperty("position_steps").GetInt32());
        Assert.Equal(0, release.GetProperty("velocity_steps").GetInt32());
        Assert.Equal(9, release.GetProperty("cases").GetArrayLength());
        foreach (JsonElement index in release.GetProperty("cases").EnumerateArray())
        {
            JsonElement native = document.RootElement.GetProperty("cases")[index.GetInt32()];
            JsonElement initial = native.GetProperty("initial");
            var position = VanillaEntityInterpolation.CreateRemotePlayer(release.GetProperty("protocol").GetInt32(), Vector(initial.GetProperty("position")), initial.GetProperty("rotation")[0].GetSingle(), initial.GetProperty("rotation")[1].GetSingle(), 0.6F, 1.8F);
            var velocity = new VanillaEntityVelocity(0);
            Assert.Equal(12, native.GetProperty("ticks").GetArrayLength());
            foreach (JsonElement tick in native.GetProperty("ticks").EnumerateArray())
            {
                foreach (JsonElement operation in tick.GetProperty("operations").EnumerateArray())
                {
                    switch (operation.GetProperty("kind").GetString())
                    {
                        case "target":
                            position.SetTarget(Vector(operation.GetProperty("position")), operation.GetProperty("rotation")[0].GetSingle(), operation.GetProperty("rotation")[1].GetSingle());
                            break;
                        case "velocity":
                            velocity.Observe(Vector(operation.GetProperty("value")));
                            break;
                        default:
                            throw new InvalidOperationException("Unknown legacy native input");
                    }
                }
                AssertState(position, tick.GetProperty("before"));
                position.Tick(_ => true);
                velocity.Tick();
                AssertState(position, tick.GetProperty("after"));
                AssertVector(Vector(tick.GetProperty("velocity")), velocity.Value);
            }
        }
    }

    [Theory]
    [InlineData("rotation_precision", "LegacyFloatRotation")]
    [InlineData("position_precision", "LinearStep")]
    public void LegacyPrecisionWitnessesRejectDifferentArithmetic(string name, string alternative)
    {
        using JsonDocument document = ReadNative("LegacyRemotePlayer");
        JsonElement native = document.RootElement.GetProperty("cases").EnumerateArray().First(row => row.GetProperty("name").GetString() == name);
        var position = LegacyController(native, Enum.Parse<VanillaInterpolationProfile>(alternative));
        JsonElement tick = native.GetProperty("ticks")[0], target = tick.GetProperty("operations")[0];
        position.SetTarget(Vector(target.GetProperty("position")), target.GetProperty("rotation")[0].GetSingle(), target.GetProperty("rotation")[1].GetSingle());

        position.Tick(_ => true);

        JsonElement expected = tick.GetProperty("after");
        if (name == "rotation_precision")
            Assert.NotEqual(BitConverter.SingleToInt32Bits(expected.GetProperty("rotation")[0].GetSingle()), BitConverter.SingleToInt32Bits(position.Yaw));
        else
            Assert.NotEqual(BitConverter.DoubleToInt64Bits(expected.GetProperty("position")[0].GetDouble()), BitConverter.DoubleToInt64Bits(position.Position.X));
    }

    private static VanillaEntityInterpolation LegacyController(JsonElement native, VanillaInterpolationProfile profile)
    {
        JsonElement initial = native.GetProperty("initial");
        return new VanillaEntityInterpolation(profile, Vector(initial.GetProperty("position")), initial.GetProperty("rotation")[0].GetSingle(), initial.GetProperty("rotation")[1].GetSingle(), 0.6F, 1.8F, 3);
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void VelocityPhasesMatchActualRemotePlayer(string version)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = Release(document, version);
        foreach (JsonElement index in release.GetProperty("cases").EnumerateArray())
        {
            JsonElement native = document.RootElement.GetProperty("cases")[index.GetInt32()];
            var velocity = new VanillaEntityVelocity(release.GetProperty("velocity_steps").GetInt32());
            foreach (JsonElement tick in native.GetProperty("ticks").EnumerateArray())
            {
                foreach (JsonElement operation in tick.GetProperty("operations").EnumerateArray())
                {
                    if (operation.GetString() == "motion") velocity.Observe(new Vec3d(0.3, -0.2, 0.7));
                    if (operation.GetString() == "motion_retarget") velocity.Observe(new Vec3d(-0.15, 0.4, -0.35));
                }
                velocity.Tick();
                AssertVector(Vector(tick.GetProperty("velocity")), velocity.Value);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void PositionAndPosePhasesMatchActualRemotePlayer(string version)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = Release(document, version);
        VanillaInterpolationProfile profile = release.GetProperty("position_profile").GetString() switch
        {
            "1.14.4" => VanillaInterpolationProfile.LegacyDoubleRotation,
            "1.17" => VanillaInterpolationProfile.LegacyFloatRotation,
            "1.20.2" => VanillaInterpolationProfile.LinearStep,
            "1.21.5" => VanillaInterpolationProfile.HandlerOffsetBox,
            "1.21.6" => VanillaInterpolationProfile.Handler,
            "1.21.9" => VanillaInterpolationProfile.DeduplicatingHandler,
            "26.3" => VanillaInterpolationProfile.Stepped,
            _ => throw new InvalidOperationException("Unmeasured remote-player profile"),
        };
        foreach (JsonElement index in release.GetProperty("cases").EnumerateArray())
        {
            JsonElement native = document.RootElement.GetProperty("cases")[index.GetInt32()];
            string name = native.GetProperty("name").GetString()!;
            var position = VanillaEntityInterpolation.CreateRemotePlayer(release.GetProperty("protocol").GetInt32(), new Vec3d(1.5, 64, 1.5), name == "wrap_rotation" ? 170 : 0, 0, 0.6F, 1.8F);
            foreach (JsonElement tick in native.GetProperty("ticks").EnumerateArray())
            {
                foreach (JsonElement operation in tick.GetProperty("operations").EnumerateArray())
                {
                    switch (operation.GetString())
                    {
                        case "target": position.SetTarget(new Vec3d(4.5, 67, -1.5), name == "wrap_rotation" ? -170 : 90, 30); break;
                        case "repeat": position.SetTarget(new Vec3d(4.5, 67, -1.5), 90, 30); break;
                        case "retarget": position.SetTarget(new Vec3d(-2.5, 65, 3.5), -90, -15); break;
                        case "crouch": position.RefreshDimensions(0.6F, 1.5F, profile != VanillaInterpolationProfile.LegacyDoubleRotation); break;
                    }
                }
                AssertState(position, tick.GetProperty("before"));
                position.Tick(_ => true);
                AssertState(position, tick.GetProperty("after"));
            }
        }
    }

    [Fact]
    public void SettingCurrentVelocityPreservesThePendingRemoteTarget()
    {
        var velocity = new VanillaEntityVelocity(3);
        var target = new Vec3d(0.25, -0.5, 0.75);
        velocity.Observe(target);
        velocity.Tick();
        velocity.SetCurrent(new Vec3d(0.875, 0.5, 0.75));

        velocity.Tick();
        velocity.Tick();

        AssertVector(target, velocity.Value);
    }

    private static JsonDocument ReadNative(string fixture = "RemotePlayer")
    {
        using Stream stream = typeof(RemotePlayerReplayTests).Assembly.GetManifestResourceStream($"Umpk.Client.Tests.Fixtures.{fixture}.native.json")!;
        return JsonDocument.Parse(stream);
    }

    private static JsonElement Release(JsonDocument document, string version)
        => document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);

    private static Vec3d Vector(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());

    private static void AssertVector(Vec3d expected, Vec3d actual)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.X), BitConverter.DoubleToInt64Bits(actual.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Y), BitConverter.DoubleToInt64Bits(actual.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Z), BitConverter.DoubleToInt64Bits(actual.Z));
    }

    private static void AssertState(VanillaEntityInterpolation actual, JsonElement expected)
    {
        AssertVector(Vector(expected.GetProperty("position")), actual.Position);
        JsonElement rotation = expected.GetProperty("rotation");
        Assert.Equal(BitConverter.SingleToInt32Bits(rotation[0].GetSingle()), BitConverter.SingleToInt32Bits(actual.Yaw));
        Assert.Equal(BitConverter.SingleToInt32Bits(rotation[1].GetSingle()), BitConverter.SingleToInt32Bits(actual.Pitch));
        JsonElement box = expected.GetProperty("box");
        AssertVector(new Vec3d(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble()), new Vec3d(actual.Bounds.MinX, actual.Bounds.MinY, actual.Bounds.MinZ));
        AssertVector(new Vec3d(box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()), new Vec3d(actual.Bounds.MaxX, actual.Bounds.MaxY, actual.Bounds.MaxZ));
        Assert.Equal(expected.GetProperty("active").GetBoolean(), actual.HasActiveInterpolation);
        JsonElement target = expected.GetProperty("target");
        AssertVector(new Vec3d(target[0].GetDouble(), target[1].GetDouble(), target[2].GetDouble()), actual.TargetPosition);
        Assert.Equal(BitConverter.SingleToInt32Bits(target[3].GetSingle()), BitConverter.SingleToInt32Bits(actual.TargetYaw));
        Assert.Equal(BitConverter.SingleToInt32Bits(target[4].GetSingle()), BitConverter.SingleToInt32Bits(actual.TargetPitch));
    }
}
