using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Umpk.Client;

/// <summary>Tuning knobs for a <see cref="UmpkClientSupervisor"/>.</summary>
public sealed class ClientSupervisorOptions
{
    /// <summary>The reconnect policy source. Null (the default) means no automatic reconnect: a session that ends unexpectedly goes straight to a terminal <see cref="ClientStatus.Disconnected"/>.</summary>
    public IReconnectPolicyProvider? ReconnectPolicy { get; set; }

    /// <summary>Logger factory for supervisor diagnostics. Defaults to a no-op logger.</summary>
    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    /// <summary>The time source the reconnect backoff delay is measured against. Defaults to the system clock.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>How long an <see cref="IClientExtension.DeactivateAsync"/> call is given before it is abandoned: its <see cref="ClientExtensionContext.Deactivated"/> token is cancelled and teardown continues without it. Long enough for well-behaved cleanup; short enough that a wedged extension cannot hold <see cref="ClientExtensionCollection.RemoveAsync"/> or <see cref="UmpkClientSupervisor.StopAsync"/> hostage. Defaults to 5 seconds.</summary>
    public TimeSpan ExtensionTeardownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Whether server-directed transfer packets are followed automatically. Defaults off.</summary>
    public bool FollowServerTransfers { get; set; }

    /// <summary>Maximum consecutive server-directed transfer hops. Defaults to eight.</summary>
    public int MaxTransferHops { get; set; } = 8;
}
