using System.Text.Json;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

public sealed class MoveEntityRotNativeTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            foreach (JsonElement index in release.GetProperty("cases").EnumerateArray())
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index.GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void BoundCodecPreservesNativeRotationValuesAndBytes(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        Assert.Equal(protocol, release.GetProperty("protocol").GetInt32());
        JsonElement expected = document.RootElement.GetProperty("cases")[index];
        byte[] body = Convert.FromHexString(expected.GetProperty("body_hex").GetString()!);
        BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:move_entity_rot");

        var packet = Assert.IsType<ClientboundMoveEntityRotPacket>(codec.DecodeFrame(body));

        Assert.Equal(expected.GetProperty("entity_id").GetInt32(), packet.EntityId);
        Assert.Equal(expected.GetProperty("yaw").GetSingle(), packet.Yaw);
        Assert.Equal(expected.GetProperty("pitch").GetSingle(), packet.Pitch);
        Assert.Equal(expected.GetProperty("on_ground").GetBoolean(), packet.OnGround);
        Assert.Equal(body, codec.Encode(packet));

        if (protocol is 776 or 777)
        {
            // Both layouts accept these four-byte bodies, but the neighboring layout must change their meaning.
            BoundPacketCodec neighbor = BoundCodec.At(protocol == 777 ? 776 : 777, PacketFlow.Clientbound, "minecraft:move_entity_rot");
            Assert.NotEqual(packet, Assert.IsType<ClientboundMoveEntityRotPacket>(neighbor.DecodeFrame(body)));
        }
    }

    private static JsonDocument ReadNative() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.Protocol.Java.Tests", "Fixtures", "MoveEntityRot.native.json")));
}
