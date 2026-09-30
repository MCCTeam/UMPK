using System.IO.Pipelines;
using QuickProxyNet;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Connects through a SOCKS5 proxy (RFC 1928), optionally with username/password auth (RFC 1929). Host names are resolved by the proxy without a local DNS leak.</summary>
public sealed class Socks5ConnectionFactory(ProxyOptions proxy) : IConnectionFactory
{
    private readonly ProxyOptions _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));

    /// <inheritdoc />
    public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) =>
        ProxyConnection.ConnectAsync(ProxyType.Socks5, _proxy, endpoint, ct);
}
