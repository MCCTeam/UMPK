namespace Umpk.Auth;

/// <summary>The default <see cref="IHttpMessageHandlerFactory"/>: a single shared <see cref="SocketsHttpHandler"/> with automatic decompression disabled defaults. Hosts that need proxy configuration supply their own.</summary>
public sealed class DefaultHttpMessageHandlerFactory : IHttpMessageHandlerFactory
{
    private readonly SocketsHttpHandler _handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AllowAutoRedirect = false,
    };

    /// <summary>The shared default factory instance.</summary>
    public static DefaultHttpMessageHandlerFactory Instance { get; } = new();

    /// <inheritdoc />
    public HttpMessageHandler CreateHandler() => _handler;
}
