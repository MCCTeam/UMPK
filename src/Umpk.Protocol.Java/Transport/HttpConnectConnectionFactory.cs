using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Connects through an HTTP proxy using the CONNECT method (tunnels raw TCP once the proxy replies 2xx). Supports Basic proxy authentication. The target host is sent verbatim so the proxy resolves it.</summary>
public sealed class HttpConnectConnectionFactory(ProxyOptions proxy) : IConnectionFactory
{
    private readonly ProxyOptions _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));

    /// <inheritdoc />
    public async ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(_proxy.Host, _proxy.Port, ct).ConfigureAwait(false);
            var stream = new NetworkStream(socket, ownsSocket: false);
            await TunnelAsync(stream, endpoint, ct).ConfigureAwait(false);
            return SocketDuplexPipe.Wrap(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task TunnelAsync(NetworkStream stream, ServerEndpoint endpoint, CancellationToken ct)
    {
        string target = endpoint.Host.Contains(':', StringComparison.Ordinal)
            ? $"[{endpoint.Host}]:{endpoint.Port}"
            : $"{endpoint.Host}:{endpoint.Port}";

        var request = new StringBuilder();
        request.Append("CONNECT ").Append(target).Append(" HTTP/1.1\r\n");
        request.Append("Host: ").Append(target).Append("\r\n");
        if (!string.IsNullOrEmpty(_proxy.Username))
        {
            string token = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_proxy.Username}:{_proxy.Password}"));
            request.Append("Proxy-Authorization: Basic ").Append(token).Append("\r\n");
        }

        request.Append("Proxy-Connection: keep-alive\r\n\r\n");

        byte[] bytes = Encoding.ASCII.GetBytes(request.ToString());
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        string status = await ReadStatusLineAsync(stream, ct).ConfigureAwait(false);
        // Expect "HTTP/1.x 2xx...".
        string[] parts = status.Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out int code) || code / 100 != 2)
            throw new ConnectionClosedException(CloseReason.ProtocolViolation, $"HTTP proxy CONNECT failed: {status}");

        // Drain the remaining response headers up to the blank line.
        await DrainHeadersAsync(stream, ct).ConfigureAwait(false);
    }

    private static async Task<string> ReadStatusLineAsync(NetworkStream stream, CancellationToken ct) =>
        await ReadLineAsync(stream, ct).ConfigureAwait(false);

    private static async Task DrainHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        while (true)
        {
            string line = await ReadLineAsync(stream, ct).ConfigureAwait(false);
            if (line.Length == 0)
                return;

        }
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder(64);
        byte[] one = new byte[1];
        bool sawCr = false;
        while (true)
        {
            int n = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0)
                throw new ConnectionClosedException(CloseReason.SocketEof, "HTTP proxy closed the connection early.");

            char c = (char)one[0];
            if (c == '\n' && sawCr)
            {
                sb.Length--; // drop the trailing CR
                return sb.ToString();
            }

            sawCr = c == '\r';
            sb.Append(c);
            if (sb.Length > 8192)
                throw new ConnectionClosedException(CloseReason.ProtocolViolation, "HTTP proxy response line too long.");

        }
    }
}
