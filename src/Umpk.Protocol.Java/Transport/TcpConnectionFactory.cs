using System.IO.Pipelines;
using System.Net.Sockets;

namespace Umpk.Protocol.Java.Transport;

/// <summary>The default connection factory: opens a TCP socket to the endpoint and wraps it as a duplex pipe. Disables Nagle's algorithm, matching low-latency game traffic.</summary>
public sealed class TcpConnectionFactory : IConnectionFactory
{
    /// <summary>A shared instance.</summary>
    public static TcpConnectionFactory Shared { get; } = new();

    /// <inheritdoc />
    public async ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return SocketDuplexPipe.Wrap(socket);
    }
}
