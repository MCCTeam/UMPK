using System.Text;
using System.Text.Json;

namespace Umpk.Client.Bedrock.Authentication;

/// <summary>Well-known Bedrock device operating system identifiers used by the login envelope.</summary>
/// <remarks>Offline login currently sends the Linux identifier; exact-BDS single-field offline probes rejected DeviceOS 7 while accepting 12 and 15 on the Linux test host.</remarks>
internal static class BedrockDeviceOperatingSystem
{
    /// <summary>The Linux device identifier sent by offline login.</summary>
    internal const int Linux = 15;
}

/// <summary>Carries one built login envelope plus client-data token for a Bedrock join attempt.</summary>
/// <remarks>The artifacts prove local construction and connection-key binding only and say nothing about server acceptance.</remarks>
internal sealed class BedrockLoginArtifacts
{
    internal BedrockLoginArtifacts(BedrockAuthenticationMode mode, string identityEnvelope, string clientDataToken, DateTimeOffset? expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityEnvelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientDataToken);
        Mode = mode;
        IdentityEnvelope = identityEnvelope;
        ClientDataToken = clientDataToken;
        ExpiresAt = expiresAt;
    }

    internal BedrockAuthenticationMode Mode { get; }

    internal string IdentityEnvelope { get; }

    internal string ClientDataToken { get; }

    internal DateTimeOffset? ExpiresAt { get; }

    public override string ToString() => $"{nameof(BedrockLoginArtifacts)} {{ Mode = {Mode}, HasIdentityEnvelope = true, HasClientDataToken = true, ExpiresAt = {ExpiresAt} }}";
}

/// <summary>Reports either one built offline login envelope or the credential failure that stopped it.</summary>
internal readonly record struct BedrockLoginArtifactResult
{
    private BedrockLoginArtifactResult(BedrockLoginArtifacts? artifacts, BedrockCredentialFailure? failure)
    {
        Artifacts = artifacts;
        Failure = failure;
    }

    internal bool IsSuccess => Artifacts is not null;

    internal BedrockLoginArtifacts? Artifacts { get; }

    internal BedrockCredentialFailure? Failure { get; }

    internal static BedrockLoginArtifactResult Success(BedrockLoginArtifacts artifacts) => new(artifacts ?? throw new ArgumentNullException(nameof(artifacts)), null);

    internal static BedrockLoginArtifactResult Failed(BedrockCredentialFailure failure) => new(null, failure);
}

/// <summary>Builds offline-only protocol-2169 login envelopes with connection-key signatures and a local self-check before return.</summary>
/// <remarks>Online mode is refused because service-chain trust is not pinned for this protocol, and the pre-return validation proves shape and key binding only while saying nothing about server acceptance.</remarks>
internal sealed class BedrockLoginArtifactFactory(TimeProvider timeProvider)
{
    private static readonly TimeSpan OfflineTokenLifetime = TimeSpan.FromHours(1);
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>Builds one offline-only protocol-2169 login envelope plus client-data token and self-checks the pair before returning.</summary>
    /// <remarks>Online mode is refused because service-chain trust is not pinned for this protocol, the self-issued offline token lives one hour from creation, the client-data profile carries a hardcoded view distance of 4 rather than a value from the request, and local validation proves shape and key binding only while saying nothing about server acceptance.</remarks>
    internal async ValueTask<BedrockLoginArtifactResult> CreateAsync(
        BedrockCredentialRequest request,
        IBedrockCredentialProvider provider,
        BedrockConnectionKey connectionKey,
        BedrockClientSkinProfile skin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(connectionKey);
        ArgumentNullException.ThrowIfNull(skin);
        request.Validate();
        if (request.Mode != provider.Mode)
        {
            return BedrockLoginArtifactResult.Failed(new BedrockCredentialFailure(
                BedrockAuthenticationFailureCode.AuthenticationModeMismatch,
                "The selected authentication mode does not match the credential provider."));
        }

        BedrockCredentialResult credentialResult = await provider.AcquireAsync(request, connectionKey.PublicKeySubjectPublicKeyInfo, cancellationToken).ConfigureAwait(false);
        if (!credentialResult.IsSuccess)
            return BedrockLoginArtifactResult.Failed(credentialResult.Failure ?? new BedrockCredentialFailure(BedrockAuthenticationFailureCode.CredentialAcquisitionFailed, "Credential acquisition failed."));

        BedrockIdentityArtifacts identity = credentialResult.Artifacts!;
        if (identity.Mode != request.Mode)
        {
            return BedrockLoginArtifactResult.Failed(new BedrockCredentialFailure(
                BedrockAuthenticationFailureCode.AuthenticationModeMismatch,
                "The credential result does not match the selected authentication mode."));
        }

        if (request.Mode == BedrockAuthenticationMode.Online)
        {
            return BedrockLoginArtifactResult.Failed(new BedrockCredentialFailure(
                BedrockAuthenticationFailureCode.OnlineAuthenticationUnavailable,
                "Online service-chain trust and acquisition have not been pinned for protocol 2169."));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string multiplayerToken = BedrockCompactJwt.CreateOfflineToken(
            identity.DisplayName,
            identity.Identity,
            now,
            now + OfflineTokenLifetime,
            "api://auth-minecraft-services/multiplayer",
            connectionKey);
        var certificate = new BedrockCertificateEnvelope([string.Empty]);
        string certificateJson = JsonSerializer.Serialize(certificate, BedrockAuthJsonContext.Default.BedrockCertificateEnvelope);
        var envelope = new BedrockIdentityEnvelope(certificateJson, 2, multiplayerToken);
        string identityJson = JsonSerializer.Serialize(envelope, BedrockAuthJsonContext.Default.BedrockIdentityEnvelope);
        var clientProfile = new BedrockClientDataProfile(
            now.ToUnixTimeMilliseconds(),
            1,
            1,
            Guid.NewGuid(),
            "UMPK",
            BedrockDeviceOperatingSystem.Linux,
            request.Protocol.Version.Name,
            "en_GB",
            request.ServerAddress,
            identity.DisplayName,
            4);
        string clientDataToken = BedrockCompactJwt.CreateClientDataToken(clientProfile, skin, connectionKey);
        var artifacts = new BedrockLoginArtifacts(BedrockAuthenticationMode.Offline, identityJson, clientDataToken, now + OfflineTokenLifetime);
        BedrockLoginArtifactValidator.ValidateOffline(artifacts, connectionKey.PublicKeySubjectPublicKeyInfo, now);
        return BedrockLoginArtifactResult.Success(artifacts);
    }
}

/// <summary>Checks locally built offline login envelopes for size, shape, and connection-key binding without contacting any service.</summary>
internal static class BedrockLoginArtifactValidator
{
    /// <summary>Checks one locally built offline login envelope for size, shape, and connection-key binding without contacting any service.</summary>
    /// <remarks>The envelope must use authentication type 2 with exactly one empty certificate entry, duplicate or missing JSON properties fail closed, and success proves local construction only rather than server acceptance.</remarks>
    internal static BedrockOfflineMultiplayerClaims ValidateOffline(
        BedrockLoginArtifacts artifacts,
        ReadOnlyMemory<byte> expectedConnectionKey,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Mode != BedrockAuthenticationMode.Offline)
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.AuthenticationModeMismatch, "The login artifacts are not offline artifacts.");

        if (Encoding.UTF8.GetByteCount(artifacts.IdentityEnvelope) > 256 * 1024)
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidArtifact, "The identity envelope exceeds its size limit.");
        byte[] identityJson = Encoding.UTF8.GetBytes(artifacts.IdentityEnvelope);
        BedrockCompactJwt.ValidateUniqueProperties(identityJson, "Certificate", "AuthenticationType", "Token");

        BedrockIdentityEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(artifacts.IdentityEnvelope, BedrockAuthJsonContext.Default.BedrockIdentityEnvelope);
        }
        catch (JsonException exception)
        {
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidArtifact, "The identity envelope contains invalid JSON.", exception);
        }

        if (envelope is null || envelope.AuthenticationType != 2 || string.IsNullOrWhiteSpace(envelope.Token))
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidArtifact, "The offline identity envelope is invalid.");
        if (Encoding.UTF8.GetByteCount(envelope.Certificate) > 256 * 1024)
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidArtifact, "The certificate wrapper exceeds its size limit.");
        byte[] certificateJson = Encoding.UTF8.GetBytes(envelope.Certificate);
        BedrockCompactJwt.ValidateUniqueProperties(certificateJson, "chain");
        BedrockCertificateEnvelope? certificate = JsonSerializer.Deserialize(envelope.Certificate, BedrockAuthJsonContext.Default.BedrockCertificateEnvelope);
        if (certificate is null || certificate.Chain.Count != 1 || certificate.Chain[0].Length != 0)
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidArtifact, "The offline certificate wrapper is invalid.");

        BedrockOfflineMultiplayerClaims claims = BedrockCompactJwt.ValidateOfflineToken(envelope.Token, expectedConnectionKey, now);
        BedrockCompactJwt.ValidateClientDataToken(artifacts.ClientDataToken, expectedConnectionKey);
        return claims;
    }
}
