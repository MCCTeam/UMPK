using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Tests.Item;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Support;

/// <summary>Resolves the era codec the registrar actually binds for a protocol number, and runs frames through it via <see cref="BoundPacketCodec"/> - the same entry point the live dispatcher uses, so a trailing byte raises the same fault a live session would see.</summary>
/// <remarks>Codec-level tests prove a codec is right; only this proves the TIMELINE selects it. An era misbinding lives entirely in the binding table, so a suite that never resolves by protocol number cannot see one.</remarks>
internal static class BoundCodec
{
    /// <summary>The bound Play-phase codec for a packet identifier at a protocol number.</summary>
    public static BoundPacketCodec At(int protocol, PacketFlow flow, string identifier) =>
        At(protocol, ProtocolPhase.Play, flow, identifier);

    /// <summary>The bound codec for a packet identifier in any phase at a protocol number.</summary>
    public static BoundPacketCodec At(int protocol, ProtocolPhase phase, PacketFlow flow, string identifier)
    {
        BoundPacketCodec bound = Resolve(protocol, phase, flow, identifier);
        Assert.True(bound.IsImplemented, $"{identifier} is a marker at protocol {protocol}");
        return bound;
    }

    /// <summary>Whether a packet identifier resolves a real codec (rather than a marker) at a protocol.</summary>
    public static bool IsImplementedAt(int protocol, ProtocolPhase phase, PacketFlow flow, string identifier) =>
        Resolve(protocol, phase, flow, identifier).IsImplemented;

    private static BoundPacketCodec Resolve(int protocol, ProtocolPhase phase, PacketFlow flow, string identifier)
    {
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures());

        // The codec key is part of the frozen generated call shape and is not consulted: resolution is by protocol number alone (see PacketRegistrar.Register).
        PacketRegistrar.Register(builder, phase, flow, 0, identifier);
        ProtocolDescriptor descriptor = builder.Build();

        Assert.True(descriptor.GetRegistry(phase, flow).TryGetInbound(0, out BoundPacketCodec bound));
        return bound;
    }

    /// <summary>The bound Play-phase entry for a packet identifier at a protocol number WITHOUT asserting it is implemented, so a test can assert the registrar leaves it as an honest marker. This is the negative half of <see cref="At"/>: a packet that should not exist on an era must resolve nothing, and only resolving by protocol number can show that.</summary>
    public static BoundPacketCodec EntryAt(int protocol, PacketFlow flow, string identifier)
    {
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures());

        PacketRegistrar.Register(builder, ProtocolPhase.Play, flow, 0, identifier);
        ProtocolDescriptor descriptor = builder.Build();

        Assert.True(descriptor.GetRegistry(ProtocolPhase.Play, flow).TryGetInbound(0, out BoundPacketCodec bound));
        return bound;
    }

    /// <summary>The bound LOGIN-phase codec for a packet identifier at a protocol number. The login family needs its own resolver because the login timelines are keyed on <see cref="ProtocolPhase.Login"/>; resolving them in the Play phase would silently fall through to a marker instead of the era codec.</summary>
    public static BoundPacketCodec LoginAt(int protocol, PacketFlow flow, string identifier)
    {
        BoundPacketCodec bound = LoginEntryAt(protocol, flow, identifier);
        Assert.True(bound.IsImplemented, $"login {identifier} is a marker at protocol {protocol}");
        return bound;
    }

    /// <summary>The bound LOGIN-phase entry at a protocol number WITHOUT asserting it is implemented, so a test can pin where a login packet legitimately does not exist yet.</summary>
    public static BoundPacketCodec LoginEntryAt(int protocol, PacketFlow flow, string identifier)
    {
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures());

        PacketRegistrar.Register(builder, ProtocolPhase.Login, flow, 0, identifier);
        ProtocolDescriptor descriptor = builder.Build();

        Assert.True(descriptor.GetRegistry(ProtocolPhase.Login, flow).TryGetInbound(0, out BoundPacketCodec bound));
        return bound;
    }

    /// <summary>Encodes a packet through a bound codec under the item test registries.</summary>
    public static byte[] Encode(this BoundPacketCodec bound, object packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(ref writer, packet, ItemTestRegistries.Context);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a frame through a bound codec under the item test registries, frame-exactly.</summary>
    public static object DecodeFrame(this BoundPacketCodec bound, byte[] payload) =>
        bound.Decode(payload, ItemTestRegistries.Context);
}
