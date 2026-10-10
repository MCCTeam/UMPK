using System.Security.Cryptography;
using Umpk.Protocol.Java.Crypto;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

public sealed class LoginEncryptionTests
{
    public static TheoryData<int, int> OfflineEncryptionCases => new()
    {
        { 766, 0 }, { 766, 1 }, { 766, 2 }, { 766, 3 },
        { 770, 0 }, { 770, 1 }, { 770, 2 }, { 770, 3 },
        { 776, 0 }, { 776, 1 }, { 776, 2 }, { 776, 3 },
    };

    [Theory]
    [MemberData(nameof(OfflineEncryptionCases))]
    public Task EncryptionWithoutAuthentication_SkipsSessionJoin(int protocol, int configured)
        => CompleteLoginAsync(protocol, configured, requestEncryption: true, shouldAuthenticate: false);

    [Theory]
    [InlineData(47)]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(765)]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(776)]
    public Task AuthenticatedEncryption_JoinsBeforeSendingKey(int protocol)
        => CompleteLoginAsync(protocol, configured: 3, requestEncryption: true, shouldAuthenticate: true);

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public Task NoEncryption_SkipsSessionJoin(int configured)
        => CompleteLoginAsync(776, configured, requestEncryption: false, shouldAuthenticate: false);

    [Theory]
    // Before 1.20.5 the flag is absent on the wire, so even false must decode as true.
    [InlineData(765, 0)]
    [InlineData(765, 1)]
    [InlineData(765, 2)]
    [InlineData(766, 0)]
    [InlineData(766, 1)]
    [InlineData(766, 2)]
    public async Task AuthenticationRequired_MissingCredentialsFails(int protocol, int configured)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using FakeJavaServer server = FakeJavaServer.Create();
        JavaVersion version = Version(protocol);
        await using JavaConnection client = Client(server, version);
        var auth = new RecordingAuthenticator();
        Task<LoginResult> login = JavaClientLogin.LoginAsync(client, version, Options(configured, auth), cts.Token);
        await ReceiveStartAsync(server, cts.Token);
        using RSA rsa = RSA.Create(2048);
        await server.SendFrameAsync(1, BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:hello")
            .Encode(new ClientboundHelloPacket("", rsa.ExportSubjectPublicKeyInfo(), [1, 2, 3, 4], protocol >= 766)), cts.Token);

        await Assert.ThrowsAsync<ProtocolViolationException>(() => login);
        Assert.Equal(0, auth.Calls);
        Assert.False(client.IsEncrypted);
    }

    [Fact]
    public async Task SessionJoinRejection_DoesNotEnableEncryption()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using FakeJavaServer server = FakeJavaServer.Create();
        JavaVersion version = Version(776);
        await using JavaConnection client = Client(server, version);
        var auth = new RecordingAuthenticator { Reject = true };
        Task<LoginResult> login = JavaClientLogin.LoginAsync(client, version, Options(3, auth), cts.Token);
        await ReceiveStartAsync(server, cts.Token);
        using RSA rsa = RSA.Create(2048);
        await server.SendFrameAsync(1, BoundCodec.LoginAt(776, PacketFlow.Clientbound, "minecraft:hello")
            .Encode(new ClientboundHelloPacket("", rsa.ExportSubjectPublicKeyInfo(), [1, 2, 3, 4], true)), cts.Token);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => login);
        Assert.Equal(1, auth.Calls);
        Assert.False(client.IsEncrypted);
    }

    private static async Task CompleteLoginAsync(int protocol, int configured, bool requestEncryption, bool shouldAuthenticate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        JavaVersion version = Version(protocol);
        await using JavaConnection client = Client(server, version);
        var auth = new RecordingAuthenticator();
        JavaLoginOptions options = Options(configured, auth);
        Task<LoginResult> login = JavaClientLogin.LoginAsync(client, version, options, ct);
        await ReceiveStartAsync(server, ct);

        if (requestEncryption)
        {
            using RSA rsa = RSA.Create(2048);
            byte[] publicKey = rsa.ExportSubjectPublicKeyInfo();
            byte[] token = [1, 2, 3, 4];
            await server.SendFrameAsync(1, BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:hello")
                .Encode(new ClientboundHelloPacket("", publicKey, token, shouldAuthenticate)), ct);
            Task<InboundFrame> responseTask = server.NextFrameAsync(ct).AsTask();
            await Task.WhenAny(login, responseTask);
            if (login.IsCompleted)
                await login; // Surface a login fault immediately, rather than timing out waiting for its response.
            InboundFrame response = await responseTask;
            Assert.Equal(1, response.WireId);
            var key = (ServerboundKeyPacket)BoundCodec.LoginAt(protocol, PacketFlow.Serverbound, "minecraft:key")
                .DecodeFrame(response.CopyPayload().ToArray());
            Assert.Equal(token, rsa.Decrypt(key.VerifyToken, RSAEncryptionPadding.Pkcs1));
            byte[] secret = rsa.Decrypt(key.SharedSecret, RSAEncryptionPadding.Pkcs1);
            Assert.Equal(16, secret.Length);
            Assert.Equal(shouldAuthenticate ? 1 : 0, auth.Calls);
            if (shouldAuthenticate)
            {
                Assert.Equal(MinecraftServerId.Compute("", secret, publicKey), auth.Hash);
                Assert.Same(options.Credentials, auth.Credentials);
                Assert.Equal(ct, auth.Token);
            }
            server.ServerConnection.EnableEncryption(secret);
        }

        // The first encrypted packet also negotiates compression. A key-boundary race or a skipped
        // encryption exchange cannot pass by simply returning a login result.
        await server.SendFrameAsync(3, BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_compression")
            .Encode(new ClientboundLoginCompressionPacket(0)), ct);
        server.EnableCompression(0);
        var profile = new GameProfile(Guid.NewGuid(), "EncryptionTest");
        await server.SendFrameAsync(2, BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_finished")
            .Encode(new ClientboundLoginFinishedPacket(profile.Id, profile.Name, [], Guid.NewGuid())), ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        LoginResult result = await login;
        Assert.Equal(ProtocolPhase.Play, result.Phase);
        Assert.Equal(requestEncryption && shouldAuthenticate, result.IsAuthenticated);
        Assert.Equal(profile.Id, result.Uuid);
        Assert.Equal(requestEncryption, client.IsEncrypted);
        Assert.True(client.CompressionEnabled);
        Assert.Equal(requestEncryption && shouldAuthenticate ? 1 : 0, auth.Calls);

        byte[] probe = RandomNumberGenerator.GetBytes(300);
        await server.SendFrameAsync(0x7F, probe, ct);
        InboundItem inbound = await client.ReceiveAsync(ct);
        Assert.Equal(probe, inbound.Frame.Payload.ToArray());
        await client.SendFrameAsync(0x7E, probe, ct);
        InboundFrame echoed = await server.NextFrameAsync(ct);
        Assert.Equal(probe, echoed.Payload.ToArray());
    }

    private static JavaVersion Version(int protocol)
    {
        var game = new GameVersion(GameEdition.Java, "test", protocol);
        var features = new ProtocolFeatures();
        var builder = new ProtocolDescriptorBuilder(game, features);
        PacketRegistrar.Register(builder, ProtocolPhase.Handshake, PacketFlow.Serverbound, 0, "minecraft:intention");
        PacketRegistrar.Register(builder, ProtocolPhase.Login, PacketFlow.Serverbound, 0, "minecraft:hello");
        PacketRegistrar.Register(builder, ProtocolPhase.Login, PacketFlow.Serverbound, 1, "minecraft:key");
        PacketRegistrar.Register(builder, ProtocolPhase.Login, PacketFlow.Clientbound, 1, "minecraft:hello");
        PacketRegistrar.Register(builder, ProtocolPhase.Login, PacketFlow.Clientbound, 2, "minecraft:login_finished");
        PacketRegistrar.Register(builder, ProtocolPhase.Login, PacketFlow.Clientbound, 3, "minecraft:login_compression");
        return new JavaVersion(game, builder.Build(), features);
    }

    private static JavaConnection Client(FakeJavaServer server, JavaVersion version)
    {
        var client = new JavaConnection(server.ClientPipe, new JavaConnectionOptions
        {
            ReadIdleTimeout = TimeSpan.Zero,
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        });
        client.BindCodec(new DescriptorFrameCodecBinding(version.Protocol), PacketFlow.Clientbound);
        client.Start();
        return client;
    }

    private static async Task ReceiveStartAsync(FakeJavaServer server, CancellationToken ct)
    {
        Assert.Equal(0, (await server.NextFrameAsync(ct)).WireId);
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        Assert.Equal(0, (await server.NextFrameAsync(ct)).WireId);
    }

    private static JavaLoginOptions Options(int configured, RecordingAuthenticator auth) => new()
    {
        Username = "EncryptionTest",
        ServerHost = "localhost",
        ServerPort = 25565,
        Authenticator = (configured & 1) != 0 ? auth : null,
        Credentials = (configured & 2) != 0 ? new ProfileCredentials(new GameProfile(Guid.NewGuid(), "EncryptionTest"), "test-token") : null,
    };

    private sealed class RecordingAuthenticator : ISessionAuthenticator
    {
        public int Calls { get; private set; }
        public string? Hash { get; private set; }
        public ProfileCredentials? Credentials { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool Reject { get; init; }

        public ValueTask JoinServerAsync(string serverIdHash, ProfileCredentials credentials, CancellationToken ct)
        {
            Calls++;
            Hash = serverIdHash;
            Credentials = credentials;
            Token = ct;
            if (Reject)
                throw new UnauthorizedAccessException("Session join rejected.");
            return ValueTask.CompletedTask;
        }
    }
}
