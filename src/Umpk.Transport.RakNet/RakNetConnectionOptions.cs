namespace Umpk.Transport.RakNet;

/// <summary>Bounds for one RakNet session covering datagram size, logical message size, and queue retention.</summary>
/// <remarks>Every bound is enforced instead of bypassed, so saturation terminates safely instead of evicting accepted reliable work; tune from measured target traffic rather than copying these defaults as protocol facts.</remarks>
public sealed class RakNetConnectionOptions
{
    /// <summary>Gets the largest RakNet MTU value offered during connection negotiation, in bytes.</summary>
    /// <value>The negotiation ceiling, defaulting to the fork maximum of 1492 bytes; the usable RakNet payload is smaller after transport overhead.</value>
    public ushort MaximumMtu { get; init; } = global::RakNet.RakNetConstants.MaxMTUSize;

    /// <summary>Gets the largest single logical message the session accepts, in bytes.</summary>
    /// <value>The per-message ceiling, defaulting to 8 MiB.</value>
    public int MaximumMessageBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Gets how many inbound messages may wait for application consumption.</summary>
    /// <value>The inbound message count bound, defaulting to 1024.</value>
    public int MaximumInboundMessages { get; init; } = 1024;

    /// <summary>Gets how many payload bytes may wait for application consumption.</summary>
    /// <value>The inbound byte bound, defaulting to 16 MiB.</value>
    public int MaximumInboundBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Gets how long the oldest inbound message may remain unconsumed before the session terminates with a capacity failure.</summary>
    /// <value>The maximum age of the oldest queued inbound message, defaulting to 30 seconds.</value>
    public TimeSpan MaximumInboundQueueAge { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets how many outbound messages may wait for transmission.</summary>
    /// <value>The outbound message count bound, defaulting to 1024.</value>
    public int MaximumOutboundMessages { get; init; } = 1024;

    /// <summary>Gets how many outbound payload bytes may wait for transmission.</summary>
    /// <value>The outbound byte bound, defaulting to 16 MiB.</value>
    public int MaximumOutboundBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Gets how many unacknowledged reliable bytes the session retains for retransmit.</summary>
    /// <value>The retransmit byte bound, defaulting to 16 MiB.</value>
    public int MaximumPendingReliableBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Gets how many unacknowledged reliable datagrams the session retains for retransmit.</summary>
    /// <value>The retransmit datagram bound, defaulting to 4096.</value>
    public int MaximumPendingReliableDatagrams { get; init; } = 4096;

    internal global::RakNet.RakNetLimits ToLimits()
    {
        return new global::RakNet.RakNetLimits(
            maxPendingReliableDatagrams: MaximumPendingReliableDatagrams,
            maxPendingReliableBytes: MaximumPendingReliableBytes,
            maxOutboundMessages: MaximumOutboundMessages,
            maxOutboundBytes: MaximumOutboundBytes,
            maxInboundMessages: MaximumInboundMessages,
            maxInboundBytes: MaximumInboundBytes,
            maxLogicalMessageBytes: MaximumMessageBytes,
            maxInboundQueueAge: MaximumInboundQueueAge);
    }
}
