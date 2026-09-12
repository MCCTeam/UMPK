namespace Umpk.Protocol.Java;

/// <summary>The client-role session-join seam. An online-mode client login proves ownership of its profile to the session service before the server enables encryption, by POSTing the access token, profile UUID, and the SHA-1 server-id hash. Implemented by <c>Umpk.Auth</c>'s <c>YggdrasilSessionService</c>; the login helper depends only on this interface so hosts can substitute their own session service.</summary>
public interface ISessionAuthenticator
{
    /// <summary>Proves session ownership to the session server. Completes on success; throws on rejection.</summary>
    /// <param name="serverIdHash">The Minecraft server-id hash (two's-complement SHA-1 hex).</param>
    /// <param name="credentials">The profile plus its bearer access token.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask JoinServerAsync(string serverIdHash, ProfileCredentials credentials, CancellationToken ct);
}
