using System.Text.Json;
using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.TestKit.Fixtures;

public readonly record struct NativePlayerCorrectionState(Vec3d Position, Vec3d Velocity, float Yaw, float Pitch, EntityPose Pose, Aabb Box, double EyeHeight, bool OnGround, bool HorizontalCollision, bool VerticalCollision, double FallDistance);

public readonly record struct NativePlayerCorrection(int Flags, NativePlayerCorrectionState Before, NativePlayerCorrectionState After);

public static class NativePlayerCorrections
{
    public static IEnumerable<object[]> Versions()
    {
        using JsonDocument fixture = Read();
        foreach (JsonElement release in fixture.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32()];
    }

    public static NativePlayerCorrection[] ForVersion(string version)
    {
        using JsonDocument fixture = Read();
        JsonElement release = fixture.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        return fixture.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()].EnumerateArray()
            .Select(row => new NativePlayerCorrection(row.GetProperty("flags").GetInt32(), State(row.GetProperty("before")), State(row.GetProperty("after"))))
            .ToArray();
    }

    private static NativePlayerCorrectionState State(JsonElement row)
    {
        JsonElement position = row.GetProperty("position");
        JsonElement velocity = row.GetProperty("velocity");
        JsonElement rotation = row.GetProperty("rotation");
        JsonElement box = row.GetProperty("box");
        EntityPose pose = row.GetProperty("pose").GetString() switch
        {
            "standing" => EntityPose.Standing,
            "crouching" => EntityPose.Crouching,
            "swimming" => EntityPose.Swimming,
            "fall_flying" => EntityPose.FallFlying,
            string unknown => throw new InvalidDataException($"Unknown native player pose: {unknown}"),
            null => throw new InvalidDataException("Missing native player pose."),
        };
        return new NativePlayerCorrectionState(
            new Vec3d(position[0].GetDouble(), position[1].GetDouble(), position[2].GetDouble()),
            new Vec3d(velocity[0].GetDouble(), velocity[1].GetDouble(), velocity[2].GetDouble()),
            rotation[0].GetSingle(), rotation[1].GetSingle(), pose,
            new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()),
            row.GetProperty("eye_height").GetDouble(), row.GetProperty("on_ground").GetBoolean(), row.GetProperty("horizontal_collision").GetBoolean(),
            row.GetProperty("vertical_collision").GetBoolean(), row.GetProperty("fall_distance").GetDouble());
    }

    private static JsonDocument Read()
    {
        using Stream stream = typeof(NativePlayerCorrections).Assembly.GetManifestResourceStream("Umpk.TestKit.Fixtures.PlayerCorrections.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
