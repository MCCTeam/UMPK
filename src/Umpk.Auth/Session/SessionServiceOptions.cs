namespace Umpk.Auth.Session;

/// <summary>Configuration for <see cref="YggdrasilSessionService"/>: the session-server base URL (Mojang default or an authlib-injector provider) and the HTTP handler seam.</summary>
public sealed class SessionServiceOptions
{
    /// <summary>Base URL for the session server. Null uses Mojang's <c>https://sessionserver.mojang.com/</c>. For authlib-injector, supply the provider's <c>.../sessionserver/</c> base.</summary>
    public Uri? BaseUrl { get; init; }

    /// <summary>The HTTP handler seam. Defaults to the shared default factory.</summary>
    public IHttpMessageHandlerFactory HttpHandlerFactory { get; init; } = DefaultHttpMessageHandlerFactory.Instance;
}
