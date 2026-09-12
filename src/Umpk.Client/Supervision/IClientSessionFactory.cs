namespace Umpk.Client;

/// <summary>Builds a <see cref="UmpkClient"/> for one <see cref="SessionAttempt"/>, and releases it once the supervisor is done with it. A factory rather than a single client, because a <see cref="UmpkClient"/> resolves its version, wire index, applier catalog, physics profile and identity at construction: a reconnect that switches account, or that lands on a server which changed version, has to build a new one.</summary>
public interface IClientSessionFactory
{
    /// <summary>Builds a client for the given attempt. The returned client must be unconnected: the supervisor calls <see cref="UmpkClient.ConnectAsync"/> itself, immediately afterward.</summary>
    ValueTask<UmpkClient> CreateAsync(SessionAttempt attempt, CancellationToken ct);

    /// <summary>Releases a client the supervisor is done with, either because its session ended or because the attempt that built it failed. <paramref name="disconnect"/> carries the reason when one is known (null for a factory-side connect failure that never reached a session). The default disposes the client; a host that wants to reuse one instance across reconnects overrides this with <see cref="UmpkClient.DisconnectAsync"/> instead.</summary>
    ValueTask ReleaseAsync(UmpkClient client, DisconnectInfo? disconnect, CancellationToken ct) => client.DisposeAsync();
}
