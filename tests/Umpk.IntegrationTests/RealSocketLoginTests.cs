using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>
/// The same client/server login drivers as <see cref="ServerClientLoginTests"/>, but over a real loopback TCP socket on an ephemeral OS-assigned port (bound to 127.0.0.1:0) rather than in-memory pipes, proving the drivers work across an actual kernel socket boundary with real framing, encryption, and phase gating. Compression is left disabled for the reason documented on <see cref="ServerClientLoginTests"/>.
///
/// <para>Unlike <see cref="LiveServerTests"/> (which share one external server on a fixed port and are serialized via <see cref="LiveServerCollection"/>), each case here spins up its own in-process listener on an ephemeral port with no shared state, so it is safe to run in the default parallel lane and is intentionally not placed in the serialized live-server collection.</para>
/// </summary>
public class RealSocketLoginTests
{
    private static JavaConnectionOptions FrameMode() => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
        InboundChannelCapacity = 256,
    };

    private static IDuplexPipe WrapSocket(Socket socket)
    {
        socket.NoDelay = true;
        var stream = new NetworkStream(socket, ownsSocket: true);
        return new StreamDuplexPipe(
            PipeReader.Create(stream),
            PipeWriter.Create(stream));
    }

    [Theory]
    [InlineData(770)]
    [InlineData(776)]
    public async Task Online_OverLoopbackTcp_BothReachPlay(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var profile = new GameProfile(Guid.NewGuid(), "SocketTester");
        var verifier = new FakeSessionService(profile);

        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        // Bind to an ephemeral port and read back the OS-assigned port, so parallel test suites never contend on a fixed port number.
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

        Task<Socket> acceptTask = listener.AcceptAsync(cts.Token).AsTask();

        using var clientSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await clientSocket.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        Socket serverSocket = await acceptTask;

        var client = new JavaConnection(WrapSocket(clientSocket), FrameMode());
        var server = new JavaConnection(WrapSocket(serverSocket), FrameMode());
        client.BindCodec(new DescriptorFrameCodecBinding(version.Protocol), PacketFlow.Clientbound);
        server.BindCodec(new DescriptorFrameCodecBinding(version.Protocol), PacketFlow.Serverbound);
        client.Start();
        server.Start();

        await using (client)
        await using (server)
        {
            using RSA keyPair = RSA.Create(2048);
            var serverOptions = new JavaServerLoginOptions
            {
                KeyPair = keyPair,
                Verifier = verifier,
                CompressionThreshold = -1,
                ClientIp = IPAddress.Loopback,
            };

            Task<ServerLoginResult> serverTask = JavaServerLogin.AcceptAsync(server, version, serverOptions, cts.Token);
            Task<LoginResult> clientTask = JavaClientLogin.LoginAsync(client, version, new JavaLoginOptions
            {
                Username = "SocketTester",
                ServerHost = "localhost",
                ServerPort = (ushort)port,
                Authenticator = verifier,
                Credentials = new ProfileCredentials(profile, "fake-access-token"),
            }, cts.Token);

            LoginResult clientResult = await clientTask;
            ServerLoginResult serverResult = await serverTask;

            Assert.Equal(ProtocolPhase.Play, clientResult.Phase);
            Assert.Equal(ProtocolPhase.Play, serverResult.Phase);
            Assert.True(serverResult.OnlineMode);
            Assert.Equal(profile.Id, serverResult.Profile.Id);
            Assert.Equal(profile.Id, clientResult.Uuid);
            Assert.Equal(verifier.JoinHash, verifier.VerifyHash);

            // Post-login traffic crosses the now-encrypted socket intact.
            byte[] probe = new byte[300];
            Random.Shared.NextBytes(probe);
            await server.SendFrameAsync(0x7F, probe, cts.Token);
            InboundItem item = await client.ReceiveAsync(cts.Token);
            Assert.Equal(0x7F, item.Frame.WireId);
            Assert.Equal(probe, item.Frame.Payload.ToArray());
        }
    }

    private sealed class StreamDuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;

        public PipeWriter Output { get; } = output;
    }

    private sealed class FakeSessionService(GameProfile profile) : ISessionAuthenticator, IServerSessionVerifier
    {
        public string? JoinHash { get; private set; }

        public string? VerifyHash { get; private set; }

        public ValueTask JoinServerAsync(string serverIdHash, ProfileCredentials credentials, CancellationToken ct)
        {
            JoinHash = serverIdHash;
            return ValueTask.CompletedTask;
        }

        public ValueTask<GameProfile?> VerifyJoinAsync(string username, string serverIdHash, IPAddress? clientIp, CancellationToken ct)
        {
            VerifyHash = serverIdHash;
            return ValueTask.FromResult<GameProfile?>(profile);
        }
    }
}
