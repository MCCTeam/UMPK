using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Connects through a SOCKS4 proxy, or SOCKS4a when <see cref="RemoteDns"/> is set. SOCKS4 resolves the target host to an IPv4 address locally and sends the address on the wire; SOCKS4a instead sends the hostname so the proxy resolves it. <see cref="ProxyOptions.Username"/> is sent as the SOCKS4 userid; <see cref="ProxyOptions.Password"/> is unused by SOCKS4 and ignored.</summary>
public sealed class Socks4ConnectionFactory(ProxyOptions proxy, bool remoteDns = false) : IConnectionFactory
{
    private readonly ProxyOptions _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));

    /// <summary>When set, the target host is sent as a hostname for the proxy to resolve (SOCKS4a). When clear, the host is resolved to an IPv4 address locally (SOCKS4).</summary>
    public bool RemoteDns { get; } = remoteDns;

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
        byte[] userid = string.IsNullOrEmpty(_proxy.Username) ? [] : Encoding.ASCII.GetBytes(_proxy.Username);
        byte[]? hostBytes = null;
        byte[] ipv4;

        if (RemoteDns)
        {
            // SOCKS4a: an "invalid" address 0.0.0.x (x != 0) signals the proxy to resolve the trailing host.
            ipv4 = [0x00, 0x00, 0x00, 0x01];
            hostBytes = Encoding.ASCII.GetBytes(endpoint.Host);
        }
        else
            ipv4 = (await ResolveIpv4Async(endpoint.Host, ct).ConfigureAwait(false)).GetAddressBytes();

        int length = 9 + userid.Length + (hostBytes is null ? 0 : hostBytes.Length + 1);
        byte[] request = new byte[length];
        int i = 0;
        request[i++] = 0x04; // SOCKS version 4
        request[i++] = 0x01; // CONNECT
        request[i++] = (byte)(endpoint.Port >> 8);
        request[i++] = (byte)(endpoint.Port & 0xFF);
        ipv4.CopyTo(request, i);
        i += 4;
        userid.CopyTo(request, i);
        i += userid.Length;
        request[i++] = 0x00; // userid terminator
        if (hostBytes is not null)
        {
            hostBytes.CopyTo(request, i);
            i += hostBytes.Length;
            request[i] = 0x00; // host terminator
        }

        await stream.WriteAsync(request, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        byte[] reply = new byte[8];
        await ReadExactAsync(stream, reply, ct).ConfigureAwait(false);
        if (reply[0] != 0x00 || reply[1] != 0x5A)
            throw new ConnectionClosedException(CloseReason.ProtocolViolation, $"SOCKS4 CONNECT failed (reply 0x{reply[1]:X2}).");

    }

    private async Task<IPAddress> ResolveIpv4Async(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out IPAddress? literal) && literal.AddressFamily == AddressFamily.InterNetwork)
            return literal;

        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct).ConfigureAwait(false);
        if (addresses.Length == 0)
            throw new ConnectionClosedException(CloseReason.ProtocolViolation, $"SOCKS4 could not resolve an IPv4 address for '{host}'; use SOCKS4a to let the proxy resolve the name.");

        return addresses[0];
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
                throw new ConnectionClosedException(CloseReason.SocketEof, "SOCKS4 proxy closed the connection early.");

            read += n;
        }
    }
}
