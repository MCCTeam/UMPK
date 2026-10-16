using System.Text.Json;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

public sealed class SetBorderNativeTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            for (int index = 0; index < document.RootElement.GetProperty("cases").GetArrayLength(); index++)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index];
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void BoundCodecPreservesNativeFieldsAndBytes(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        Assert.Contains(document.RootElement.GetProperty("releases").EnumerateArray(), row => row.GetProperty("version").GetString() == version && row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement row = document.RootElement.GetProperty("cases")[index];
        JsonElement expected = row.GetProperty("decoded");
        byte[] body = Convert.FromHexString(row.GetProperty("body_hex").GetString()!);
        BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:set_border");

        var packet = Assert.IsType<ClientboundSetBorderPacket>(codec.DecodeFrame(body));

        Assert.Equal((WorldBorderAction)row.GetProperty("action").GetInt32(), packet.Action);
        Assert.Equal(expected.GetProperty("newCenterX").GetDouble(), packet.CenterX);
        Assert.Equal(expected.GetProperty("newCenterZ").GetDouble(), packet.CenterZ);
        Assert.Equal(expected.GetProperty("oldSize").GetDouble(), packet.OldSize);
        Assert.Equal(expected.GetProperty("newSize").GetDouble(), packet.NewSize);
        Assert.Equal(expected.GetProperty("lerpTime").GetInt64(), packet.LerpTime);
        Assert.Equal(expected.GetProperty("newAbsoluteMaxSize").GetInt32(), packet.PortalTeleportBoundary);
        Assert.Equal(expected.GetProperty("warningBlocks").GetInt32(), packet.WarningBlocks);
        Assert.Equal(expected.GetProperty("warningTime").GetInt32(), packet.WarningTime);
        Assert.Equal(body, codec.Encode(packet));
    }

    [Theory]
    [InlineData(47, "world_border")]
    [InlineData(107, "set_border")]
    [InlineData(754, "set_border")]
    public void CombinedBorderRejectsUnknownActionsTruncationAndTrailingBytes(int protocol, string identifier)
    {
        BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:" + identifier);
        Assert.Throws<ProtocolViolationException>(() => codec.DecodeFrame([6]));
        Assert.Throws<ProtocolViolationException>(() => codec.DecodeFrame([3, 0]));
        Assert.Throws<ProtocolViolationException>(() => codec.DecodeFrame([4, 17, 0]));
    }

    [Fact]
    public void SetBorderDoesNotBindBeforeItsIdentifierExists() => Assert.False(BoundCodec.EntryAt(47, PacketFlow.Clientbound, "minecraft:set_border").IsImplemented);

    private static JsonDocument ReadNative() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.Protocol.Java.Tests", "Fixtures", "SetBorder.native.json")));
}
