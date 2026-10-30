using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Corpus;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

public sealed class TickControlCorpusTests
{
    [Theory]
    [InlineData(765, "minecraft:ticking_state")]
    [InlineData(765, "minecraft:ticking_step")]
    [InlineData(777, "minecraft:ticking_state")]
    [InlineData(777, "minecraft:ticking_step")]
    public void RecordedServerBodiesPreserveValuesAndBytesAtBothBoundaries(int protocol, string identifier)
    {
        CorpusFrameRef reference = CorpusFrames.EnumerateCoveredBindings().First(frame =>
            frame.Protocol == 776 && frame.Packet == identifier);
        (_, byte[] body) = CorpusFrames.Load(reference);
        var key = new ProtocolTimeline.PacketKey(ProtocolPhase.Play, PacketFlow.Clientbound, identifier);
        BoundPacketCodec codec = Witnesses.Bind(protocol, key)!;
        Assert.True(codec.IsImplemented);
        PacketCodecContext context = Witnesses.Context(protocol);
        Assert.True(codec.TryProbeShape(body, context, out int consumed, out _));
        Assert.Equal(body.Length, consumed);
        object packet = codec.Decode(body, context);
        if (identifier == "minecraft:ticking_state")
        {
            var state = Assert.IsType<ClientboundTickingStatePacket>(packet);
            Assert.Equal(20f, state.TickRate);
            Assert.False(state.IsFrozen);
        }
        else
            Assert.Equal(0, Assert.IsType<ClientboundTickingStepPacket>(packet).TickSteps);
        var encoded = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(encoded);
        codec.Encode(ref writer, packet, context);
        Assert.Equal(body, encoded.WrittenSpan.ToArray());
    }
}
