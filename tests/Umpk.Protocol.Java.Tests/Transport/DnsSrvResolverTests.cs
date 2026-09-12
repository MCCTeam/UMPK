using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

public class DnsSrvResolverTests
{
    [Fact]
    public async Task DnsSrvResolver_HonorsTheConfiguredTimeout()
    {
        // The timeout-only constructor lets a caller pick a short DNS wait instead of the 5s default, chaining to the same DiscoverSystemServers() the parameterless constructor uses. A host with no SRV record answers fast when a server responds, and the 50ms budget (not the 5s default) bounds the wait when a configured server is unreachable, so either way this returns well under 5 seconds.
        var resolver = new DnsSrvResolver(TimeSpan.FromMilliseconds(50));
        var input = new ServerEndpoint("unreachable-dns-timeout-test.invalid", 25565);

        var stopwatch = Stopwatch.StartNew();
        ServerEndpoint result = await resolver.ResolveAsync(input, Ct());
        stopwatch.Stop();

        Assert.Equal(input, result);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(4),
            $"Expected well under the 5s default with a 50ms timeout; took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ResolvesSrvRecord_FromFakeServer()
    {
        using var server = new FakeDnsServer(replyTarget: "mc.internal.example", replyPort: 12345, priority: 0, weight: 5);
        var resolver = new DnsSrvResolver([server.Endpoint], TimeSpan.FromSeconds(2));

        ServerEndpoint result = await resolver.ResolveAsync(
            new ServerEndpoint("example.com", 25565), Ct());

        Assert.Equal("mc.internal.example", result.Host);
        Assert.Equal((ushort)12345, result.Port);
    }

    [Fact]
    public async Task PicksLowestPriority_ThenHighestWeight()
    {
        using var server = new FakeDnsServer(
            [new(10, 1, 1000, "low.prio"), new(0, 1, 2000, "chosen.a"), new(0, 9, 3000, "chosen.b")]);
        var resolver = new DnsSrvResolver([server.Endpoint], TimeSpan.FromSeconds(2));

        ServerEndpoint result = await resolver.ResolveAsync(new ServerEndpoint("srv.test", 25565), Ct());

        // Priority 0 with weight 9 -> chosen.b:3000
        Assert.Equal("chosen.b", result.Host);
        Assert.Equal((ushort)3000, result.Port);
    }

    [Fact]
    public async Task NoAnswer_ReturnsInputUnchanged()
    {
        using var server = new FakeDnsServer(Array.Empty<FakeDnsServer.Srv>());
        var resolver = new DnsSrvResolver([server.Endpoint], TimeSpan.FromSeconds(2));

        var input = new ServerEndpoint("plain.host", 25566);
        ServerEndpoint result = await resolver.ResolveAsync(input, Ct());

        Assert.Equal(input, result);
    }

    [Fact]
    public async Task IpLiteral_SkipsLookup()
    {
        // No server needed; IP literals never carry SRV records.
        var resolver = new DnsSrvResolver([new IPEndPoint(IPAddress.Loopback, 9)], TimeSpan.FromMilliseconds(200));
        var input = new ServerEndpoint("127.0.0.1", 25565);
        ServerEndpoint result = await resolver.ResolveAsync(input, Ct());
        Assert.Equal(input, result);
    }

    [Fact]
    public async Task TruncatedResponse_ReturnsInputUnchanged()
    {
        // The TC (truncation) bit means the answer set did not fit in the datagram. Parsing a partial record set could pick the wrong target, so the resolver treats it as no answer.
        using var server = new FakeDnsServer(
            [new(0, 5, 12345, "mc.internal.example")], truncated: true);
        var resolver = new DnsSrvResolver([server.Endpoint], TimeSpan.FromSeconds(2));

        var input = new ServerEndpoint("example.com", 25565);
        ServerEndpoint result = await resolver.ResolveAsync(input, Ct());

        Assert.Equal(input, result);
    }

    [Fact]
    public async Task MalformedCompressionPointerAtBufferEdge_DoesNotThrow_ReturnsInput()
    {
        // An SRV record whose target name ends in a compression-pointer first byte with no second byte (pointer at the buffer edge) must not throw IndexOutOfRange; the resolver bounds-checks, yields an empty target, skips the record, and returns the input unchanged.
        using var server = new FakeDnsServer(MalformedPointerResponse);
        var resolver = new DnsSrvResolver([server.Endpoint], TimeSpan.FromSeconds(2));

        var input = new ServerEndpoint("edge.test", 25565);
        ServerEndpoint result = await resolver.ResolveAsync(input, Ct());

        Assert.Equal(input, result);
    }

    // A DNS response with QDCOUNT=1, ANCOUNT=1, one SRV record whose target is a single 0xC0 byte (a compression-pointer first byte) placed at the very end of the datagram (no second pointer byte).
    private static byte[] MalformedPointerResponse(ushort id, ReadOnlySpan<byte> query)
    {
        int qStart = 12;
        int qEnd = qStart;
        while (query[qEnd] != 0)
            qEnd += query[qEnd] + 1;

        qEnd += 1 + 4; // null + QTYPE + QCLASS
        ReadOnlySpan<byte> question = query[qStart..qEnd];

        using var ms = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[0..], id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x8180); // response, RD, RA (not truncated)
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);      // QDCOUNT
        BinaryPrimitives.WriteUInt16BigEndian(header[6..], 1);      // ANCOUNT
        ms.Write(header);
        ms.Write(question);

        ms.WriteByte(0xC0); // name pointer to question
        ms.WriteByte(0x0C);
        Be16(ms, 33); // TYPE SRV
        Be16(ms, 1);  // CLASS IN
        Be32(ms, 60); // TTL
        Be16(ms, 7);  // RDLENGTH = priority(2)+weight(2)+port(2)+target(1)
        Be16(ms, 0);  // priority
        Be16(ms, 5);  // weight
        Be16(ms, 25565); // port
        ms.WriteByte(0xC0); // target: a compression-pointer first byte, with NO second byte (edge)

        return ms.ToArray();
    }

    private static void Be16(Stream s, ushort v)
    {
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)(v & 0xFF));
    }

    private static void Be32(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    internal delegate byte[] DnsResponder(ushort id, ReadOnlySpan<byte> query);

    private sealed class FakeDnsServer : IDisposable
    {
        private readonly Socket _socket;
        private readonly Srv[] _records;
        private readonly bool _truncated;
        private readonly DnsResponder? _customResponder;
        private readonly CancellationTokenSource _cts = new();

        public readonly record struct Srv(ushort Priority, ushort Weight, ushort Port, string Target);

        public FakeDnsServer(string replyTarget, ushort replyPort, ushort priority, ushort weight)
            : this([new Srv(priority, weight, replyPort, replyTarget)])
        {
        }

        public FakeDnsServer(Srv[] records, bool truncated = false)
        {
            _records = records;
            _truncated = truncated;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            Endpoint = (IPEndPoint)_socket.LocalEndPoint!;
            _ = Task.Run(RunAsync);
        }

        public FakeDnsServer(DnsResponder responder)
        {
            _records = [];
            _customResponder = responder;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            Endpoint = (IPEndPoint)_socket.LocalEndPoint!;
            _ = Task.Run(RunAsync);
        }

        public IPEndPoint Endpoint { get; }

        private async Task RunAsync()
        {
            byte[] buffer = new byte[512];
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (!_cts.IsCancellationRequested)
            {
                SocketReceiveFromResult result;
                try
                {
                    result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, from, _cts.Token);
                }
                catch (Exception)
                {
                    return;
                }

                ushort id = BinaryPrimitives.ReadUInt16BigEndian(buffer);
                byte[] response = _customResponder is not null
                    ? _customResponder(id, buffer.AsSpan(0, result.ReceivedBytes))
                    : BuildResponse(id, buffer.AsSpan(0, result.ReceivedBytes));
                await _socket.SendToAsync(response, SocketFlags.None, result.RemoteEndPoint, _cts.Token);
            }
        }

        private byte[] BuildResponse(ushort id, ReadOnlySpan<byte> query)
        {
            // Echo the question section from the query.
            int qStart = 12;
            int qEnd = qStart;
            while (query[qEnd] != 0)
                qEnd += query[qEnd] + 1;

            qEnd += 1 + 4; // null + QTYPE + QCLASS
            ReadOnlySpan<byte> question = query[qStart..qEnd];

            using var ms = new MemoryStream();
            Span<byte> header = stackalloc byte[12];
            BinaryPrimitives.WriteUInt16BigEndian(header[0..], id);
            BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)(0x8180 | (_truncated ? 0x0200 : 0))); // response, RD, RA, +TC
            BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1); // QDCOUNT
            BinaryPrimitives.WriteUInt16BigEndian(header[6..], (ushort)_records.Length); // ANCOUNT
            ms.Write(header);
            ms.Write(question);

            foreach (Srv r in _records)
            {
                // Name pointer to the question (offset 12 = 0xC00C).
                ms.WriteByte(0xC0);
                ms.WriteByte(0x0C);
                WriteU16(ms, 33); // TYPE SRV
                WriteU16(ms, 1);  // CLASS IN
                WriteU32(ms, 60); // TTL

                byte[] target = EncodeName(r.Target);
                WriteU16(ms, (ushort)(6 + target.Length)); // RDLENGTH
                WriteU16(ms, r.Priority);
                WriteU16(ms, r.Weight);
                WriteU16(ms, r.Port);
                ms.Write(target);
            }

            return ms.ToArray();
        }

        private static byte[] EncodeName(string name)
        {
            using var ms = new MemoryStream();
            foreach (string label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(label);
                ms.WriteByte((byte)bytes.Length);
                ms.Write(bytes);
            }

            ms.WriteByte(0);
            return ms.ToArray();
        }

        private static void WriteU16(Stream s, ushort v)
        {
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)(v & 0xFF));
        }

        private static void WriteU32(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _socket.Dispose();
            _cts.Dispose();
        }
    }
}
