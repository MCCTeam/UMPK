using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Chat preview: what this client does NOT do, made observable instead of silent.</summary>
/// <remarks>
/// <para>UMPK drives no chat-preview round trip on any version, so its serverbound chat writes <c>signedPreview = false</c> and its signature covers the message's PLAIN text rather than the server's decoration of it. <b>That is a correct wire behaviour, not a broken send</b>, and these tests pin both halves of that claim.</para>
/// <para>When <c>signedPreview</c> is false, the server verifies the signature against the plain text and carries the decoration as <c>unsignedContent</c>. The message is still delivered.</para>
/// <para>The client therefore reports a notice instead of refusing the send.</para>
/// </remarks>
public sealed class ChatPreviewRefusalTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>1.19 and 1.19.2: the only two protocols that have chat preview.</summary>
    private const int V1 = 759;
    private const int V2 = 760;

    /// <summary><c>minecraft:set_display_chat_preview</c> contains one Boolean byte and no other fields.</summary>
    /// <remarks>Asserted as BYTES rather than as a round trip. A round trip through one codec agrees with itself whatever the codec writes; the byte is what a vanilla server actually puts on the wire.</remarks>
    [Theory]
    [InlineData(V1, true, (byte)0x01)]
    [InlineData(V1, false, (byte)0x00)]
    [InlineData(V2, true, (byte)0x01)]
    [InlineData(V2, false, (byte)0x00)]
    public void SetDisplayChatPreview_IsExactlyVanillasWriteBoolean(int protocol, bool enabled, byte expected)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "set_display_chat_preview");
        Assert.True(codec.IsImplemented);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, new ClientboundSetDisplayChatPreviewPacket(enabled), PacketCodecContext.Registryless);

        Assert.Equal([expected], buffer.WrittenSpan.ToArray());

        // Decode(span, ...) is frame-exact: a trailing byte raises rather than being ignored, so this also asserts the codec consumes the whole one-byte body.
        var decoded = Assert.IsType<ClientboundSetDisplayChatPreviewPacket>(
            codec.Decode(buffer.WrittenSpan, PacketCodecContext.Registryless));
        Assert.Equal(enabled, decoded.Enabled);
    }

    /// <summary>Cross-era rejection. The chat-preview toggle exists on 759 and 760 and on NO other protocol: 1.19 introduced the preview family and 1.19.3 deleted it. A binding that leaked past either boundary would decode a wire id belonging to a different packet.</summary>
    /// <remarks>Requires the codec to be IMPLEMENTED, not merely the identity to be present: an unbound marker also carries the identity in its phase registry. The sibling <c>SetDisplayChatPreview_IsExactlyVanillasWriteBoolean</c> already asserts <c>IsImplemented</c> for 759 and 760 specifically; this test's OWN job - which its name promises - is to also rule out every OTHER protocol, so it needs the same assertion inside its own loop rather than borrowing the sibling's coverage by accident.</remarks>
    [Fact]
    public void SetDisplayChatPreview_IsRegisteredOnExactlyTwoProtocols()
    {
        List<int> carried = [];
        foreach (int protocol in JavaVersions.All.Select(v => v.Version.Protocol).Distinct().Order())
        {
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
            Assert.True(version.Protocol.TryGetRegistry(
                ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));

            foreach ((int wireId, PacketType type) in registry.Packets)
            {
                if (type.Id != UiPackets.Clientbound.SetDisplayChatPreview.Id)
                    continue;

                Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
                Assert.True(codec.IsImplemented, $"set_display_chat_preview is an unbound marker on {protocol}.");
                carried.Add(protocol);
                break;
            }
        }

        Assert.Equal([V1, V2], carried);
    }

    /// <summary>The serverbound preview flag is written FALSE on both eras, and this pins the whole frame rather than the flag alone, so a build that moved the flag would fail here too.</summary>
    /// <remarks>The expected byte order is text, epoch-millisecond timestamp, salt, signature byte array, the preview boolean, and on 760 the acknowledgement block (an empty counted list plus an absent optional). 759 has no acknowledgement block at all, which is the era difference this also pins.</remarks>
    [Theory]
    [InlineData(V1)]
    [InlineData(V2)]
    public void ServerboundChat_WritesTheUnpreviewedFlag(int protocol)
    {
        var packet = new ServerboundSignedChatPacket(
            "hi", TimestampMillis: 0x0102030405060708L, Salt: 0x1112131415161718L,
            Signature: [0xAA, 0xBB], LastSeen: new LastSeenMessagesUpdate(0, new byte[3], 0));

        byte[] actual = BoundDescriptorCodec.EncodeServerbound(protocol, packet);

        byte[] head =
        [
            0x02, (byte)'h', (byte)'i',                          // writeUtf("hi")
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,      // writeInstant -> epoch millis, big endian
            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,      // writeLong(salt)
            0x02, 0xAA, 0xBB,                                    // writeByteArray(signature)
            0x00,                                                // writeBoolean(signedPreview) == FALSE
        ];

        // 760 appends update behavior: a VarInt-counted list of entries, then readOptional's present flag for the last-received entry. Empty and absent here.
        byte[] expected = protocol == V2 ? [.. head, 0x00, 0x00] : head;
        Assert.Equal(expected, actual);
    }

    /// <summary>A previewing server is RECORDED and ANNOUNCED, which is the whole reason the toggle is decoded. Before this it was a declared marker, so a session against such a server signed over the plain text with nothing anywhere saying so.</summary>
    [Theory]
    [InlineData(V1)]
    [InlineData(V2)]
    public async Task AServerThatPreviewsChat_IsRecordedAndAnnounced(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var harness = new ApplierHarness(version);
        List<ChatPreviewAnnounced> announced = [];
        harness.Events.Subscribe<ChatPreviewAnnounced>(announced.Add);

        Assert.False(harness.State.Chat.ServerPreviewsChat);

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            protocol, "set_display_chat_preview", new ClientboundSetDisplayChatPreviewPacket(true)));

        Assert.True(harness.State.Chat.ServerPreviewsChat);
        Assert.Equal(new ChatPreviewAnnounced(true, protocol), Assert.Single(announced));

        // And the server turning it back off is followed, not latched.
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            protocol, "set_display_chat_preview", new ClientboundSetDisplayChatPreviewPacket(false)));

        Assert.False(harness.State.Chat.ServerPreviewsChat);
        Assert.Equal(2, announced.Count);
        Assert.False(announced[1].Enabled);
    }

    /// <summary>The toggle belongs to one server on one negotiated version and cannot survive a reconnect.</summary>
    [Fact]
    public async Task PreviewState_DoesNotSurviveTheSession()
    {
        Assert.True(JavaVersions.TryGetByProtocol(V2, out JavaVersion version));
        var harness = new ApplierHarness(version);
        await harness.ApplyAsync(new ClientboundSetDisplayChatPreviewPacket(true));
        Assert.True(harness.State.Chat.ServerPreviewsChat);

        harness.State.ResetForSessionEnd();

        Assert.False(harness.State.Chat.ServerPreviewsChat);
    }

    /// <summary>The whole item, driven through a real <see cref="UmpkClient"/> over a real <see cref="JavaConnection"/> against a scripted 1.19.2 server that announces chat preview: the client says out loud what it is not doing, records it where a consumer can read it, and still puts a message on the wire with the preview flag false.</summary>
    /// <remarks>The log assertion is the effect that did not exist before. The frame assertion is the one that must NOT change: refusing to send here would refuse a message a previewing 1.19.2 server accepts and broadcasts.</remarks>
    [Fact]
    public async Task On760_APreviewingServer_IsAnnouncedAndTheSendStillGoesOutUnpreviewed()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        Assert.True(JavaVersions.TryGetByProtocol(V2, out JavaVersion version));
        ProtocolDescriptor descriptor = version.Protocol;
        await using FakeJavaServer server = FakeJavaServer.Create();
        var logs = new CapturingLoggerFactory();

        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(V2))
            .UseLoggerFactory(logs)
            .ConfigureFeatures(f =>
            {
                f.Physics = false;
                f.Pathfinding = false;
            })
            .Build();

        var announced = new TaskCompletionSource<ChatPreviewAnnounced>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<ChatPreviewAnnounced>(e => announced.TrySetResult(e));

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, descriptor, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await ScriptedServer.SendPlayReadinessFrameAsync(server, descriptor, ct);
        await connect.WaitAsync(Budget, ct);

        // The server announces that it previews chat.
        await SendAsync(server, descriptor, ProtocolPhase.Play,
            new ClientboundSetDisplayChatPreviewPacket(true), ct);

        ChatPreviewAnnounced evt = await announced.Task.WaitAsync(Budget, ct);
        Assert.True(evt.Enabled);
        Assert.Equal(V2, evt.Protocol);

        await WaitForAsync(() => client.State.Chat.ServerPreviewsChat, ct);

        // The notice names the protocol and says what the signature covers.
        await WaitForAsync(
            () => logs.Entries.Any(e =>
                e.Contains("chat preview enabled", StringComparison.Ordinal)
                && e.Contains("760", StringComparison.Ordinal)
                && e.Contains("plain text", StringComparison.Ordinal)),
            ct);

        // The message still goes out, and its preview flag is false. Byte 0 of the frame is the wire id, then vanilla's ServerboundChatPacket field order; the flag is the byte right after the signature array and right before the empty last-seen block.
        await client.Actions.Chat.SendChatAsync("hello", ct);
        (int wireId, byte[] body) = await NextPlayFrameAsync(server, ct);

        Assert.Equal(
            new Internal.WireIndex(version).ServerboundPlay(PlayPackets.Serverbound.SignedChat.Id), wireId);
        // 0x05 is the VarInt length prefix for "hello". Keep it as an explicit byte because a control character inside a string literal is invisible in common editors and diffs.
        Assert.Equal([0x05, .. "hello"u8], body[..6]);

        // Trailing three bytes on 760: preview flag, then update behavior (empty list, absent optional). An unsigned send writes a zero-length signature array, so the flag is the byte after it and the frame tail is fully determined.
        Assert.Equal([0x00, 0x00, 0x00], body[^3..]);
    }

    private static async Task<(int WireId, byte[] Body)> NextPlayFrameAsync(
        FakeJavaServer server, CancellationToken ct)
    {
        InboundFrame frame = await server.NextFrameAsync(ct);
        return (frame.WireId, frame.CopyPayload());
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
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
            Assert.True(codec.IsImplemented, $"{packet.Type.Id} is a marker in {phase}.");
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

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyList<string> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new Recorder(_entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class Recorder(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(formatter(state, exception));
        }
    }
}
