namespace Umpk.Auth;

/// <summary>The authlib-injector URL rules a Yggdrasil-flow account's configured provider root has to go through: the trailing-slash normalization every relative resolution against it depends on, and the session-server sub-API.</summary>
public static class YggdrasilEndpoints
{
    /// <summary>The path segment that separates an authlib-injector provider's session-server API from its authserver and minecraftservices APIs. UMPK's session-service client takes a session-server base (it appends <c>session/minecraft/join</c>, Mojang-style), so the configured provider root has to be pushed down into this sub-API first.</summary>
    private const string SessionServerSegment = "sessionserver/";

    /// <summary>Returns <paramref name="url"/> unchanged if it already ends in a slash, otherwise a copy with one appended. Every consumer resolves relative URIs against a provider root, and <see cref="Uri"/> relative resolution drops the last path segment of a base that does not end in a slash, so <c>http://host/authlib-injector</c> would silently resolve <c>authserver/authenticate</c> to <c>http://host/authserver/authenticate</c> instead of the intended <c>http://host/authlib-injector/authserver/authenticate</c>.</summary>
    public static Uri EnsureTrailingSlash(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.AbsolutePath.EndsWith('/') ? url : new UriBuilder(url) { Path = url.AbsolutePath + "/" }.Uri;
    }

    /// <summary>The session-server base URL under an authlib-injector provider root: the root's <c>sessionserver/</c> sub-API. A provider serves the session API under <c>&lt;root&gt;/sessionserver/session/minecraft/join</c>, not under <c>&lt;root&gt;/session/minecraft/join</c>, so <paramref name="providerRoot"/> is normalized with <see cref="EnsureTrailingSlash"/> before the sub-API is resolved against it - resolving against the root as given would silently drop its last segment whenever that root lacked a trailing slash.</summary>
    public static Uri SessionServer(Uri providerRoot)
    {
        ArgumentNullException.ThrowIfNull(providerRoot);
        return new Uri(EnsureTrailingSlash(providerRoot), SessionServerSegment);
    }
}
