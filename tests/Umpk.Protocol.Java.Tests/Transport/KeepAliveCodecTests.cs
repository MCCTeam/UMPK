using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>Keep-alive identifier round trips for both integer widths and packet directions.</summary>
public sealed class KeepAliveCodecTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void BothIdentifierWidths_RoundTripInBothDirections(int seed)
    {
        var random = new Random(seed);
        long compactId = random.Next(0, 100000);
        Assert.Equal(compactId,
            CodecRoundTrip.Cycle(PlayKeepAliveCodecs.ClientV1_8, new ClientboundPlayKeepAlivePacket(compactId)).Id);
        Assert.Equal(compactId,
            CodecRoundTrip.Cycle(PlayKeepAliveCodecs.ServerV1_8, new ServerboundPlayKeepAlivePacket(compactId)).Id);

        long wideId = random.NextInt64();
        Assert.Equal(wideId,
            CodecRoundTrip.Cycle(PlayKeepAliveCodecs.ClientV1_12_2, new ClientboundPlayKeepAlivePacket(wideId)).Id);
        Assert.Equal(wideId,
            CodecRoundTrip.Cycle(PlayKeepAliveCodecs.ServerV1_12_2, new ServerboundPlayKeepAlivePacket(wideId)).Id);
    }
}
