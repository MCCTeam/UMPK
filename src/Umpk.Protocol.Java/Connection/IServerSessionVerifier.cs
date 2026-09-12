using System.Net;

namespace Umpk.Protocol.Java;

/// <summary>The server-role session-verification seam. An online-mode server login, after enabling encryption, verifies that the joining client authenticated with the session service (<c>hasJoined</c>) and receives back the authenticated profile (including signed textures). Implemented by <c>Umpk.Auth</c>'s <c>YggdrasilSessionService</c>; <c>JavaServerLogin</c> depends only on this interface.</summary>
public interface IServerSessionVerifier
{
    /// <summary>Verifies a client's session join. Returns the authenticated <see cref="GameProfile"/> on success, or <see langword="null"/> when the session service reports no matching join (unverified username).</summary>
    /// <param name="username">The username the client announced in login-start.</param>
    /// <param name="serverIdHash">The Minecraft server-id hash the server computed from the shared secret.</param>
    /// <param name="clientIp">The client IP to forward for proxy-prevention, or <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<GameProfile?> VerifyJoinAsync(string username, string serverIdHash, IPAddress? clientIp, CancellationToken ct);
}
