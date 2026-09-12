using Umpk.Client.State;

namespace Umpk.Client.Snapshots;

/// <summary>Session-level facts.</summary>
/// <param name="Host">The connected server host, or empty before a version/endpoint has been negotiated.</param>
/// <param name="Port">The connected server port, or 0 before negotiation.</param>
/// <param name="VersionName">The negotiated Minecraft version name, or empty before negotiation.</param>
/// <param name="Protocol">The negotiated protocol number, or 0 before negotiation.</param>
/// <param name="ObservedLatencyMs"><see cref="ClientState.ObservedLatency"/>: the server-measured round trip in milliseconds, or null before the server reports one. Never confuse it with <paramref name="KeepAliveTurnaround"/>.</param>
/// <param name="Brand">The server brand (<c>vanilla</c>, <c>Paper</c>, ...) from the <c>minecraft:brand</c> plugin channel (<c>MC|Brand</c> before 1.13), or null until the server announces it.</param>
/// <param name="TpsEstimate">The measured server ticks-per-second, or null when it is NOT KNOWN. Never shown as zero: a server paused by 1.21.2+ pause-when-empty or held by <c>/tick freeze</c> stops broadcasting game time, so the estimate expires back to null rather than decaying toward zero. The client genuinely cannot tell a paused server from an unreachable one.</param>
/// <param name="KeepAliveTurnaround">How long this client took to answer the last keep-alive, or null before the first one. This is OUR OWN responder-side turnaround, NOT a round trip: a Java client is only ever the responder on the keep-alive exchange and the id is the server's own clock reading, so it cannot measure RTT. Useful for spotting a stalled session loop, not for reporting ping.</param>
/// <param name="KeepAliveInterval">The gap between the last two clientbound keep-alives, or null before the second one. A vanilla server aims for 15 seconds, so a materially longer gap means the server is behind on its own network tick.</param>
public sealed record SessionSnapshot(
    string Host,
    int Port,
    string VersionName,
    int Protocol,
    int? ObservedLatencyMs,
    string? Brand,
    double? TpsEstimate,
    TimeSpan? KeepAliveTurnaround,
    TimeSpan? KeepAliveInterval)
{
    /// <summary>Projects a <see cref="SessionSnapshot"/> from live tracked state. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static SessionSnapshot Project(ClientState state, SessionInfo? session)
    {
        ArgumentNullException.ThrowIfNull(state);
        ServerState server = state.Server;
        return new SessionSnapshot(
            session?.Endpoint.Host ?? string.Empty,
            session?.Endpoint.Port ?? 0,
            session?.Version.Version.Name ?? string.Empty,
            session?.Version.Version.Protocol ?? 0,
            state.ObservedLatency,
            server.Brand,
            server.TicksPerSecond,
            server.KeepAliveTurnaround,
            server.KeepAliveInterval);
    }
}
