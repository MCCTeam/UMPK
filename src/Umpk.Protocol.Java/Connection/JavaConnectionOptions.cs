using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Umpk.Protocol.Java;

/// <summary>Tuning knobs for a <see cref="JavaConnection"/>. Timeouts mirror vanilla's keep-alive window. The inbound channel bound provides real backpressure: when it fills, the socket read loop stalls until a consumer drains it.</summary>
public sealed record JavaConnectionOptions
{
    /// <summary>Bound of the inbound delivery channel. Proxies set this high; 0 means unbounded.</summary>
    public int InboundChannelCapacity { get; init; } = 1024;

    /// <summary>Maximum accepted uncompressed frame length. A larger frame is a protocol violation and closes the connection. Enforced by the frame reader on the uncompressed frame body and, on the compressed path, on the declared uncompressed length before inflation. The default is the wire limit of 8 MiB. Independently, the on-wire length prefix is capped at 3 bytes (21-bit), so the compressed frame body cannot exceed about 2 MiB.</summary>
    public int MaxFrameLength { get; init; } = 8 * 1024 * 1024;

    /// <summary>Idle read timeout; no inbound traffic for this long closes the connection.</summary>
    public TimeSpan ReadIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Connect timeout used by connection factories.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Login timeout used by the login helper.</summary>
    public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Policy for frames whose wire id is not in the bound registry.</summary>
    public UnknownPacketPolicy UnknownPacketPolicy { get; init; } = UnknownPacketPolicy.Throw;

    /// <summary>Behavior when a mapped codec throws mid-decode (a malformed field, or the frame-exact trailing-byte check). Defaults to <see cref="DecodeFailureMode.FailConnection"/>, the strict client/server behavior. The proxy role selects <see cref="DecodeFailureMode.ForwardVerbatim"/> so a mapped-but-buggy codec degrades to a verbatim relay instead of killing the session; the frame then surfaces as an <see cref="UnknownPacket"/> carrying its known wire id and raw payload for byte-identical re-emission.</summary>
    public DecodeFailureMode DecodeFailurePolicy { get; init; } = DecodeFailureMode.FailConnection;

    /// <summary>Logger for connection diagnostics. Defaults to a no-op logger.</summary>
    public ILogger Logger { get; init; } = NullLogger.Instance;
}

/// <summary>Policy applied to inbound frames whose wire id is unmapped.</summary>
public enum UnknownPacketPolicy
{
    /// <summary>Raise a protocol violation (default for client/server roles).</summary>
    Throw,

    /// <summary>Surface the raw frame verbatim (default for the proxy role).</summary>
    Preserve,

    /// <summary>Drop the frame and count it.</summary>
    Skip,
}
