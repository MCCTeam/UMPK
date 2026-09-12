using Umpk.Protocol.Java.Signing;

namespace Umpk.Auth.Persistence;

/// <summary>The on-disk shape of a cached <see cref="JavaSession"/>. A closed DTO keeps the persisted JSON contract stable and independent of the public record's members.</summary>
public sealed record PersistedSession(
    Guid ProfileId,
    string ProfileName,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string? RefreshToken,
    AuthKind Kind);

/// <summary>The on-disk shape of cached <see cref="PlayerCertificates"/>.</summary>
public sealed record PersistedCertificates(
    string PublicKeyPem,
    string PrivateKeyPem,
    string PublicKeySignature,
    string PublicKeySignatureV2,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RefreshedAfter);

/// <summary>A standalone refresh-token record, used when only the refresh token needs to survive independently of a full session (for example after an access token expires).</summary>
public sealed record PersistedRefreshToken(string RefreshToken, string? LoginHint);
