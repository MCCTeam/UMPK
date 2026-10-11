using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.Authentication;

/// <summary>Selects the credential source for a Bedrock login attempt.</summary>
/// <remarks>Only offline acquisition is available; online mode fails closed because service-chain trust is not pinned for protocol 2169.</remarks>
internal enum BedrockAuthenticationMode
{
    Offline,
    Online,
}

/// <summary>Names the credential, token, handshake, and client-data failures a Bedrock login attempt can report.</summary>
internal enum BedrockAuthenticationFailureCode
{
    AuthenticationModeMismatch,
    CredentialAcquisitionFailed,
    StaleCredentialResult,
    OnlineAuthenticationUnavailable,
    InvalidArtifact,
    InvalidAlgorithm,
    InvalidCurve,
    InvalidSignature,
    InvalidIssuer,
    InvalidAudience,
    ExpiredArtifact,
    ArtifactNotYetValid,
    ArtifactLifetimeExceeded,
    ConnectionKeyMismatch,
    InvalidClientData,
    InvalidHandshake,
}

/// <summary>Reports one Bedrock authentication failure with its machine-readable code.</summary>
internal sealed class BedrockAuthenticationException(BedrockAuthenticationFailureCode code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    internal BedrockAuthenticationFailureCode Code { get; } = code;
}

/// <summary>Describes one credential acquisition for a protocol-2169 Bedrock join attempt.</summary>
internal sealed record BedrockCredentialRequest(
    BedrockAuthenticationMode Mode,
    BedrockProtocolProfile Protocol,
    string ServerAddress,
    string? LoginHint)
{
    /// <summary>Requires a protocol-2169 profile and a non-empty server address before any acquisition starts.</summary>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(ServerAddress);
        if (Protocol.Version.Protocol != 2169)
            throw new ArgumentException("The credential request must use Bedrock protocol 2169.", nameof(Protocol));
    }
}

/// <summary>Carries one acquired credential with its display identity and optional service material.</summary>
/// <remarks>Possession of these artifacts proves nothing about platform identity or server acceptance; offline artifacts carry a local GUID and display name only.</remarks>
internal sealed class BedrockIdentityArtifacts
{
    internal BedrockIdentityArtifacts(
        BedrockAuthenticationMode mode,
        Guid identity,
        string displayName,
        string? xuid,
        IReadOnlyList<string> serviceCertificateChain,
        string? multiplayerToken,
        DateTimeOffset? expiresAt)
    {
        if (identity == Guid.Empty)
            throw new ArgumentException("The Bedrock identity must not be empty.", nameof(identity));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (displayName.Length > 64)
            throw new ArgumentException("The display name cannot exceed 64 characters.", nameof(displayName));
        ArgumentNullException.ThrowIfNull(serviceCertificateChain);

        Mode = mode;
        Identity = identity;
        DisplayName = displayName;
        Xuid = xuid;
        ServiceCertificateChain = serviceCertificateChain.ToArray();
        MultiplayerToken = multiplayerToken;
        ExpiresAt = expiresAt;
    }

    internal BedrockAuthenticationMode Mode { get; }

    internal Guid Identity { get; }

    internal string DisplayName { get; }

    internal string? Xuid { get; }

    internal IReadOnlyList<string> ServiceCertificateChain { get; }

    internal string? MultiplayerToken { get; }

    internal DateTimeOffset? ExpiresAt { get; }

    public override string ToString() => $"{nameof(BedrockIdentityArtifacts)} {{ Mode = {Mode}, Identity = {Identity}, DisplayName = {DisplayName}, CertificateCount = {ServiceCertificateChain.Count}, HasMultiplayerToken = {MultiplayerToken is not null}, ExpiresAt = {ExpiresAt} }}";
}

/// <summary>Describes one credential acquisition failure with its machine-readable code.</summary>
internal readonly record struct BedrockCredentialFailure(BedrockAuthenticationFailureCode Code, string Message);

/// <summary>Reports either one acquired credential or the failure that stopped acquisition.</summary>
internal readonly record struct BedrockCredentialResult
{
    private BedrockCredentialResult(BedrockIdentityArtifacts? artifacts, BedrockCredentialFailure? failure)
    {
        Artifacts = artifacts;
        Failure = failure;
    }

    internal bool IsSuccess => Artifacts is not null;

    internal BedrockIdentityArtifacts? Artifacts { get; }

    internal BedrockCredentialFailure? Failure { get; }

    internal static BedrockCredentialResult Success(BedrockIdentityArtifacts artifacts) => new(artifacts ?? throw new ArgumentNullException(nameof(artifacts)), null);

    internal static BedrockCredentialResult Failed(BedrockAuthenticationFailureCode code, string message) => new(null, new BedrockCredentialFailure(code, message));
}

/// <summary>Acquires credential material for a Bedrock login attempt using the caller's connection public key as input.</summary>
/// <remarks>Implementations must honor the request mode and treat the supplied connection public key as a borrowed view rather than owned bytes.</remarks>
internal interface IBedrockCredentialProvider
{
    BedrockAuthenticationMode Mode { get; }

    /// <summary>Acquires one credential for the request while borrowing the connection public key for key binding.</summary>
    ValueTask<BedrockCredentialResult> AcquireAsync(
        BedrockCredentialRequest request,
        ReadOnlyMemory<byte> connectionPublicKeySubjectPublicKeyInfo,
        CancellationToken cancellationToken);
}

/// <summary>Supplies local offline identity material with no platform account behind it.</summary>
/// <remarks>The provider accepts display names up to 64 UTF-16 code units as a local bound only rather than any server policy; one exact-BDS offline leg accepted a tested 16-character ASCII name and rejected its 17-character extension, while Unicode shapes and other-server policy remain unknown.</remarks>
internal sealed class OfflineBedrockCredentialProvider(string displayName, Guid identity) : IBedrockCredentialProvider
{
    private readonly string _displayName = string.IsNullOrWhiteSpace(displayName) || displayName.Length > 64
        ? throw new ArgumentException("The display name must contain between 1 and 64 characters.", nameof(displayName))
        : displayName;
    private readonly Guid _identity = identity == Guid.Empty ? throw new ArgumentException("The identity must not be empty.", nameof(identity)) : identity;

    public BedrockAuthenticationMode Mode => BedrockAuthenticationMode.Offline;

    /// <summary>Returns the local offline credential after checking cancellation, the request shape, and the connection key.</summary>
    /// <remarks>The returned identity comes from the values supplied when this provider was created and proves nothing about platform identity or server acceptance.</remarks>
    public ValueTask<BedrockCredentialResult> AcquireAsync(
        BedrockCredentialRequest request,
        ReadOnlyMemory<byte> connectionPublicKeySubjectPublicKeyInfo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (connectionPublicKeySubjectPublicKeyInfo.IsEmpty)
            throw new ArgumentException("The connection public key is required.", nameof(connectionPublicKeySubjectPublicKeyInfo));

        return ValueTask.FromResult(BedrockCredentialResult.Success(new BedrockIdentityArtifacts(
            BedrockAuthenticationMode.Offline,
            _identity,
            _displayName,
            "0",
            [],
            null,
            null)));
    }
}

/// <summary>Stands in for the unavailable online credential path and fails every acquisition closed.</summary>
/// <remarks>Online Bedrock authentication stays explicitly unavailable because service-chain trust is not pinned for protocol 2169.</remarks>
internal sealed class UnavailableOnlineBedrockCredentialProvider(string reason) : IBedrockCredentialProvider
{
    private readonly string _reason = string.IsNullOrWhiteSpace(reason) ? "Online Bedrock authentication is unavailable." : reason;

    public BedrockAuthenticationMode Mode => BedrockAuthenticationMode.Online;

    /// <summary>Refuses one online credential acquisition without contacting any service.</summary>
    public ValueTask<BedrockCredentialResult> AcquireAsync(
        BedrockCredentialRequest request,
        ReadOnlyMemory<byte> connectionPublicKeySubjectPublicKeyInfo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (connectionPublicKeySubjectPublicKeyInfo.IsEmpty)
            throw new ArgumentException("The connection public key is required.", nameof(connectionPublicKeySubjectPublicKeyInfo));

        return ValueTask.FromResult(BedrockCredentialResult.Failed(BedrockAuthenticationFailureCode.OnlineAuthenticationUnavailable, _reason));
    }
}

/// <summary>Acquires one credential only while the calling session generation stays current.</summary>
internal static class BedrockCredentialAcquisition
{
    /// <summary>Acquires one credential only while the calling session generation stays current across the provider await.</summary>
    /// <remarks>A mode mismatch fails without calling the provider, staleness is checked both before and after the await so a superseded session never receives a credential, and cancellation is observed before either check resolves.</remarks>
    internal static async ValueTask<BedrockCredentialResult> AcquireAsync(
        IBedrockCredentialProvider provider,
        BedrockCredentialRequest request,
        ReadOnlyMemory<byte> connectionPublicKeySubjectPublicKeyInfo,
        long sessionGeneration,
        Func<long> currentGeneration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(currentGeneration);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Mode != provider.Mode)
            return BedrockCredentialResult.Failed(BedrockAuthenticationFailureCode.AuthenticationModeMismatch, "The selected authentication mode does not match the credential provider.");
        if (sessionGeneration != currentGeneration())
            return BedrockCredentialResult.Failed(BedrockAuthenticationFailureCode.StaleCredentialResult, "Credential acquisition belongs to a stale session generation.");

        BedrockCredentialResult result = await provider.AcquireAsync(request, connectionPublicKeySubjectPublicKeyInfo, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return sessionGeneration == currentGeneration()
            ? result
            : BedrockCredentialResult.Failed(BedrockAuthenticationFailureCode.StaleCredentialResult, "Credential acquisition completed for a stale session generation.");
    }
}
