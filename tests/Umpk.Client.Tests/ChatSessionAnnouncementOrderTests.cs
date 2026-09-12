using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Corpus;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The client announces its chat session only after the join packet on protocols 761, 762, and 763.</summary>
/// <remarks>
/// <para>The server keeps its inbound decoder in login until it sends its first play packet, which is the join packet. On protocol 761, the login packet set contains only hello, key, and custom_query.</para>
/// <para>A premature <c>chat_session_update</c> therefore uses a play wire ID against the login packet set. The server disconnects, and the client can misread the login disconnect under its play table.</para>
/// <para>The required order is silence before the join packet and an announcement immediately afterward.</para>
/// </remarks>
public sealed class ChatSessionAnnouncementOrderTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>How long the server waits for a frame it must NOT receive. The assertion fails immediately if the announcement is awaited inside <c>ConnectAsync</c>, because the frame is already queued by the time the connect task completes.</summary>
    private static readonly TimeSpan SilenceGrace = TimeSpan.FromMilliseconds(400);

    [Theory]
    [InlineData(761)]
    [InlineData(762)]
    [InlineData(763)]
    public async Task NoPlayFrameIsSentBeforeTheJoinPacket_AndTheSessionIsAnnouncedRightAfterIt(int protocol)
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        ProtocolDescriptor descriptor = version.Protocol;
        await using FakeJavaServer server = FakeJavaServer.Create();

        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(protocol))
            .UseChatSigning(new FixedCertificateProvider(Certificates()))
            .ConfigureFeatures(f =>
            {
                f.Physics = false;
                f.Pathfinding = false;
            })
            .Build();

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, descriptor, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);

        Assert.False(connect.IsCompleted);

        // THE ASSERTION. The server has sent no play packet, so its inbound decoder is still the LOGIN one: anything arriving here is decoded against a three-entry list and kills the session.
        using (var silence = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            silence.CancelAfter(SilenceGrace);
            InboundFrame? early = null;
            try
            {
                early = await server.NextFrameAsync(silence.Token);
            }
            catch (OperationCanceledException) when (silence.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Expected: silence until the join packet.
            }

            Assert.True(
                early is null,
                $"The client sent wire id 0x{early?.WireId:X2} before the join packet; on protocol "
                + $"{protocol} the server is still decoding against the LOGIN packet set there.");
        }

        // The real recorded join packet for this protocol, replayed byte for byte from the corpus, so the applier chain runs on the same bytes a live server sends.
        (int joinWireId, byte[] joinBody) = RecordedJoinFrame(protocol);
        await server.SendFrameAsync(joinWireId, joinBody, ct);
        await connect.WaitAsync(Budget, ct);

        // And now it is allowed, and it happens: the announcement is the first thing the client sends.
        InboundFrame announced = await server.NextFrameAsync(ct);
        Assert.Equal(
            new Internal.WireIndex(version).ServerboundPlay(PlayPackets.Serverbound.ChatSessionUpdate.Id),
            announced.WireId);
    }

    /// <summary>Vanilla does not switch its serverbound decoder from LOGIN until it writes its first PLAY frame. A configured brand must therefore remain queued behind the client's first received play item; it is not a protocol-specific omission. This guards the distinct wire ids used by the affected 1.16–1.18 families.</summary>
    [Theory]
    [InlineData(751)]
    [InlineData(753)]
    [InlineData(754)]
    [InlineData(755)]
    [InlineData(756)]
    [InlineData(757)]
    [InlineData(758)]
    public async Task ConfiguredBrand_WaitsForTheFirstServerPlayItem(int protocol)
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(protocol))
            .ConfigureOptions(options => options.ClientBrand = "umpk-test")
            .ConfigureFeatures(features =>
            {
                features.Physics = false;
                features.Pathfinding = false;
            })
            .Build();

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, version.Protocol, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);

        Assert.False(connect.IsCompleted);

        using var silence = CancellationTokenSource.CreateLinkedTokenSource(ct);
        silence.CancelAfter(SilenceGrace);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await server.NextFrameAsync(silence.Token));

        await ScriptedServer.SendPlayReadinessFrameAsync(server, version.Protocol, ct);
        await connect.WaitAsync(Budget, ct);

        int customPayloadWireId = new Internal.WireIndex(version).ServerboundPlay(Identifier.Minecraft("custom_payload"));
        for (int i = 0; i < 2; i++)
        {
            InboundFrame frame = await server.NextFrameAsync(ct);
            if (frame.WireId != customPayloadWireId)
                continue;

            var reader = new PacketReader(frame.Payload);
            Assert.Equal("minecraft:brand", reader.ReadString());
            Assert.Equal("umpk-test", reader.ReadString());
            return;
        }

        throw new Xunit.Sdk.XunitException("The configured client brand was not sent after the first play item.");
    }

    /// <summary>The <c>ClientboundLoginPacket</c> frame out of the recorded corpus for this protocol: the join packet on 759-763 carries the whole registry blob and nothing hand-built is a faithful stand-in.</summary>
    private static (int WireId, byte[] Body) RecordedJoinFrame(int protocol)
    {
        string root = FindCorpusRoot();
        Assert.True(root is not null, "the corpus fixtures are missing");
        Assert.True(
            JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        Assert.True(
            version.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));
        int joinWireId = registry.Packets
            .Where(p => p.Type.Id == PlayPackets.Clientbound.Login.Id)
            .Select(p => p.WireId)
            .Single();

        foreach (string path in CorpusLoader.DiscoverCaptures(Path.Combine(root!, protocol.ToString(System.Globalization.CultureInfo.InvariantCulture))))
        {
            LoadedCorpus corpus = CorpusLoader.LoadFileAsync(path).GetAwaiter().GetResult();
            foreach (RecordedFrame frame in corpus.Frames)
                if (frame.Direction == CorpusDirection.Clientbound
                    && frame.Phase == CorpusPhase.Play
                    && frame.WireId == joinWireId)
                    return (joinWireId, [.. frame.Body]);

        }

        throw new Xunit.Sdk.XunitException($"no recorded join frame for protocol {protocol}");
    }

    private static string FindCorpusRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "fixtures", "corpus");
            if (Directory.Exists(candidate))
                return candidate;

            dir = Path.GetDirectoryName(dir);
        }

        return null!;
    }

    private static PlayerCertificates Certificates()
    {
        using RSA rsa = RSA.Create(2048);
        byte[] signature = System.Text.Encoding.UTF8.GetBytes("v15-u6..");
        return new PlayerCertificates(
            rsa.ExportSubjectPublicKeyInfoPem(),
            rsa.ExportPkcs8PrivateKeyPem(),
            Convert.ToBase64String(signature),
            Convert.ToBase64String(signature),
            DateTimeOffset.UtcNow.AddDays(2),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private static async Task SendAsync<TPacket>(
        FakeJavaServer server, ProtocolDescriptor descriptor, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket
    {
        Assert.True(descriptor.TryGetRegistry(phase, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != packet.Type.Id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
            await server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct);
            return;
        }

        throw new Xunit.Sdk.XunitException($"{packet.Type.Id} is not registered clientbound in {phase}.");
    }

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }

    private sealed class FixedCertificateProvider(PlayerCertificates certificates) : IChatSigningProvider
    {
        public ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<PlayerCertificates?>(certificates);
    }
}
