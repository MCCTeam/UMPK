using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class VanillaPlacementCollisionTests
{
    [Theory]
    [MemberData(nameof(NativeReleases))]
    public void FullCubeObstructionMatchesNativeClientWitnesses(string release, int protocol, int profileIndex)
    {
        using JsonDocument document = ReadWitnesses();
        JsonElement profile = document.RootElement.GetProperty("profiles")[profileIndex];
        int index = 0;
        foreach (JsonElement witness in profile.GetProperty("witnesses").EnumerateArray())
        {
            double[] cube = witness[0].EnumerateArray().Select(value => value.GetDouble()).ToArray();
            double[] entity = witness[1].EnumerateArray().Select(value => value.GetDouble()).ToArray();
            var position = new BlockPos((int)cube[0], (int)cube[1], (int)cube[2]);
            Assert.Equal(Aabb.BlockAt(position.X, position.Y, position.Z), new Aabb(cube[0], cube[1], cube[2], cube[3], cube[4], cube[5]));
            bool actual = VanillaPlacementCollision.Intersects(position, new Aabb(entity[0], entity[1], entity[2], entity[3], entity[4], entity[5]), BlockInteractionProfile.ForProtocol(protocol));
            Assert.True(actual == witness[2].GetBoolean(), $"Native client obstruction differs for {release} (protocol {protocol}), witness {index}: {witness.GetRawText()}");
            index++;
        }
        Assert.Equal(1500, index);
    }

    public static IEnumerable<object[]> NativeReleases
    {
        get
        {
            using JsonDocument document = ReadWitnesses();
            foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), release.GetProperty("profile").GetInt32()];
        }
    }

    private static JsonDocument ReadWitnesses()
    {
        using Stream stream = typeof(VanillaPlacementCollisionTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.PlacementCollision.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
