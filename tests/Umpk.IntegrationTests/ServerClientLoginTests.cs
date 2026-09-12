using System.Net;
using System.Security.Cryptography;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>
/// End-to-end login validation over in-memory pipes: the UMPK client login driver (<see cref="JavaClientLogin"/>) against the UMPK server login driver (<see cref="JavaServerLogin"/>), both offline and full online-mode (real AES-CFB8 encryption on both legs, with a fake <see cref="ISessionAuthenticator"/>/<see cref="IServerSessionVerifier"/> pair). Run on the modern 1.21.5 which has the configuration phase, and 26.2 for the session-id era.
///
/// Compression is enabled here (threshold 64, plus a threshold-0 edge case that compresses every frame): the read loop parks at the set-compression frame boundary until the driver enables compression, so the next frame is read with the compressed reader even over a zero-latency in-memory pipe. If that gate regressed, login_finished (the first compressed frame) would decode with the uncompressed reader and the login would fault. Encryption, enabled at the same protocol position, is exercised alongside.
/// </summary>
public class ServerClientLoginTests
{
    private static JavaConnectionOptions FrameMode() => new()
    {
        UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        ReadIdleTimeout = TimeSpan.Zero,
        InboundChannelCapacity = 256,
    };

    private static (JavaConnection Client, JavaConnection Server) Pair(JavaVersion version)
    {
        DuplexPipePair pipes = DuplexPipePair.Create();
        var client = new JavaConnection(pipes.Left, FrameMode());
        var server = new JavaConnection(pipes.Right, FrameMode());
        client.BindCodec(new DescriptorFrameCodecBinding(version.Protocol), PacketFlow.Clientbound);
        server.BindCodec(new DescriptorFrameCodecBinding(version.Protocol), PacketFlow.Serverbound);
        client.Start();
        server.Start();
        return (client, server);
    }

    [Theory]
    [InlineData(770, 64)]
    [InlineData(770, 0)]
    [InlineData(776, 64)]
    [InlineData(776, 0)]
    // Protocols 759 and 760 reach login_finished directly because they predate the configuration phase.
    [InlineData(759, 64)]
    [InlineData(760, 64)]
    public async Task Offline_ClientReachesPlay_ServerReachesPlay(int protocol, int compressionThreshold)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        (JavaConnection client, JavaConnection server) = Pair(version);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await using (client)
        await using (server)
        {
            var serverOptions = new JavaServerLoginOptions
            {
                KeyPair = RSA.Create(2048),
                Verifier = null, // offline
                CompressionThreshold = compressionThreshold,
            };

            Task<ServerLoginResult> serverTask = JavaServerLogin.AcceptAsync(server, version, serverOptions, cts.Token);
            Task<LoginResult> clientTask = JavaClientLogin.LoginAsync(client, version, new JavaLoginOptions
            {
                Username = "OfflineTester",
                ServerHost = "localhost",
                ServerPort = 25565,
            }, cts.Token);

            LoginResult clientResult = await clientTask;
            ServerLoginResult serverResult = await serverTask;

            Assert.Equal(ProtocolPhase.Play, clientResult.Phase);
            Assert.Equal(ProtocolPhase.Play, serverResult.Phase);
            Assert.Equal("OfflineTester", clientResult.Username);
            Assert.Equal(clientResult.Uuid, serverResult.Profile.Id);
            Assert.False(serverResult.OnlineMode);

            // Both legs enabled compression at the set-compression frame boundary; reaching play proves login_finished (the first compressed frame) decoded with the compressed reader.
            Assert.True(client.CompressionEnabled);
            Assert.True(server.CompressionEnabled);

            // A post-login frame larger than the threshold crosses the compressed channel intact.
            byte[] probe = new byte[300];
            Random.Shared.NextBytes(probe);
            await server.SendFrameAsync(0x7F, probe, cts.Token);
            InboundItem item = await client.ReceiveAsync(cts.Token);
            Assert.Equal(0x7F, item.Frame.WireId);
            Assert.Equal(probe, item.Frame.Payload.ToArray());

            serverOptions.KeyPair.Dispose();
        }
    }

    [Theory]
    [InlineData(770, 64)]
    [InlineData(770, 0)]
    [InlineData(776, 64)]
    [InlineData(776, 0)]
    // Protocols 759/760 changed the ServerboundKeyPacket wire shape (see LoginCodecs.KeyV1_19) even on the path with NO profile key attached (this client sets neither ProfileKey nor ProfileCertificates below), because the era wraps the field in an Either UNCONDITIONALLY. This is the first time 759/760 runs through the full JavaClientLogin<->JavaServerLogin online-mode driver pair; it proves the new codec's plain/"left" branch still completes a real encrypted login end to end. The signed-challenge ("a profile key WAS presented") branch is not exercised here: JavaServerLogin has no profile-key/signed-challenge validation of its own to drive against (out of scope for this client/server integration); that branch is proven by the pinned-frame wire tests, the ProfileKeyChallenge sign/verify round trip, and the live vanilla-server run.
    [InlineData(759, 64)]
    [InlineData(760, 64)]
    public async Task Online_FullEncryptionHandshake_BothReachPlay(int protocol, int compressionThreshold)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        (JavaConnection client, JavaConnection server) = Pair(version);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var profile = new GameProfile(Guid.NewGuid(), "OnlineTester");
        var verifier = new FakeSessionService(profile);

        await using (client)
        await using (server)
        {
            using RSA keyPair = RSA.Create(2048);
            var serverOptions = new JavaServerLoginOptions
            {
                KeyPair = keyPair,
                Verifier = verifier,
                CompressionThreshold = compressionThreshold,
                ClientIp = IPAddress.Loopback,
            };

            Task<ServerLoginResult> serverTask = JavaServerLogin.AcceptAsync(server, version, serverOptions, cts.Token);
            Task<LoginResult> clientTask = JavaClientLogin.LoginAsync(client, version, new JavaLoginOptions
            {
                Username = "OnlineTester",
                ServerHost = "localhost",
                ServerPort = 25565,
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

            // Both roles agreed on the same server-id hash (the join token the fake verifier matched).
            Assert.Equal(verifier.JoinHash, verifier.VerifyHash);
            Assert.NotNull(verifier.JoinHash);

            Assert.True(client.CompressionEnabled);
            Assert.True(server.CompressionEnabled);

            // Post-login traffic crosses the now-encrypted, compressed channel intact.
            byte[] probe = new byte[300];
            Random.Shared.NextBytes(probe);
            await server.SendFrameAsync(0x7F, probe, cts.Token);
            InboundItem item = await client.ReceiveAsync(cts.Token);
            Assert.Equal(0x7F, item.Frame.WireId);
            Assert.Equal(probe, item.Frame.Payload.ToArray());
        }
    }

    /// <summary>A fake session service that unconditionally verifies and records the hashes it saw.</summary>
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
