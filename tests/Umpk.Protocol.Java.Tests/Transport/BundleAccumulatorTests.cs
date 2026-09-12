using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>Bundle delimiter state and packet delivery outside and inside a bundle.</summary>
public sealed class BundleAccumulatorTests
{
    [Fact]
    public void Delimiters_AccumulatePacketsIntoOneBundle()
    {
        var accumulator = new BundleAccumulator(Identifier.Minecraft("bundle_delimiter"));
        var delimiter = new StubPacket();
        var inner = new StubPacket(Identifier.Minecraft("add_entity"));

        Assert.Equal(BundleFeed.Opened, accumulator.Offer(delimiter, out _));
        Assert.True(accumulator.IsAccumulating);
        Assert.Equal(BundleFeed.Buffered, accumulator.Offer(inner, out _));
        Assert.Equal(BundleFeed.Buffered, accumulator.Offer(inner, out _));
        Assert.Equal(BundleFeed.Closed, accumulator.Offer(delimiter, out PacketBundle? bundle));
        Assert.NotNull(bundle);
        Assert.Equal(2, bundle.Packets.Count);
        Assert.False(accumulator.IsAccumulating);
    }

    [Fact]
    public void PacketOutsideBundle_PassesThrough()
    {
        var accumulator = new BundleAccumulator(Identifier.Minecraft("bundle_delimiter"));
        var packet = new StubPacket(Identifier.Minecraft("add_entity"));
        Assert.Equal(BundleFeed.PassThrough, accumulator.Offer(packet, out PacketBundle? bundle));
        Assert.Null(bundle);
    }

    private sealed record StubPacket(Identifier Id) : IPacket
    {
        public StubPacket()
            : this(Identifier.Minecraft("bundle_delimiter"))
        {
        }

        public PacketType Type => new MarkerPacketType(ProtocolPhase.Play, PacketFlow.Clientbound, Id);
    }
}
