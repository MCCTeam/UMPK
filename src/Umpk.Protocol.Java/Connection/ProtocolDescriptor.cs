namespace Umpk.Protocol.Java;

/// <summary>The immutable per-version binding of (phase, flow, wire id) to packet type and codec instance, plus the terminal-packet set that drives phase transitions. Built by generated code through <see cref="ProtocolDescriptorBuilder"/>. After binding, the hot path has no protocol-version comparisons; version differences were resolved once when this descriptor was selected.</summary>
public sealed class ProtocolDescriptor
{
    private readonly Dictionary<(ProtocolPhase, PacketFlow), PhaseRegistry> _registries;

    private readonly Dictionary<(ProtocolPhase, PacketFlow, int), ProtocolPhase> _terminals;

    private readonly HashSet<(ProtocolPhase, PacketFlow, int)> _compressionEnablePoints;

    private readonly HashSet<(ProtocolPhase, PacketFlow, int)> _encryptionEnablePoints;

    internal ProtocolDescriptor(
        GameVersion version,
        ProtocolFeatures features,
        Dictionary<(ProtocolPhase, PacketFlow), PhaseRegistry> registries,
        Dictionary<(ProtocolPhase, PacketFlow, int), ProtocolPhase> terminals,
        HashSet<(ProtocolPhase, PacketFlow, int)> compressionEnablePoints,
        HashSet<(ProtocolPhase, PacketFlow, int)> encryptionEnablePoints)
    {
        Version = version;
        Features = features;
        _registries = registries;
        _terminals = terminals;
        _compressionEnablePoints = compressionEnablePoints;
        _encryptionEnablePoints = encryptionEnablePoints;
    }

    /// <summary>The version this descriptor serves.</summary>
    public GameVersion Version { get; }

    /// <summary>The per-version feature flags.</summary>
    public ProtocolFeatures Features { get; }

    /// <summary>Gets the packet registry for a (phase, flow) pair.</summary>
    public bool TryGetRegistry(ProtocolPhase phase, PacketFlow flow, out PhaseRegistry registry) =>
        _registries.TryGetValue((phase, flow), out registry!);

    /// <summary>Gets the packet registry for a (phase, flow) pair or throws if absent.</summary>
    public PhaseRegistry GetRegistry(ProtocolPhase phase, PacketFlow flow) =>
        _registries.TryGetValue((phase, flow), out PhaseRegistry? registry)
            ? registry
            : throw new KeyNotFoundException($"No packet registry for {phase}/{flow} in protocol {Version.Protocol}.");

    /// <summary>Reports whether a decoded frame terminates its phase, and if so the phase to enter. The connection pauses inbound reading at the transition until the consumer acknowledges it.</summary>
    public bool TryGetTerminalTransition(ProtocolPhase phase, PacketFlow flow, int wireId, out ProtocolPhase nextPhase) =>
        _terminals.TryGetValue((phase, flow, wireId), out nextPhase);

    /// <summary>Reports whether a decoded frame is the set-compression frame (<c>login_compression</c>), after which every subsequent frame uses the compressed wire format. The connection pauses inbound reading at this frame boundary until the consumer enables compression via <see cref="JavaConnection.EnableCompression"/>, so the next frame is read with the compressed reader. Without this pause a zero-latency in-memory pipe can hand the read loop the next (compressed) frame before the consumer's enable call lands, decoding it with the wrong reader.</summary>
    public bool IsCompressionEnablePoint(ProtocolPhase phase, PacketFlow flow, int wireId) =>
        _compressionEnablePoints.Contains((phase, flow, wireId));

    /// <summary>Reports whether a decoded frame is the encryption-request frame (client role: the clientbound encryption request <c>hello</c>; server role: the serverbound <c>key</c> response), after which the peer's bytes are AES-CFB8 encrypted. The connection pauses inbound reading at this frame boundary until the consumer enables encryption via <see cref="JavaConnection.EnableEncryption"/>, so post-handshake bytes are not appended to the read buffer undecrypted (mirror of the set-compression pause).</summary>
    public bool IsEncryptionEnablePoint(ProtocolPhase phase, PacketFlow flow, int wireId) =>
        _encryptionEnablePoints.Contains((phase, flow, wireId));
}
