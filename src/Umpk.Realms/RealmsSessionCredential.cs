using System.Globalization;
using Umpk.Auth;

namespace Umpk.Realms;

/// <summary>The resolved auth material Realms REST needs, derived from a completed <see cref="JavaSession"/>. Java Realms authenticates with the Minecraft-services access token (the same token the XSTS <c>rp://api.minecraftservices.com/</c> chain produces) carried in a session cookie alongside the player UUID, username, and client version. No Realms-specific XSTS relying party is required.</summary>
/// <remarks><see cref="AccessToken"/> is a secret and is excluded from <see cref="ToString"/>. It must never be written to logs or exception messages. This mirrors the <see cref="JavaSession"/> redaction pattern.</remarks>
/// <param name="AccessToken">The Minecraft-services access token that proves ownership of the profile.</param>
/// <param name="ProfileId">The player UUID.</param>
/// <param name="Username">The player username.</param>
/// <param name="ClientVersion">The Minecraft client version string reported to Realms (for example <c>1.21.11</c>).</param>
public sealed record RealmsSessionCredential(
    string AccessToken,
    Guid ProfileId,
    string Username,
    string ClientVersion)
{
    /// <summary>Builds Realms auth material from a completed session and the client version to report. The session's access token and profile are reused directly; no additional auth call is made.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null: a caller bug.</exception>
    /// <exception cref="RealmsException"><paramref name="session"/> is not a <see cref="AuthKind.Microsoft"/> session (<see cref="RealmsErrorKind.RequiresMicrosoftAccount"/>): not a caller bug, a fact about the world. Realms authenticates with the Minecraft-services token the Microsoft XBL/XSTS chain produces; an offline or Yggdrasil session never has one.</exception>
    public static RealmsSessionCredential FromSession(JavaSession session, string clientVersion)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrEmpty(clientVersion);
        if (session.Kind != AuthKind.Microsoft)
            throw new RealmsException(RealmsErrorKind.RequiresMicrosoftAccount, "Realms requires a Microsoft account session.");

        return new RealmsSessionCredential(session.AccessToken, session.Profile.Id, session.Profile.Name, clientVersion);
    }

    /// <summary>Builds the Realms session cookie header value: <c>sid=token:&lt;accessToken&gt;:&lt;undashedUuid&gt;;user=&lt;username&gt;;version=&lt;clientVersion&gt;</c>. The UUID is emitted without dashes to match the vanilla client.</summary>
    internal string ToCookieHeader() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"sid=token:{AccessToken}:{ProfileId:N};user={Username};version={ClientVersion}");

    /// <summary>Redacts the access token; prints only the profile, username, and client version.</summary>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"RealmsSessionCredential {{ ProfileId = {ProfileId}, Username = {Username}, ClientVersion = {ClientVersion}, AccessToken = <redacted> }}");
}
