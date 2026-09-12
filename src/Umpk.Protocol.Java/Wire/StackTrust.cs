namespace Umpk.Protocol.Java.Codecs;

/// <summary>Whether a component patch on the wire is the trusted form or the untrusted one. This is NOT an era: both forms exist on every component protocol from 1.20.5 up, and which one a packet uses is a property of that packet's direction and trust, not of the version. It shared a parameter slot with era flags for long enough to read like one.</summary>
/// <remarks>Only untrusted stack patches length-prefix each component payload, which is what makes an unmodelled component recoverable there and packet-fatal on the other.</remarks>
internal enum StackTrust
{
    /// <summary>The server-sourced patch: component payloads are written inline, with no length.</summary>
    Trusted,

    /// <summary>The untrusted patch (the creative-mode slot the client sends): each component payload carries a VarInt length, so a reader that does not model a component can carry its bytes verbatim.</summary>
    Untrusted,
}
