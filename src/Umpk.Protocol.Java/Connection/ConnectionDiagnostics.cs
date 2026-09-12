using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Umpk.Protocol.Java;

/// <summary>Static metric instruments and activity source for connection observability. Hosts that do not wire OpenTelemetry pay only the counter increments. The <see cref="Meter"/> and <see cref="ActivitySource"/> are the public hooks a listener subscribes to by name.</summary>
public static class ConnectionDiagnostics
{
    /// <summary>The meter name observers subscribe to.</summary>
    public const string MeterName = "Umpk.Protocol.Java";

    /// <summary>The activity source name for connect/login spans.</summary>
    public const string ActivitySourceName = "Umpk.Protocol.Java";

    internal static readonly Meter Meter = new(MeterName);

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    internal static readonly Counter<long> BytesIn =
        Meter.CreateCounter<long>("umpk.connection.bytes_in");

    internal static readonly Counter<long> BytesOut =
        Meter.CreateCounter<long>("umpk.connection.bytes_out");

    internal static readonly Counter<long> PacketsIn =
        Meter.CreateCounter<long>("umpk.connection.packets_in");

    internal static readonly Counter<long> PacketsOut =
        Meter.CreateCounter<long>("umpk.connection.packets_out");

    internal static readonly Counter<long> DecodeErrors =
        Meter.CreateCounter<long>("umpk.connection.decode_errors");

    /// <summary>Item stacks whose wire id the session item registry could not resolve and that were degraded to the <c>minecraft:unknown</c> placeholder instead of faulting the connection (legacy 1.8-1.12.2 slots only). A non-zero count means the version's item table has a gap, not that the session is unhealthy.</summary>
    internal static readonly Counter<long> UnknownItems =
        Meter.CreateCounter<long>("umpk.connection.unknown_items");

    /// <summary>Packets dropped because an item component patch named a component the era knows but UMPK does not model, on the compact (non-length-delimited) wire where its payload cannot be skipped. The session survives; the packet does not. A non-zero count means the component table has a gap, so some inventory data was not observed. It never indicates a framing fault: an out-of-range component id raises <see cref="ProtocolViolationException"/> and still closes the connection.</summary>
    internal static readonly Counter<long> UnmodeledComponents =
        Meter.CreateCounter<long>("umpk.connection.unmodeled_components");
}
