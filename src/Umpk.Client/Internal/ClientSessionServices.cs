using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Client.State;
using Umpk.Hosting;
using Umpk.Protocol.Java;

namespace Umpk.Client.Internal;

/// <summary>The shared, per-session services threaded to actions and appliers: the version, options, state, wire index, and a way to marshal onto the session loop. Read-only after session start.</summary>
internal sealed class ClientSessionServices
{
    public required JavaVersion Version { get; init; }

    public required ClientOptions Options { get; init; }

    public required ClientPolicies Policies { get; init; }

    public required ClientState State { get; init; }

    public required WireIndex Wire { get; init; }

    public required ILogger Logger { get; init; }

    public required ISessionScheduler Scheduler { get; init; }

    /// <summary>The session's event bus, when one is wired. Nullable: a hand-built harness that constructs this record directly may leave it unset. An action whose verified outcome REFUSES rather than degrades when this is null, because a poll-only read cannot tell an observed refusal (a re-asserted block, a respawn that never arrives) from silence; see <see cref="Umpk.Client.Actions.InteractionActions.DigBlockVerifiedAsync"/>.</summary>
    public EventBus? Events { get; init; }

    /// <summary>The live connection phase, or <see cref="ProtocolPhase.Play"/> before connect. Needed by the action surfaces whose packet is registered in both the configuration and play phases under separate packet identities (the 1.21.6+ dialog response).</summary>
    public Func<ProtocolPhase> CurrentPhase { get; init; } = static () => ProtocolPhase.Play;

    /// <summary>The current connection lifetime, or <see langword="null"/> when no connection exists. Live-client navigation links caller cancellation to this token so a terminal session fault cannot leave a movement operation running against a dead connection. Hand-built component tests retain their standalone lifetime through the default non-cancelable token.</summary>
    public Func<CancellationToken?> CurrentSessionCancellation { get; init; } =
        static () => CancellationToken.None;

    /// <summary>Which version-optional actions this session can actually perform. Derived from <see cref="Wire"/> rather than injected so every construction site (including the test harnesses that build services by hand) gets the same answers the live client gives.</summary>
    public ClientActionCapabilities Capabilities =>
        _capabilities ??= new ClientActionCapabilities(Wire, Version.Version.Protocol, CurrentPhase);

    private ClientActionCapabilities? _capabilities;

    /// <summary>Marshals a value-returning function onto the session loop.</summary>
    public Task<TResult> InvokeOnLoopResult<TResult>(Func<TResult> work, CancellationToken ct)
        => Scheduler.InvokeAsync(work, ct);

    /// <summary>The local player's tracked state.</summary>
    public SelfState Self => State.Self;
}
