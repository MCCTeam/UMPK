using System.Collections.Immutable;

namespace Umpk.Client.Bedrock.Bootstrap;

internal enum BedrockBootstrapEventKind
{
    Connected,
    NetworkSettingsAccepted,
    CredentialsReady,
    ServerHandshakeValidated,
    LoginSucceeded,
    ResourcePackOfferReceived,
    ResourceNegotiationCompleted,
    StartGame,
    PlayerSpawnAuthorized,
    ItemRegistryPublished,
    BiomeRegistryPublished,
    EntityRegistryPublished,
    LocalPlayerInitializationAccepted,
    Disconnected,
}

internal readonly record struct BedrockBootstrapEvent(BedrockBootstrapEventKind Kind, ulong RuntimeEntityId = 0, string? Detail = null)
{
    internal static BedrockBootstrapEvent Connected() => new(BedrockBootstrapEventKind.Connected);

    internal static BedrockBootstrapEvent NetworkSettingsAccepted() => new(BedrockBootstrapEventKind.NetworkSettingsAccepted);

    internal static BedrockBootstrapEvent CredentialsReady() => new(BedrockBootstrapEventKind.CredentialsReady);

    internal static BedrockBootstrapEvent ServerHandshakeValidated() => new(BedrockBootstrapEventKind.ServerHandshakeValidated);

    internal static BedrockBootstrapEvent LoginSucceeded() => new(BedrockBootstrapEventKind.LoginSucceeded);

    internal static BedrockBootstrapEvent ResourcePackOfferReceived() => new(BedrockBootstrapEventKind.ResourcePackOfferReceived);

    internal static BedrockBootstrapEvent ResourceNegotiationCompleted() => new(BedrockBootstrapEventKind.ResourceNegotiationCompleted);

    internal static BedrockBootstrapEvent StartGame(ulong runtimeEntityId) => new(BedrockBootstrapEventKind.StartGame, runtimeEntityId);

    internal static BedrockBootstrapEvent PlayerSpawnAuthorized() => new(BedrockBootstrapEventKind.PlayerSpawnAuthorized);

    internal static BedrockBootstrapEvent ItemRegistryPublished() => new(BedrockBootstrapEventKind.ItemRegistryPublished);

    internal static BedrockBootstrapEvent BiomeRegistryPublished() => new(BedrockBootstrapEventKind.BiomeRegistryPublished);

    internal static BedrockBootstrapEvent EntityRegistryPublished() => new(BedrockBootstrapEventKind.EntityRegistryPublished);

    internal static BedrockBootstrapEvent LocalPlayerInitializationAccepted(ulong runtimeEntityId) => new(BedrockBootstrapEventKind.LocalPlayerInitializationAccepted, runtimeEntityId);

    internal static BedrockBootstrapEvent Disconnected(string? reason) => new(BedrockBootstrapEventKind.Disconnected, Detail: reason);
}

internal enum BedrockBootstrapEffectKind
{
    SendNetworkSettingsRequest,
    InstallCompression,
    AcquireCredentials,
    SendLogin,
    InstallEncryption,
    SendClientHandshake,
    EvaluateResourcePacks,
    SendLocalPlayerInitialized,
    PublishReady,
}

internal readonly record struct BedrockBootstrapEffect(BedrockBootstrapEffectKind Kind, ulong RuntimeEntityId = 0);

internal enum BedrockBootstrapFailureCode
{
    IllegalTransition,
    DuplicateObligation,
    InvalidRuntimeEntity,
    RemoteDisconnect,
}

internal readonly record struct BedrockBootstrapFailure(BedrockBootstrapFailureCode Code, string Message);

internal sealed record BedrockBootstrapState
{
    internal static BedrockBootstrapState Initial { get; } = new();

    internal bool TransportConnected { get; init; }

    internal bool CompressionInstalled { get; init; }

    internal bool CredentialsAvailable { get; init; }

    internal bool LoginSent { get; init; }

    internal bool EncryptionInstalled { get; init; }

    internal bool IdentityAccepted { get; init; }

    internal bool ResourceOfferSeen { get; init; }

    internal bool ResourcesComplete { get; init; }

    internal bool WorldKnown { get; init; }

    internal bool SpawnAuthorized { get; init; }

    internal bool ItemRegistryAvailable { get; init; }

    internal bool BiomeRegistryAvailable { get; init; }

    internal bool EntityRegistryAvailable { get; init; }

    internal bool LocalPlayerInitializationSent { get; init; }

    internal bool LocalPlayerInitializationAccepted { get; init; }

    internal bool IsReady { get; init; }

    internal bool IsTerminal { get; init; }

    internal ulong LocalRuntimeEntityId { get; init; }
}

internal readonly record struct BedrockBootstrapTransition(
    BedrockBootstrapState State,
    ImmutableArray<BedrockBootstrapEffect> Effects,
    BedrockBootstrapFailure? Failure);

internal static class BedrockBootstrapPolicy
{
    internal static BedrockBootstrapTransition Apply(BedrockBootstrapState state, BedrockBootstrapEvent @event)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (@event.Kind == BedrockBootstrapEventKind.Disconnected)
            return Fail(state, BedrockBootstrapFailureCode.RemoteDisconnect, SanitizeDisconnect(@event.Detail));
        if (state.IsTerminal)
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "The bootstrap session is already terminal.");

        return @event.Kind switch
        {
            BedrockBootstrapEventKind.Connected => Connect(state),
            BedrockBootstrapEventKind.NetworkSettingsAccepted => AcceptNetworkSettings(state),
            BedrockBootstrapEventKind.CredentialsReady => AcceptCredentials(state),
            BedrockBootstrapEventKind.ServerHandshakeValidated => AcceptHandshake(state),
            BedrockBootstrapEventKind.LoginSucceeded => AcceptLogin(state),
            BedrockBootstrapEventKind.ResourcePackOfferReceived => AcceptResourceOffer(state),
            BedrockBootstrapEventKind.ResourceNegotiationCompleted => CompleteResources(state),
            BedrockBootstrapEventKind.StartGame => AcceptStartGame(state, @event.RuntimeEntityId),
            BedrockBootstrapEventKind.PlayerSpawnAuthorized => AcceptSpawn(state),
            BedrockBootstrapEventKind.ItemRegistryPublished => PublishRegistry(state, BedrockBootstrapEventKind.ItemRegistryPublished),
            BedrockBootstrapEventKind.BiomeRegistryPublished => PublishRegistry(state, BedrockBootstrapEventKind.BiomeRegistryPublished),
            BedrockBootstrapEventKind.EntityRegistryPublished => PublishRegistry(state, BedrockBootstrapEventKind.EntityRegistryPublished),
            BedrockBootstrapEventKind.LocalPlayerInitializationAccepted => AcceptInitialization(state, @event.RuntimeEntityId),
            _ => Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "The bootstrap event is not supported."),
        };
    }

    private static BedrockBootstrapTransition Connect(BedrockBootstrapState state)
    {
        if (state.TransportConnected)
            return Fail(state, BedrockBootstrapFailureCode.DuplicateObligation, "The transport was already connected.");
        return Success(state with { TransportConnected = true }, new BedrockBootstrapEffect(BedrockBootstrapEffectKind.SendNetworkSettingsRequest));
    }

    private static BedrockBootstrapTransition AcceptNetworkSettings(BedrockBootstrapState state)
    {
        if (!state.TransportConnected)
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "Network settings arrived before transport connection.");
        if (state.CompressionInstalled)
            return Fail(state, BedrockBootstrapFailureCode.DuplicateObligation, "Network settings were already accepted.");
        if (state.LoginSent)
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "Network settings arrived after Login.");
        return Success(
            state with { CompressionInstalled = true },
            new BedrockBootstrapEffect(BedrockBootstrapEffectKind.InstallCompression),
            new BedrockBootstrapEffect(BedrockBootstrapEffectKind.AcquireCredentials));
    }

    private static BedrockBootstrapTransition AcceptCredentials(BedrockBootstrapState state)
    {
        if (!state.CompressionInstalled)
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "Credentials became ready before network settings.");
        if (state.LoginSent)
            return Fail(state, BedrockBootstrapFailureCode.DuplicateObligation, "Login was already sent.");
        return Success(
            state with { CredentialsAvailable = true, LoginSent = true },
            new BedrockBootstrapEffect(BedrockBootstrapEffectKind.SendLogin));
    }

    private static BedrockBootstrapTransition AcceptHandshake(BedrockBootstrapState state)
    {
        if (!state.LoginSent || state.IdentityAccepted)
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "The server handshake is outside the login window.");
        if (state.EncryptionInstalled)
            return Fail(state, BedrockBootstrapFailureCode.DuplicateObligation, "Encryption was already installed.");
        return Success(
            state with { EncryptionInstalled = true },
            new BedrockBootstrapEffect(BedrockBootstrapEffectKind.InstallEncryption),
            new BedrockBootstrapEffect(BedrockBootstrapEffectKind.SendClientHandshake));
    }

    private static BedrockBootstrapTransition AcceptLogin(BedrockBootstrapState state)
    {
        if (!state.LoginSent)
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "Login success arrived before Login was sent.");
        if (state.IdentityAccepted)
            return Fail(state, BedrockBootstrapFailureCode.DuplicateObligation, "Login success was already accepted.");
        return Success(state with { IdentityAccepted = true });
    }

    private static BedrockBootstrapTransition AcceptResourceOffer(BedrockBootstrapState state)
    {
        if (!state.IdentityAccepted || state.ResourcesComplete)
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "The resource offer is outside resource negotiation.");
        if (state.ResourceOfferSeen)
            return Fail(state, BedrockBootstrapFailureCode.DuplicateObligation, "A resource offer is already active.");
        return Success(state with { ResourceOfferSeen = true }, new BedrockBootstrapEffect(BedrockBootstrapEffectKind.EvaluateResourcePacks));
    }

    private static BedrockBootstrapTransition CompleteResources(BedrockBootstrapState state)
    {
        if (!state.IdentityAccepted)
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "Resource negotiation completed before login success.");
        if (state.ResourcesComplete)
            return Fail(state, BedrockBootstrapFailureCode.DuplicateObligation, "Resource negotiation was already complete.");
        return EvaluateReadiness(state with { ResourcesComplete = true });
    }

    private static BedrockBootstrapTransition AcceptStartGame(BedrockBootstrapState state, ulong runtimeEntityId)
    {
        if (!CanStream(state))
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "StartGame arrived before bootstrap streaming.");
        if (state.WorldKnown)
            return Fail(state, BedrockBootstrapFailureCode.DuplicateObligation, "StartGame was already accepted.");
        if (runtimeEntityId == 0)
            return Fail(state, BedrockBootstrapFailureCode.InvalidRuntimeEntity, "StartGame did not contain a local runtime entity ID.");
        return EvaluateInitialization(state with { WorldKnown = true, LocalRuntimeEntityId = runtimeEntityId });
    }

    private static BedrockBootstrapTransition AcceptSpawn(BedrockBootstrapState state)
    {
        if (!CanStream(state))
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "Player spawn arrived before bootstrap streaming.");
        if (state.SpawnAuthorized)
            // Retransmitted spawn authorizations must not fail the session.
            return Success(state);
        return EvaluateInitialization(state with { SpawnAuthorized = true });
    }
    private static BedrockBootstrapTransition PublishRegistry(BedrockBootstrapState state, BedrockBootstrapEventKind kind)
    {
        if (!CanStream(state))
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "A registry arrived before bootstrap streaming.");
        BedrockBootstrapState updated = kind switch
        {
            BedrockBootstrapEventKind.ItemRegistryPublished when !state.ItemRegistryAvailable => state with { ItemRegistryAvailable = true },
            BedrockBootstrapEventKind.BiomeRegistryPublished when !state.BiomeRegistryAvailable => state with { BiomeRegistryAvailable = true },
            BedrockBootstrapEventKind.EntityRegistryPublished when !state.EntityRegistryAvailable => state with { EntityRegistryAvailable = true },
            // A repeated registry publication is a retransmission, not a protocol violation.
            _ => state,
        };
        return EvaluateReadiness(updated);
    }

    private static BedrockBootstrapTransition AcceptInitialization(BedrockBootstrapState state, ulong runtimeEntityId)
    {
        if (!state.LocalPlayerInitializationSent)
            return Fail(state, BedrockBootstrapFailureCode.IllegalTransition, "Local player initialization was accepted before it was sent.");
        if (state.LocalPlayerInitializationAccepted)
            return Fail(state, BedrockBootstrapFailureCode.DuplicateObligation, "Local player initialization was already accepted.");
        if (runtimeEntityId == 0 || runtimeEntityId != state.LocalRuntimeEntityId)
            return Fail(state, BedrockBootstrapFailureCode.InvalidRuntimeEntity, "The initialized runtime entity ID does not match StartGame.");
        return EvaluateReadiness(state with { LocalPlayerInitializationAccepted = true });
    }

    private static BedrockBootstrapTransition EvaluateInitialization(BedrockBootstrapState state)
    {
        if (!state.WorldKnown || !state.SpawnAuthorized || state.LocalPlayerInitializationSent)
            return EvaluateReadiness(state);
        return Success(
            state with { LocalPlayerInitializationSent = true },
            new BedrockBootstrapEffect(BedrockBootstrapEffectKind.SendLocalPlayerInitialized, state.LocalRuntimeEntityId));
    }

    private static BedrockBootstrapTransition EvaluateReadiness(BedrockBootstrapState state)
    {
        bool ready = state.ResourcesComplete
            && state.WorldKnown
            && state.SpawnAuthorized
            && state.ItemRegistryAvailable
            && state.BiomeRegistryAvailable
            && state.EntityRegistryAvailable
            && state.LocalPlayerInitializationAccepted;
        if (!ready || state.IsReady)
            return Success(state);
        return Success(state with { IsReady = true }, new BedrockBootstrapEffect(BedrockBootstrapEffectKind.PublishReady));
    }

    private static bool CanStream(BedrockBootstrapState state) => state.IdentityAccepted && state.ResourcesComplete;

    private static BedrockBootstrapTransition Success(BedrockBootstrapState state, params BedrockBootstrapEffect[] effects) => new(state, [.. effects], null);

    private static BedrockBootstrapTransition Fail(BedrockBootstrapState state, BedrockBootstrapFailureCode code, string message) =>
        new(state with { IsTerminal = true }, [], new BedrockBootstrapFailure(code, message));

    private static string SanitizeDisconnect(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "The peer disconnected.";
        string sanitized = new(reason.Where(character => !char.IsControl(character)).Take(256).ToArray());
        return sanitized.Length == 0 ? "The peer disconnected." : sanitized;
    }
}
