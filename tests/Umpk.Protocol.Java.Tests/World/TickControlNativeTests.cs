using System.Text.Json;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

public sealed class TickControlNativeTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
        {
            JsonElement cases = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()];
            for (int index = 0; index < cases.GetArrayLength(); index++)
            {
                JsonElement row = cases[index];
                if (!row.GetProperty("world_present").GetBoolean() || row.GetProperty("kind").GetString() == "step" && row.GetProperty("frozen").GetBoolean())
                    continue;

                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index];
            }
        }
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void NativeBodiesPreserveDecodedValuesAndExactBytes(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        Assert.Equal(protocol, release.GetProperty("protocol").GetInt32());
        JsonElement expected = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()][index];
        string identifier = expected.GetProperty("kind").GetString() == "state" ? "ticking_state" : "ticking_step";
        byte[] body = Convert.FromHexString(expected.GetProperty("body_hex").GetString()!);
        BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:" + identifier);

        object packet = codec.DecodeFrame(body);

        if (identifier == "ticking_state")
        {
            var state = Assert.IsType<ClientboundTickingStatePacket>(packet);
            Assert.Equal(expected.GetProperty("decoded_rate_bits").GetInt32(), BitConverter.SingleToInt32Bits(state.TickRate));
            Assert.Equal(expected.GetProperty("decoded_frozen").GetBoolean(), state.IsFrozen);
        }
        else
        {
            var step = Assert.IsType<ClientboundTickingStepPacket>(packet);
            Assert.Equal(expected.GetProperty("decoded_steps").GetInt32(), step.TickSteps);
        }

        Assert.Equal(body, codec.Encode(packet));
    }

    [Theory]
    [InlineData(765)]
    [InlineData(777)]
    public void BoundFramesRejectTruncationAndTrailingData(int protocol)
    {
        BoundPacketCodec state = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:ticking_state");
        BoundPacketCodec step = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:ticking_step");
        Assert.Throws<ProtocolViolationException>(() => state.DecodeFrame([0x41, 0xA0, 0, 0]));
        Assert.Throws<ProtocolViolationException>(() => state.DecodeFrame([0x41, 0xA0, 0, 0, 0, 0]));
        Assert.Throws<ProtocolViolationException>(() => step.DecodeFrame([0x80]));
        Assert.Throws<ProtocolViolationException>(() => step.DecodeFrame([0, 0]));
    }

    private static JsonDocument ReadNative() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.Protocol.Java.Tests", "Fixtures", "TickControl.native.json")));
}
