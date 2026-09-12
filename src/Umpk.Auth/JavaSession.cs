namespace Umpk.Auth;

/// <summary>The immutable result of an authentication flow: the resolved player identity plus the bearer token that proves ownership of it. Connection-scoped state (encryption keys, signing chain) lives elsewhere; this record only carries the auth outcome.</summary>
/// <remarks>The <see cref="AccessToken"/> and <see cref="RefreshToken"/> are secrets and are excluded from <see cref="ToString"/>. They must never be written to logs or exception messages.</remarks>
public sealed record JavaSession(
    GameProfile Profile,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string? RefreshToken,
    AuthKind Kind)
{
    /// <summary>True when <see cref="ExpiresAt"/> is at or before <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    /// <summary>Redacts both tokens; prints only the profile, expiry, and kind.</summary>
    public override string ToString() =>
        $"JavaSession {{ Profile = {Profile}, ExpiresAt = {ExpiresAt:O}, Kind = {Kind}, " +
        "AccessToken = <redacted>, RefreshToken = <redacted> }";
}
