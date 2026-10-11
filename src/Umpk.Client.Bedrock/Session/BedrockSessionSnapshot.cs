namespace Umpk.Client.Bedrock.Session;

/// <summary>Captures one frozen view of join progress, registry state, local identity, and actor observation counts.</summary>
/// <remarks>Readiness records completed bootstrap obligations, while world knowledge, spawn permission, and actor observations remain separate; a null termination does not by itself prove a live transport.</remarks>
internal sealed record BedrockSessionSnapshot(
    bool TransportConnected,
    bool CompressionInstalled,
    bool LoginSent,
    bool EncryptionInstalled,
    bool IdentityAccepted,
    bool ResourcesComplete,
    bool WorldKnown,
    bool SpawnAuthorized,
    bool ItemRegistryAvailable,
    bool BiomeRegistryAvailable,
    bool EntityRegistryAvailable,
    bool LocalPlayerInitializationSent,
    bool LocalPlayerInitializationAccepted,
    bool IsReady,
    long LocalEntityId,
    ulong LocalRuntimeEntityId,
    int LevelChunksObserved,
    int ActorCount,
    int ActorWorldGeneration,
    bool ActorObservationsAvailable,
    BedrockTermination? Termination);
