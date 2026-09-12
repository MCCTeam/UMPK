namespace Umpk.Protocol.Java;

/// <summary>Why a marker is allowed to exist. Every allowance must pick one and justify it in prose.</summary>
internal enum MarkerReason
{
    /// <summary>The frame is genuinely served by a path that is not a codec, and a codec would duplicate work that already runs. The bar is high: the alternative path must be named and must actually be exercised.</summary>
    HandledElsewhere,

    /// <summary>A codec exists on a neighbouring era but its wire is different here, so binding it would mis-frame the packet. A marker relays verbatim and never faults; a wrong codec desyncs or kills the session. This is the only reason that is an argument FOR a marker rather than an admission.</summary>
    WrongCodecWouldBeWorse,

    /// <summary>The dataset registers a packet vanilla does not send in that phase and flow. A marker is correct because there is nothing to decode; the row itself is the thing that is wrong.</summary>
    DatasetArtifact,

    /// <summary>Developer or debug tooling a vanilla server only emits to a client that opted in, or a harness packet with no gameplay meaning. Nothing would read the decoded value.</summary>
    NoConsumerWorthWriting,

    /// <summary>An explicit capability gap: no codec, no consumer, and no misbinding. Every entry states what a consumer loses.</summary>
    KnownGap,
}
