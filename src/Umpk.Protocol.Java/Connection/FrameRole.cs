namespace Umpk.Protocol.Java;

/// <summary>What a frame at this wire id does to the connection, besides carrying a payload. Computed once at descriptor build time and read per frame off the entry the wire-id lookup already returned.</summary>
[Flags]
public enum FrameRole : byte
{
    /// <summary>No lifecycle effect.</summary>
    None = 0,

    /// <summary>Ends its phase: the read loop parks and <see cref="BoundPacketCodec.NextPhase"/> is the phase to enter.</summary>
    Terminal = 1 << 0,

    /// <summary>Clientbound <c>login_compression</c>: the read loop parks until compression is enabled.</summary>
    CompressionPoint = 1 << 1,

    /// <summary>The encryption request for this role: the read loop parks until encryption is enabled.</summary>
    EncryptionPoint = 1 << 2,

    /// <summary><c>minecraft:bundle_delimiter</c> (1.19.4+).</summary>
    BundleDelimiter = 1 << 3,
}
