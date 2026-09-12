namespace Umpk;

/// <summary>A profile plus the bearer token that proves ownership of it, as produced by an authenticator and consumed by session join. The token is a secret: it is excluded from <see cref="ToString"/> and must never be logged.</summary>
public sealed record ProfileCredentials(GameProfile Profile, string AccessToken)
{
    /// <summary>Redacts the access token; only the profile is printed.</summary>
    public override string ToString() => $"ProfileCredentials {{ Profile = {Profile}, AccessToken = <redacted> }}";
}
