using System.IO.Pipelines;
using QuickProxyNet;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Connects through a SOCKS4 proxy, or SOCKS4a when <see cref="RemoteDns"/> is set. SOCKS4 resolves the target host to an IPv4 address locally and sends the address on the wire; SOCKS4a instead sends the hostname so the proxy resolves it. <see cref="ProxyOptions.Username"/> is sent as the SOCKS4 userid; <see cref="ProxyOptions.Password"/> is unused by SOCKS4 and ignored.</summary>
public sealed class Socks4ConnectionFactory(ProxyOptions proxy, bool remoteDns = false) : IConnectionFactory
{
    private readonly ProxyOptions _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));

    /// <summary>When set, the target host is sent as a hostname for the proxy to resolve (SOCKS4a). When clear, the host is resolved to an IPv4 address locally (SOCKS4).</summary>
    public bool RemoteDns { get; } = remoteDns;

    /// <inheritdoc />
    public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) =>
        ProxyConnection.ConnectAsync(RemoteDns ? ProxyType.Socks4a : ProxyType.Socks4, _proxy, endpoint, ct);
}
