using System.Buffers;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Client.Tests.Support;

/// <summary>Resolves the codec the version catalog binds for a packet identifier at a protocol number, and round-trips packets through it. Tests that build a packet record by hand prove an applier reads the record; only going through the bound codec proves the wire form reaches that applier at all on the era under test.</summary>
/// <remarks>The sibling <c>BoundCodec</c> in the protocol test assembly registers a synthetic single-packet descriptor; this one asks <see cref="JavaVersions"/> for the real production descriptor, so a packet left as a marker on an era fails here exactly the way a live session on that era would silently drop the frame. That is the failure this batch has produced repeatedly.</remarks>
internal static class BoundDescriptorCodec
{
    /// <summary>The bound Play-clientbound codec for an identifier at a protocol, asserted implemented.</summary>
    public static BoundPacketCodec Clientbound(int protocol, string identifier)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version), $"unknown protocol {protocol}");
        ProtocolDescriptor descriptor = version!.Protocol;
        Assert.True(
            descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry),
            $"no play/clientbound registry at protocol {protocol}");

        var id = Identifier.Minecraft(identifier);
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec bound));
            Assert.True(bound.IsImplemented, $"minecraft:{identifier} is a marker at protocol {protocol}");
            return bound;
        }

        Assert.Fail($"minecraft:{identifier} is not registered at protocol {protocol}");
        return null!;
    }

    /// <summary>Encodes an outbound packet through the descriptor's Play/Serverbound registry at a protocol, asserting the packet's identity resolves a real codec rather than a marker.</summary>
    /// <remarks>A test that only inspects what the send path handed a recording sink cannot tell a sendable packet from one whose identity is an unimplemented marker on that era; the latter throws <c>NotImplementedCodecException</c> the moment a live session tries to serialise it. Running the emitted packet through the production outbound table is what makes the difference visible.</remarks>
    public static byte[] EncodeServerbound(int protocol, IPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version), $"unknown protocol {protocol}");
        Assert.True(
            version!.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound, out PhaseRegistry registry),
            $"no play/serverbound registry at protocol {protocol}");
        Assert.True(
            registry.TryGetOutbound(packet.Type, out int _, out BoundPacketCodec bound),
            $"{packet.Type.Id} has no outbound binding at protocol {protocol}");
        Assert.True(bound.IsImplemented, $"{packet.Type.Id} is a marker at protocol {protocol}");

        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Encodes a packet through the bound codec for a protocol and decodes the bytes back, so the value an applier receives is one that actually survived that era's wire form.</summary>
    public static object RoundTrip(int protocol, string identifier, object packet)
    {
        BoundPacketCodec codec = Clientbound(protocol, identifier);
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return codec.Decode(buffer.WrittenSpan, context);
    }
}
