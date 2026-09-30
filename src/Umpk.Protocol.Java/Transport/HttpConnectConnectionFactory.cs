using System.IO.Pipelines;
using QuickProxyNet;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Connects through an HTTP proxy using the CONNECT method. Supports Basic proxy authentication. The target host is sent verbatim so the proxy resolves it.</summary>
public sealed class HttpConnectConnectionFactory(ProxyOptions proxy) : IConnectionFactory
{
    private readonly ProxyOptions _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));

    /// <inheritdoc />
    public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) =>
        ProxyConnection.ConnectAsync(ProxyType.Http, _proxy, endpoint, ct);
}
