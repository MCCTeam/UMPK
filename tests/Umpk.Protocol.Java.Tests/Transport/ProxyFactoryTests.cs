using System.Net;
using System.Net.Sockets;
using System.Text;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

public class ProxyFactoryTests
{
    [Fact]
    public async Task Socks5_NoAuth_TunnelsAndTransfersData()
    {
        using var proxy = new FakeProxy(FakeProxy.Kind.Socks5, auth: false);
        var factory = new Socks5ConnectionFactory(new ProxyOptions { Host = "127.0.0.1", Port = proxy.Port });

        var pipe = await factory.ConnectAsync(new ServerEndpoint("target.example", 25565), Ct());
        await AssertEchoAsync(pipe);
    }

    [Fact]
    public async Task Socks5_WithAuth_Tunnels()
    {
        using var proxy = new FakeProxy(FakeProxy.Kind.Socks5, auth: true);
        var factory = new Socks5ConnectionFactory(new ProxyOptions
        {
            Host = "127.0.0.1",
            Port = proxy.Port,
            Username = "user",
            Password = "pass",
        });

        var pipe = await factory.ConnectAsync(new ServerEndpoint("target.example", 25565), Ct());
        await AssertEchoAsync(pipe);
    }

    [Fact]
    public async Task HttpConnect_TunnelsAndTransfersData()
    {
        using var proxy = new FakeProxy(FakeProxy.Kind.HttpConnect, auth: false);
        var factory = new HttpConnectConnectionFactory(new ProxyOptions { Host = "127.0.0.1", Port = proxy.Port });

        var pipe = await factory.ConnectAsync(new ServerEndpoint("target.example", 25565), Ct());
        await AssertEchoAsync(pipe);
    }

    [Fact]
    public async Task Socks4_NoUserId_TunnelsAndTransfersData()
    {
        using var proxy = new FakeProxy(FakeProxy.Kind.Socks4, auth: false);
        var factory = new Socks4ConnectionFactory(new ProxyOptions { Host = "127.0.0.1", Port = proxy.Port });

        var pipe = await factory.ConnectAsync(new ServerEndpoint("127.0.0.1", 25565), Ct());
        await AssertEchoAsync(pipe);
    }

    [Fact]
    public async Task Socks4_WithUserId_PutsTheUserIdOnTheWire()
    {
        using var proxy = new FakeProxy(FakeProxy.Kind.Socks4, auth: true);
        var factory = new Socks4ConnectionFactory(new ProxyOptions
        {
            Host = "127.0.0.1",
            Port = proxy.Port,
            Username = "neo",
        });

        var pipe = await factory.ConnectAsync(new ServerEndpoint("127.0.0.1", 25565), Ct());
        await AssertEchoAsync(pipe);
    }

    [Fact]
    public async Task Socks4a_SendsTheHostnameForTheProxyToResolve()
    {
        using var proxy = new FakeProxy(FakeProxy.Kind.Socks4a, auth: false);
        var factory = new Socks4ConnectionFactory(new ProxyOptions { Host = "127.0.0.1", Port = proxy.Port }, remoteDns: true);

        var pipe = await factory.ConnectAsync(new ServerEndpoint("target.example", 25565), Ct());
        await AssertEchoAsync(pipe);
    }

    [Fact]
    public async Task Socks4_Rejection_ThrowsConnectionClosedWithProtocolViolation()
    {
        using var proxy = new FakeProxy(FakeProxy.Kind.Socks4, auth: false, rejectSocks4: true);
        var factory = new Socks4ConnectionFactory(new ProxyOptions { Host = "127.0.0.1", Port = proxy.Port });

        ConnectionClosedException ex = await Assert.ThrowsAsync<ConnectionClosedException>(
            async () => await factory.ConnectAsync(new ServerEndpoint("127.0.0.1", 25565), Ct()));
        Assert.Equal(CloseReason.ProtocolViolation, ex.Reason);
    }

    private static async Task AssertEchoAsync(System.IO.Pipelines.IDuplexPipe pipe)
    {
        byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];
        await pipe.Output.WriteAsync(payload, Ct());
        await pipe.Output.FlushAsync(Ct());

        var received = new List<byte>();
        while (received.Count < payload.Length)
        {
            System.IO.Pipelines.ReadResult rr = await pipe.Input.ReadAsync(Ct());
            foreach (ReadOnlyMemory<byte> seg in rr.Buffer)
                received.AddRange(seg.ToArray());

            pipe.Input.AdvanceTo(rr.Buffer.End);
            if (rr.IsCompleted)
                break;

        }

        Assert.Equal(payload, received.ToArray());
    }

    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    private sealed class FakeProxy : IDisposable
    {
        public enum Kind { Socks5, HttpConnect, Socks4, Socks4a }

        private readonly Socket _listener;
        private readonly CancellationTokenSource _cts = new();

        public FakeProxy(Kind kind, bool auth, bool rejectSocks4 = false)
        {
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _listener.Listen(1);
            Port = (ushort)((IPEndPoint)_listener.LocalEndPoint!).Port;
            _ = Task.Run(() => ServeAsync(kind, auth, rejectSocks4));
        }

        public ushort Port { get; }

        private async Task ServeAsync(Kind kind, bool auth, bool rejectSocks4)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(_cts.Token);
            }
            catch (Exception)
            {
                return;
            }

            using var stream = new NetworkStream(client, ownsSocket: true);
            bool ok = true;
            switch (kind)
            {
                case Kind.Socks5:
                    await HandleSocks5Async(stream, auth);
                    break;
                case Kind.Socks4:
                    ok = await HandleSocks4Async(stream, remoteDns: false, reject: rejectSocks4, hasUserId: auth);
                    break;
                case Kind.Socks4a:
                    ok = await HandleSocks4Async(stream, remoteDns: true, reject: rejectSocks4, hasUserId: auth);
                    break;
                default:
                    await HandleHttpAsync(stream);
                    break;
            }

            if (!ok)
                return;

            // After the handshake, act as an echo server.
            byte[] buffer = new byte[256];
            while (!_cts.IsCancellationRequested)
            {
                int n;
                try
                {
                    n = await stream.ReadAsync(buffer, _cts.Token);
                }
                catch (Exception)
                {
                    return;
                }

                if (n == 0)
                    return;

                await stream.WriteAsync(buffer.AsMemory(0, n), _cts.Token);
                await stream.FlushAsync(_cts.Token);
            }
        }

        private async Task<bool> HandleSocks4Async(NetworkStream stream, bool remoteDns, bool reject, bool hasUserId)
        {
            byte[] head = new byte[8]; // VER CMD PORT(2) IP(4)
            await ReadExactAsync(stream, head);
            Assert.Equal((byte)0x04, head[0]);
            Assert.Equal((byte)0x01, head[1]);
            int port = (head[2] << 8) | head[3];
            Assert.Equal(25565, port);

            if (remoteDns)
                Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x01 }, head[4..8]);

            else
                Assert.Equal(new byte[] { 127, 0, 0, 1 }, head[4..8]);

            byte[] userId = await ReadUntilZeroAsync(stream);
            if (hasUserId)
                Assert.Equal("neo"u8.ToArray(), userId);

            else
                Assert.Empty(userId);

            if (remoteDns)
            {
                byte[] host = await ReadUntilZeroAsync(stream);
                Assert.Equal("target.example"u8.ToArray(), host);
            }

            byte[] reply = reject
                ? [0x00, 0x5B, 0, 0, 0, 0, 0, 0]
                : [0x00, 0x5A, 0, 0, 0, 0, 0, 0];
            await stream.WriteAsync(reply, _cts.Token);
            await stream.FlushAsync(_cts.Token);
            return !reject;
        }

        private async Task<byte[]> ReadUntilZeroAsync(NetworkStream stream)
        {
            var bytes = new List<byte>();
            byte[] one = new byte[1];
            while (true)
            {
                await ReadExactAsync(stream, one);
                if (one[0] == 0x00)
                    return [.. bytes];

                bytes.Add(one[0]);
            }
        }

        private async Task HandleSocks5Async(NetworkStream stream, bool auth)
        {
            byte[] greeting = new byte[2];
            await ReadExactAsync(stream, greeting);
            byte[] methods = new byte[greeting[1]];
            await ReadExactAsync(stream, methods);

            if (auth)
            {
                await stream.WriteAsync(new byte[] { 0x05, 0x02 }, _cts.Token); // choose user/pass
                byte[] head = new byte[2];
                await ReadExactAsync(stream, head); // ver + ulen
                byte[] user = new byte[head[1]];
                await ReadExactAsync(stream, user);
                byte[] plen = new byte[1];
                await ReadExactAsync(stream, plen);
                byte[] pass = new byte[plen[0]];
                await ReadExactAsync(stream, pass);
                await stream.WriteAsync(new byte[] { 0x01, 0x00 }, _cts.Token); // auth ok
            }
            else
            {
                await stream.WriteAsync(new byte[] { 0x05, 0x00 }, _cts.Token); // no auth
            }

            // CONNECT request: VER CMD RSV ATYP...
            byte[] reqHead = new byte[4];
            await ReadExactAsync(stream, reqHead);
            int addrLen = reqHead[3] switch
            {
                0x01 => 4,
                0x04 => 16,
                0x03 => await ReadDomainLenAsync(stream),
                _ => 0,
            };
            byte[] rest = new byte[addrLen + 2];
            await ReadExactAsync(stream, rest);

            // Reply: success, bound addr 0.0.0.0:0.
            await stream.WriteAsync(new byte[] { 0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 }, _cts.Token);
            await stream.FlushAsync(_cts.Token);
        }

        private async Task<int> ReadDomainLenAsync(NetworkStream stream)
        {
            byte[] len = new byte[1];
            await ReadExactAsync(stream, len);
            return len[0];
        }

        private async Task HandleHttpAsync(NetworkStream stream)
        {
            // Read request lines until a blank line.
            var sb = new StringBuilder();
            byte[] one = new byte[1];
            while (!sb.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                int n = await stream.ReadAsync(one.AsMemory(0, 1), _cts.Token);
                if (n == 0)
                    return;

                sb.Append((char)one[0]);
            }

            Assert.StartsWith("CONNECT ", sb.ToString());
            byte[] ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n");
            await stream.WriteAsync(ok, _cts.Token);
            await stream.FlushAsync(_cts.Token);
        }

        private async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(read), _cts.Token);
                if (n == 0)
                    throw new IOException("proxy client closed early");

                read += n;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Dispose();
            _cts.Dispose();
        }
    }
}
