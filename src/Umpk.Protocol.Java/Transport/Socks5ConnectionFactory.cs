using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Connects through a SOCKS5 proxy (RFC 1928), optionally with username/password auth (RFC 1929). The target host is sent as a domain name so the proxy resolves it without a local DNS leak.</summary>
public sealed class Socks5ConnectionFactory(ProxyOptions proxy) : IConnectionFactory
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
            await NegotiateAsync(stream, endpoint, ct).ConfigureAwait(false);
            return SocketDuplexPipe.Wrap(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task NegotiateAsync(NetworkStream stream, ServerEndpoint endpoint, CancellationToken ct)
    {
        bool auth = !string.IsNullOrEmpty(_proxy.Username);

        // Greeting: version 5, method list.
        byte[] greeting = auth
            ? [0x05, 0x02, 0x00, 0x02] // no-auth + user/pass
            : [0x05, 0x01, 0x00];      // no-auth only
        await stream.WriteAsync(greeting, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        byte[] methodReply = new byte[2];
        await ReadExactAsync(stream, methodReply, ct).ConfigureAwait(false);
        if (methodReply[0] != 0x05)
            throw new ConnectionClosedException(CloseReason.ProtocolViolation, "SOCKS5 proxy returned a bad version.");

        byte chosen = methodReply[1];
        if (chosen == 0xFF)
            throw new ConnectionClosedException(CloseReason.ProtocolViolation, "SOCKS5 proxy accepted no auth method.");

        if (chosen == 0x02)
            await UserPassAuthAsync(stream, ct).ConfigureAwait(false);

        else if (chosen != 0x00)
            throw new ConnectionClosedException(CloseReason.ProtocolViolation, $"SOCKS5 proxy chose unsupported method 0x{chosen:X2}.");

        // CONNECT request with a domain-name target.
        byte[] hostBytes = Encoding.ASCII.GetBytes(endpoint.Host);
        if (hostBytes.Length > 255)
            throw new ArgumentException("Host name is too long for SOCKS5.", nameof(endpoint));

        var request = new byte[7 + hostBytes.Length];
        request[0] = 0x05; // version
        request[1] = 0x01; // CONNECT
        request[2] = 0x00; // reserved
        request[3] = 0x03; // domain name
        request[4] = (byte)hostBytes.Length;
        hostBytes.CopyTo(request, 5);
        request[5 + hostBytes.Length] = (byte)(endpoint.Port >> 8);
        request[6 + hostBytes.Length] = (byte)(endpoint.Port & 0xFF);
        await stream.WriteAsync(request, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        // Reply header: VER REP RSV ATYP + bound address.
        byte[] head = new byte[4];
        await ReadExactAsync(stream, head, ct).ConfigureAwait(false);
        if (head[1] != 0x00)
            throw new ConnectionClosedException(CloseReason.ProtocolViolation, $"SOCKS5 CONNECT failed (reply 0x{head[1]:X2}).");

        int addrLen = head[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => await ReadDomainLengthAsync(stream, ct).ConfigureAwait(false),
            _ => throw new ConnectionClosedException(CloseReason.ProtocolViolation, "SOCKS5 reply had a bad address type."),
        };
        byte[] rest = new byte[addrLen + 2]; // address + port
        await ReadExactAsync(stream, rest, ct).ConfigureAwait(false);
    }

    private async Task UserPassAuthAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] user = Encoding.UTF8.GetBytes(_proxy.Username ?? string.Empty);
        byte[] pass = Encoding.UTF8.GetBytes(_proxy.Password ?? string.Empty);
        if (user.Length > 255 || pass.Length > 255)
            throw new ArgumentException("SOCKS5 credentials are too long.");

        var buf = new byte[3 + user.Length + pass.Length];
        buf[0] = 0x01; // auth version
        buf[1] = (byte)user.Length;
        user.CopyTo(buf, 2);
        buf[2 + user.Length] = (byte)pass.Length;
        pass.CopyTo(buf, 3 + user.Length);
        await stream.WriteAsync(buf, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        byte[] reply = new byte[2];
        await ReadExactAsync(stream, reply, ct).ConfigureAwait(false);
        if (reply[1] != 0x00)
            throw new ConnectionClosedException(CloseReason.ProtocolViolation, "SOCKS5 proxy rejected the credentials.");

    }

    private static async Task<int> ReadDomainLengthAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] len = new byte[1];
        await ReadExactAsync(stream, len, ct).ConfigureAwait(false);
        return len[0];
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
                throw new ConnectionClosedException(CloseReason.SocketEof, "SOCKS5 proxy closed the connection early.");

            read += n;
        }
    }
}
