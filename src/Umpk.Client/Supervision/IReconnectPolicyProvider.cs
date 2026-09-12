namespace Umpk.Client;

/// <summary>The plugin-overridable reconnect seam. A <see cref="UmpkClientSupervisor"/> consults its provider each time a session ends unexpectedly to decide whether (and how) to auto-reconnect.</summary>
public interface IReconnectPolicyProvider
{
    /// <summary>Returns the reconnect policy to apply, or <see langword="null"/> for no auto-reconnect. Called on each reconnect decision, and again after each failed retry attempt, so a provider may change its answer over time - including live, mid-loop, by returning a different policy or null on the re-pull.</summary>
    ReconnectPolicy? GetReconnectPolicy();
}

/// <summary>A provider that always answers the same policy (or always answers no reconnect).</summary>
public sealed class FixedReconnectPolicyProvider : IReconnectPolicyProvider
{
    private readonly ReconnectPolicy? _policy;

    /// <summary>Creates the provider from a fixed policy, or null to disable reconnect outright.</summary>
    public FixedReconnectPolicyProvider(ReconnectPolicy? policy) => _policy = policy;

    /// <inheritdoc />
    public ReconnectPolicy? GetReconnectPolicy() => _policy;
}
