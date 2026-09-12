using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>Seeded round-trip coverage for unsigned and signed chat packet shapes.</summary>
public sealed class ChatRoundTripTests
{
    [Theory]
    [InlineData("hello world")]
    [InlineData("")]
    public void UnsignedServerboundMessage_RoundTrips(string message)
    {
        var packet = new ServerboundLegacyChatPacket(message);
        Assert.Equal(packet, CodecRoundTrip.Cycle(ChatCodecs.ServerLegacyV1_8, packet));
    }

    [Fact]
    public void PositionedClientboundMessage_RoundTrips()
    {
        var packet = new ClientboundLegacyChatPacket(Component.Text("system message"), 1);
        var decoded = CodecRoundTrip.Cycle(ChatCodecs.ClientLegacyV1_8, packet);
        Assert.Equal(packet.Position, decoded.Position);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2024)]
    public void SignedMessage_RoundTrips(int seed)
    {
        var random = new Random(seed);
        byte[]? signature = random.Next(2) == 0 ? RandomBytes(random, 256) : null;
        var packet = new ServerboundSignedChatPacket(
            "gm all", random.NextInt64(), random.NextInt64(), signature,
            new LastSeenMessagesUpdate(random.Next(0, 100), RandomBytes(random, 3), (byte)random.Next(0, 256)));
        var decoded = CodecRoundTrip.Cycle(ChatCodecs.SignedV1_21_5, packet);
        Assert.Equal(packet.Message, decoded.Message);
        Assert.Equal(packet.Salt, decoded.Salt);
        Assert.Equal(packet.Signature, decoded.Signature);
        Assert.Equal(packet.LastSeen.Offset, decoded.LastSeen.Offset);
        Assert.Equal(packet.LastSeen.Checksum, decoded.LastSeen.Checksum);
    }

    private static byte[] RandomBytes(Random random, int count)
    {
        var bytes = new byte[count];
        random.NextBytes(bytes);
        return bytes;
    }
}
