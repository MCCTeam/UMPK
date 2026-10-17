using System.Text.Json;
using Umpk.Game.Entities;
using Umpk.Geometry;

namespace Umpk.TestKit.Fixtures;

public readonly record struct NativePlayerDimension(EntityPose Pose, Vec3d Position, Aabb Box, double EyeHeight);

public static class NativePlayerDimensions
{
    public static IEnumerable<object[]> Versions()
    {
        using JsonDocument fixture = Read();
        foreach (JsonElement release in fixture.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32()];
    }

    public static NativePlayerDimension[] ForVersion(string version)
    {
        using JsonDocument fixture = Read();
        JsonElement release = fixture.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        return fixture.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()].EnumerateArray().Select(row =>
        {
            JsonElement position = row.GetProperty("position");
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
            return new NativePlayerDimension(pose,
                new Vec3d(position[0].GetDouble(), position[1].GetDouble(), position[2].GetDouble()),
                new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()),
                row.GetProperty("eye_height").GetDouble());
        }).ToArray();
    }

    private static JsonDocument Read()
    {
        using Stream stream = typeof(NativePlayerDimensions).Assembly.GetManifestResourceStream("Umpk.TestKit.Fixtures.PlayerDimensions.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
