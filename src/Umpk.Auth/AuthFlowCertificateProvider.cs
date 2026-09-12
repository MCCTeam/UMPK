using Umpk.Protocol.Java.Signing;

namespace Umpk.Auth;

/// <summary>Feeds the <see cref="IChatSigningProvider"/> seam from an authenticated <see cref="JavaSession"/> over <see cref="MinecraftAuthFlow.GetCertificatesAsync"/> (which caches and refreshes the player certificates through the token store). An auth-level failure degrades to unsigned rather than tearing down the session; the caller's chat-signing coordinator retries on the next expiry check.</summary>
public sealed class AuthFlowCertificateProvider : IChatSigningProvider
{
    private readonly IMinecraftAuthFlow _flow;
    private readonly JavaSession _session;

    public AuthFlowCertificateProvider(IMinecraftAuthFlow flow, JavaSession session)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(session);
        _flow = flow;
        _session = session;
    }

    /// <inheritdoc />
    public async ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _flow.GetCertificatesAsync(_session, cancellationToken).ConfigureAwait(false);
        }
        catch (AuthException)
        {
            // No certificates available: the send path stays unsigned, exactly like an offline session.
            return null;
        }
    }
}
