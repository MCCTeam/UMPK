namespace Umpk.Protocol.Java;

/// <summary>Metadata captured when a mapped packet codec fails and the failure will terminate the connection.</summary>
/// <param name="Protocol">The negotiated protocol number, or <see langword="null"/> for a custom binding.</param>
/// <param name="Phase">The phase in which the frame was decoded.</param>
/// <param name="Flow">The direction of the failed frame.</param>
/// <param name="WireId">The frame's numeric packet id.</param>
/// <param name="PacketId">The mapped packet identity, or <see langword="null"/> when unavailable.</param>
/// <param name="CodecIdentity">The mapped era codec, or <see langword="null"/> when unavailable.</param>
/// <param name="PayloadLength">The complete payload length, whether or not evidence was retained.</param>
/// <param name="Evidence">A bounded prefix of non-sensitive payload, empty for handshake/login frames.</param>
/// <param name="EvidenceTruncated">Whether <paramref name="Evidence"/> omits payload bytes.</param>
/// <param name="Exception">The original codec exception.</param>
public sealed record PacketDecodeFailure(
    int? Protocol,
    ProtocolPhase Phase,
    PacketFlow Flow,
    int WireId,
    Identifier? PacketId,
    string? CodecIdentity,
    int PayloadLength,
    ReadOnlyMemory<byte> Evidence,
    bool EvidenceTruncated,
    Exception Exception);
