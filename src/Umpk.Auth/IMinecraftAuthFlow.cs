using Umpk.Protocol.Java.Signing;

namespace Umpk.Auth;

/// <summary>The seam <see cref="MinecraftAuthFlow"/> implements: run the configured login flow, resume a cached session, fetch profile-key certificates, and invalidate a cached identity. Exists so a consumer can fake the whole flow in a test instead of being limited to testing whatever thin wrapper it built around the sealed concrete type. Closes LIBRARY-FEEDBACK P3 (requested twice): <see cref="MinecraftAuthFlow"/> was sealed with no interface and constructs its own HTTP internals, so nothing upstream of it could be faked.</summary>
public interface IMinecraftAuthFlow : IDisposable
{
    /// <summary>Runs the configured login flow to completion and caches the resulting session.</summary>
    Task<JavaSession> LoginAsync(IAuthInteraction interaction, CancellationToken ct, string? loginHint = null);

    /// <summary>Attempts to reuse a cached session for <paramref name="loginHint"/>.</summary>
    Task<JavaSession?> TryResumeAsync(string loginHint, CancellationToken ct);

    /// <summary>Returns the player profile-key certificates for a session, using the cache when present and unexpired, otherwise fetching and caching them.</summary>
    Task<PlayerCertificates> GetCertificatesAsync(JavaSession session, CancellationToken ct);

    /// <summary>Removes any cached session and certificates for <paramref name="loginHint"/>.</summary>
    Task InvalidateAsync(string loginHint, CancellationToken ct);
}
