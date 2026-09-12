using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Default <see cref="IServerAddressResolver"/>: a minimal DNS-over-UDP client that queries the <c>_minecraft._tcp.&lt;host&gt;</c> SRV record. No external DNS library. IP literals and hosts with no SRV record resolve to themselves. When multiple records exist, the lowest priority and then the highest weight are selected.</summary>
public sealed class DnsSrvResolver : IServerAddressResolver
{
    private readonly IReadOnlyList<IPEndPoint> _servers;

    private readonly TimeSpan _timeout;

    /// <summary>Creates a resolver using the OS-configured DNS servers.</summary>
    public DnsSrvResolver()
        : this(DiscoverSystemServers(), TimeSpan.FromSeconds(5))
    {
    }

    /// <summary>Creates a resolver using the OS-configured DNS servers with a caller-chosen timeout, for a caller that wants a shorter (or longer) DNS wait than the 5 second default.</summary>
    public DnsSrvResolver(TimeSpan timeout)
        : this(DiscoverSystemServers(), timeout)
    {
    }

    /// <summary>Creates a resolver targeting explicit DNS servers (used by tests).</summary>
    public DnsSrvResolver(IReadOnlyList<IPEndPoint> servers, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(servers);
        _servers = servers;
        _timeout = timeout;
    }

    /// <summary>A resolver that performs no SRV lookup and returns endpoints unchanged.</summary>
    public static IServerAddressResolver Passthrough { get; } = new PassthroughResolver();

    /// <inheritdoc />
    public async ValueTask<ServerEndpoint> ResolveAsync(ServerEndpoint endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        // IP literals never carry SRV records.
        if (IPAddress.TryParse(endpoint.Host, out _) || _servers.Count == 0)
            return endpoint;

        string query = $"_minecraft._tcp.{endpoint.Host}";
        foreach (IPEndPoint server in _servers)
        {
            try
            {
                SrvRecord? record = await QuerySrvAsync(server, query, ct).ConfigureAwait(false);
                if (record is { } r)
                    return new ServerEndpoint(r.Target, r.Port);

                // A successful response with no SRV record means "no service"; stop asking.
                return endpoint;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Try the next configured server.
            }
        }

        return endpoint;
    }

    private async Task<SrvRecord?> QuerySrvAsync(IPEndPoint server, string name, CancellationToken ct)
    {
        byte[] request = BuildQuery(name, out ushort id);
        using var udp = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);

        await udp.SendToAsync(request, SocketFlags.None, server, timeoutCts.Token).ConfigureAwait(false);

        byte[] buffer = new byte[512];
        var from = new IPEndPoint(server.Address, 0);
        SocketReceiveFromResult result =
            await udp.ReceiveFromAsync(buffer, SocketFlags.None, from, timeoutCts.Token).ConfigureAwait(false);

        return ParseResponse(buffer.AsSpan(0, result.ReceivedBytes), id);
    }

    private static byte[] BuildQuery(string name, out ushort id)
    {
        id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        using var ms = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[0..], id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100); // RD (recursion desired)
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);      // QDCOUNT
        ms.Write(header);

        foreach (string label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            byte[] bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }

        ms.WriteByte(0); // root

        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(tail[0..], 33); // QTYPE = SRV
        BinaryPrimitives.WriteUInt16BigEndian(tail[2..], 1);  // QCLASS = IN
        ms.Write(tail);

        return ms.ToArray();
    }

    private static SrvRecord? ParseResponse(ReadOnlySpan<byte> data, ushort expectedId)
    {
        if (data.Length < 12)
            return null;

        ushort id = BinaryPrimitives.ReadUInt16BigEndian(data[0..]);
        if (id != expectedId)
            return null;

        // TC (truncation) bit: the answer set did not fit in the UDP datagram. Parsing a partial record set could select the wrong priority/target, so treat it as "no usable answer" and let the caller fall back (a full resolver would retry over TCP; this minimal one passes through).
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        if ((flags & 0x0200) != 0)
            return null;

        ushort qd = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
        ushort an = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
        if (an == 0)
            return null;

        int offset = 12;
        for (int i = 0; i < qd; i++)
        {
            offset = SkipName(data, offset);
            offset += 4; // QTYPE + QCLASS
        }

        SrvRecord? best = null;
        for (int i = 0; i < an && offset < data.Length; i++)
        {
            offset = SkipName(data, offset);
            if (offset + 10 > data.Length)
                break;

            ushort type = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            ushort rdLength = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 8)..]);
            int rdata = offset + 10;
            offset = rdata + rdLength;

            if (type != 33 || rdata + 6 > data.Length)
                continue;

            ushort priority = BinaryPrimitives.ReadUInt16BigEndian(data[rdata..]);
            ushort weight = BinaryPrimitives.ReadUInt16BigEndian(data[(rdata + 2)..]);
            ushort port = BinaryPrimitives.ReadUInt16BigEndian(data[(rdata + 4)..]);
            string target = ReadName(data, rdata + 6).Trim('.');
            if (target.Length == 0)
                continue;

            var candidate = new SrvRecord(priority, weight, port, target);
            if (best is null || candidate.Priority < best.Value.Priority ||
                (candidate.Priority == best.Value.Priority && candidate.Weight > best.Value.Weight))
                best = candidate;

        }

        return best;
    }

    private static int SkipName(ReadOnlySpan<byte> data, int offset)
    {
        while (offset < data.Length)
        {
            byte len = data[offset];
            if (len == 0)
                return offset + 1;

            if ((len & 0xC0) == 0xC0)
            {
                return offset + 2; // compression pointer terminates the name
            }

            offset += 1 + len;
        }

        return offset;
    }

    private static string ReadName(ReadOnlySpan<byte> data, int offset)
    {
        var sb = new StringBuilder();
        int guard = 0;
        while (offset < data.Length && guard++ < 128)
        {
            byte len = data[offset];
            if (len == 0)
                break;

            if ((len & 0xC0) == 0xC0)
            {
                // A 2-byte compression pointer. If the second byte is past the buffer edge the response is malformed/truncated; stop rather than indexing out of bounds.
                if (offset + 1 >= data.Length)
                    break;

                int pointer = ((len & 0x3F) << 8) | data[offset + 1];
                offset = pointer;
                continue;
            }

            offset++;
            if (offset + len > data.Length)
                break;

            if (sb.Length > 0)
                sb.Append('.');

            sb.Append(Encoding.ASCII.GetString(data.Slice(offset, len)));
            offset += len;
        }

        return sb.ToString();
    }

    private static IReadOnlyList<IPEndPoint> DiscoverSystemServers()
    {
        var servers = new List<IPEndPoint>();
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (IPAddress dns in nic.GetIPProperties().DnsAddresses)
                {
                    var ep = new IPEndPoint(dns, 53);
                    if (!servers.Contains(ep))
                        servers.Add(ep);

                }
            }
        }
        catch (NetworkInformationException)
        {
            // No DNS configuration available; resolution will fall back to passthrough.
        }

        return servers;
    }

    private readonly record struct SrvRecord(ushort Priority, ushort Weight, ushort Port, string Target);

    private sealed class PassthroughResolver : IServerAddressResolver
    {
        public ValueTask<ServerEndpoint> ResolveAsync(ServerEndpoint endpoint, CancellationToken ct) =>
            ValueTask.FromResult(endpoint);
    }
}
