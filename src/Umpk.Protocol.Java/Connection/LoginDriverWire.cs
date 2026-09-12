using System.Buffers;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java;

/// <summary>The two login drivers' shared outbound path. Both resolve a wire id from the phase registry and encode against <see cref="PacketCodecContext.Registryless"/> rather than going through the connection's own send, because a login or configuration frame is written before any registry exists. The client and the server halves differ only in which flow they send on.</summary>
internal static class LoginDriverWire
{
    /// <summary>Encodes one packet against the phase registry and sends it as a frame.</summary>
    /// <typeparam name="TPacket">The packet type.</typeparam>
    /// <param name="connection">The connection to send on.</param>
    /// <param name="descriptor">The negotiated protocol descriptor.</param>
    /// <param name="phase">The phase whose registry resolves the wire id.</param>
    /// <param name="flow">The sending side's flow.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the frame is written.</returns>
    /// <exception cref="ProtocolViolationException">The phase has no implemented codec for the packet.</exception>
    public static async Task SendAsync<TPacket>(
        JavaConnection connection,
        ProtocolDescriptor descriptor,
        ProtocolPhase phase,
        PacketFlow flow,
        TPacket packet,
        CancellationToken ct)
        where TPacket : class, IPacket
    {
        PhaseRegistry registry = descriptor.GetRegistry(phase, flow);
        if (!registry.TryGetOutbound(packet.Type, out int wireId, out BoundPacketCodec entry) || !entry.IsImplemented)
            throw new ProtocolViolationException($"No implemented codec to send {packet.Type.Id} in {phase}.");

        var body = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(body);
        entry.Encode(ref writer, packet, PacketCodecContext.Registryless);
        await connection.SendFrameAsync(wireId, body.WrittenMemory, ct).ConfigureAwait(false);
    }

    /// <summary>The wire id one registry gives an identifier, or -1 when it carries none.</summary>
    /// <param name="registry">The phase registry to search.</param>
    /// <param name="id">The packet identifier.</param>
    /// <returns>The wire id, or -1.</returns>
    public static int WireIdOf(PhaseRegistry registry, Identifier id)
    {
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id)
                return wireId;

        return -1;
    }
}
