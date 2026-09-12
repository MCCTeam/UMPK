using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Commands;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Commands;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The v1 (1.19, protocol 759) and v2 (1.19.1/1.19.2, protocol 760) OUTBOUND signed-message body.
/// <para>The login-start profile key was delivered on both protocols, but nothing ever produced a signed body for them: the send path signed only when the era was v3, and the signing coordinator built a last-seen window only for v3 too. So on 759 and 760 an authenticated client sent a null signature (an enforce-secure-profile server rejects it) and, once command sending arrived, computed every argument signature over an EMPTY last-seen list where vanilla folds in its v2 window.</para>
/// <para>Each case here signs with a real generated key pair and verifies the emitted signature against the packet's own timestamp, salt and declared window, so a body built over a different window fails rather than merely looking populated.</para>
/// </summary>
public sealed class ChatSignatureCompatibilityTests
{
    private const int V1Protocol = 759;
    private const int V2Protocol = 760;

    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");
    private static readonly Guid SessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Alex = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    /// <summary>1.19: the signature covers salt, sender uuid, epoch-second timestamp and the stable-JSON message, with no acknowledgement anywhere.</summary>
    [Fact]
    public async Task SendChat_On1_19_SignsTheMessage()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certificates = Certificates(rsa);
        var recorder = new RecordingSink();
        ChatActions chat = BuildChat(recorder, V1Protocol, State(certificates, ChatSignatureEra.V1_19));

        await chat.SendChatAsync("hello 1.19");

        var sent = Assert.IsType<ServerboundSignedChatPacket>(Assert.Single(recorder.Packets));
        Assert.Equal("hello 1.19", sent.Message);
        Assert.NotNull(sent.Signature);
        Assert.Equal(256, sent.Signature!.Length);
        Assert.Empty(sent.LegacyLastSeen);

        Assert.True(ChatSigningSession.Verify(
            certificates.PublicKeyPem, sent.Signature, ChatSignatureEra.V1_19,
            new ChatVerificationContext(
                Sender, SessionId, MessageIndex: 0, "hello 1.19",
                DateTimeOffset.FromUnixTimeMilliseconds(sent.TimestampMillis), sent.Salt, [])));

        // The frame resolves a concrete codec on the protocol-759 outbound table.
        Assert.NotEmpty(BoundDescriptorCodec.EncodeServerbound(V1Protocol, sent));
    }

    /// <summary>1.19.1/1.19.2: the signature is the two-stage header over the digest of a body that FOLDS IN the last-seen window, and the same window is repeated on the packet. Both come from one snapshot, so verifying the signature against the packet's declared window is the assertion that they agree.</summary>
    [Fact]
    public async Task SendChat_On1_19_2_SignsOverTheWindowItDeclares()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certificates = Certificates(rsa);

        var collector = new LastSeenMessagesCollector(LastSeenMessagesCollector.Window1_19);
        byte[] alexSignature = Fill(0x11);
        collector.Add(new AcknowledgedMessage(Alex, alexSignature));

        var recorder = new RecordingSink();
        ChatActions chat = BuildChat(
            recorder, V2Protocol, State(certificates, ChatSignatureEra.V1_19_1, collector));

        await chat.SendChatAsync("hello 1.19.2");

        var sent = Assert.IsType<ServerboundSignedChatPacket>(Assert.Single(recorder.Packets));
        Assert.NotNull(sent.Signature);

        // The declared window is the collector snapshot, not an empty list.
        LastSeenMessageEntry declared = Assert.Single(sent.LegacyLastSeen);
        Assert.Equal(Alex, declared.ProfileId);
        Assert.Equal(alexSignature, declared.Signature);

        Assert.True(ChatSigningSession.Verify(
            certificates.PublicKeyPem, sent.Signature!, ChatSignatureEra.V1_19_1,
            new ChatVerificationContext(
                Sender, SessionId, MessageIndex: 0, "hello 1.19.2",
                DateTimeOffset.FromUnixTimeMilliseconds(sent.TimestampMillis), sent.Salt,
                [new AcknowledgedMessage(Alex, alexSignature)], PrecedingSignature: null)));

        Assert.NotEmpty(BoundDescriptorCodec.EncodeServerbound(V2Protocol, sent));
    }

    /// <summary>The v2 signature is NOT the same as a v2 signature over an empty window, so the empty-window body over an empty window is a distinguishable, rejectable message rather than an equivalent one. Without this the previous test would pass against a build that ignored the window on both sides.</summary>
    [Fact]
    public async Task SendChat_On1_19_2_WindowActuallyChangesTheSignature()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certificates = Certificates(rsa);

        var collector = new LastSeenMessagesCollector(LastSeenMessagesCollector.Window1_19);
        collector.Add(new AcknowledgedMessage(Alex, Fill(0x11)));

        var recorder = new RecordingSink();
        ChatActions chat = BuildChat(
            recorder, V2Protocol, State(certificates, ChatSignatureEra.V1_19_1, collector));
        await chat.SendChatAsync("hello 1.19.2");

        var sent = Assert.IsType<ServerboundSignedChatPacket>(Assert.Single(recorder.Packets));

        Assert.False(
            ChatSigningSession.Verify(
                certificates.PublicKeyPem, sent.Signature!, ChatSignatureEra.V1_19_1,
                new ChatVerificationContext(
                    Sender, SessionId, MessageIndex: 0, "hello 1.19.2",
                    DateTimeOffset.FromUnixTimeMilliseconds(sent.TimestampMillis), sent.Salt, [])),
            "the signature verified against an EMPTY window, so the window is not in the signed body");
    }

    /// <summary>The window has to come from somewhere: the chat applier feeds the v2 collector from each inbound signed <c>player_chat</c>, carrying the SENDER uuid, because the v2 body hashes the uuid alongside the signature. The collector must be fed from this production path.</summary>
    [Fact]
    public async Task InboundSignedChat_FeedsTheV2Collector_AndTheNextSendCarriesIt()
    {
        Assert.True(JavaVersions.TryGetByProtocol(V2Protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);

        byte[] peerSignature = Fill(0x42);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            V2Protocol, "player_chat",
            new ClientboundPlayerChatPacket(
                Alex, Index: 0, Signature: peerSignature, SignedContent: "hi there",
                TimestampMillis: 1_700_000_000_000L, Salt: 5,
                UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Alex"), TargetName: null)));

        AcknowledgedMessage collected = Assert.Single(harness.LegacyLastSeenCollector.Snapshot());
        Assert.Equal(Alex, collected.ProfileId);
        Assert.Equal(peerSignature, collected.Signature);

        // The same collector instance drives the send path, which is how the wire window and the signed body stay the same window.
        using RSA rsa = RSA.Create(2048);
        var recorder = new RecordingSink();
        ChatActions chat = BuildChat(
            recorder, V2Protocol, State(Certificates(rsa), ChatSignatureEra.V1_19_1, harness.LegacyLastSeenCollector));

        await chat.SendChatAsync("acknowledged");

        var sent = Assert.IsType<ServerboundSignedChatPacket>(Assert.Single(recorder.Packets));
        LastSeenMessageEntry declared = Assert.Single(sent.LegacyLastSeen);
        Assert.Equal(Alex, declared.ProfileId);
        Assert.Equal(peerSignature, declared.Signature);
    }

    /// <summary>On protocol 760 each signable argument must be signed over the same v2 last-seen window declared by the packet.</summary>
    [Fact]
    public async Task SendCommand_On1_19_2_SignsArgumentsOverTheV2Window()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certificates = Certificates(rsa);

        var collector = new LastSeenMessagesCollector(LastSeenMessagesCollector.Window1_19);
        byte[] alexSignature = Fill(0x11);
        collector.Add(new AcknowledgedMessage(Alex, alexSignature));

        var recorder = new RecordingSink();
        ChatActions chat = BuildChat(
            recorder, V2Protocol, State(certificates, ChatSignatureEra.V1_19_1, collector), MsgCommandTree());

        await chat.SendCommandAsync("/msg Steve hello world");

        var sent = Assert.IsType<ServerboundSignedChatCommandPacket>(Assert.Single(recorder.Packets));
        Assert.Equal("msg Steve hello world", sent.Command);

        LastSeenMessageEntry declared = Assert.Single(sent.LegacyLastSeen);
        Assert.Equal(Alex, declared.ProfileId);
        Assert.Equal(alexSignature, declared.Signature);

        SignedCommandArgument argument = Assert.Single(sent.ArgumentSignatures);
        Assert.Equal("message", argument.Name);
        Assert.True(ChatSigningSession.Verify(
            certificates.PublicKeyPem, argument.Signature, ChatSignatureEra.V1_19_1,
            new ChatVerificationContext(
                Sender, SessionId, MessageIndex: 0, "hello world",
                DateTimeOffset.FromUnixTimeMilliseconds(sent.TimestampMillis), sent.Salt,
                [new AcknowledgedMessage(Alex, alexSignature)], PrecedingSignature: null)));

        Assert.NotEmpty(BoundDescriptorCodec.EncodeServerbound(V2Protocol, sent));
    }

    /// <summary>The offline control: with no certificates the send stays on the unsigned path on both eras, which is what an <c>enforce-secure-profile=false</c> server accepts and what the offline field tests exercised. Signing must not become mandatory as a side effect of becoming possible.</summary>
    [Theory]
    [InlineData(V1Protocol)]
    [InlineData(V2Protocol)]
    public async Task SendChat_WithoutCertificates_StaysUnsigned(int protocol)
    {
        var recorder = new RecordingSink();
        ChatActions chat = BuildChat(recorder, protocol, (send, ct) => send(null, ct));

        await chat.SendChatAsync("plain");

        var sent = Assert.IsType<ServerboundSignedChatPacket>(Assert.Single(recorder.Packets));
        Assert.Null(sent.Signature);
        Assert.Empty(sent.LegacyLastSeen);
        Assert.NotEmpty(BoundDescriptorCodec.EncodeServerbound(protocol, sent));
    }

    /// <summary>The coordinator is what decides which window exists, and it must give the v2 era the v2 collector, the v3 era the v3 tracker, and 1.19 neither. That per-era choice is the piece that was hardcoded to v3.</summary>
    /// <remarks>The 1.19 and 1.19.1 rows seed the login certificates, because that is how a real session on those eras gets a key: the login hello is their only announcement channel, so a coordinator built without a seed there deliberately refuses to acquire one and stays on the unsigned path (see <c>ProfileKeyRotationTests.PreV3Eras_WithNoLoginKey_NeverAcquireOne</c>). 1.19.3+ has <c>chat_session_update</c> and so resolves its own.</remarks>
    [Theory]
    [InlineData(ChatSignatureEra.V1_19, false, false)]
    [InlineData(ChatSignatureEra.V1_19_1, true, false)]
    [InlineData(ChatSignatureEra.V1_19_3, false, true)]
    public async Task Coordinator_InstallsTheWireLayoutWindow(ChatSignatureEra era, bool legacy, bool modern)
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certificates = Certificates(rsa);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, era, new StubProvider(certificates), TimeProvider.System, NullLogger.Instance,
            sink: new RecordingSink(), chatState: null,
            loginCertificates: era == ChatSignatureEra.V1_19_3 ? null : certificates);

        Assert.Equal(legacy, coordinator.LegacyCollector is not null);
        Assert.Equal(modern, coordinator.Tracker is not null);

        ChatSigningState? state = await coordinator.EnsureAsync(default);
        Assert.NotNull(state);
        Assert.Equal(legacy, state!.LegacyCollector is not null);
        Assert.Equal(modern, state.Tracker is not null);
    }

    private static byte[] Fill(byte value) => [.. Enumerable.Repeat(value, 256)];

    private static PlayerCertificates Certificates(RSA rsa) =>
        new(rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem(),
            Convert.ToBase64String("sig"u8.ToArray()), Convert.ToBase64String("sigv2"u8.ToArray()),
            DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);

    /// <summary>A fixed signing state for the send path. These tests never rotate, so the scope is a pass-through; the ordering guarantee it exists for is covered by <c>ProfileKeyRotationTests.V3_ARotationCannotOvertakeASendThatAlreadyResolved</c>.</summary>
    private static ChatSigningScope State(
        PlayerCertificates certificates, ChatSignatureEra era, LastSeenMessagesCollector? collector = null) =>
        (send, ct) => send(
            new ChatSigningState(new ChatSigningSession(Sender, SessionId), certificates, era)
            {
                LegacyCollector = collector,
            },
            ct);

    /// <summary>A minimal server tree with one signable argument: <c>msg &lt;targets&gt; &lt;message&gt;</c>.</summary>
    private static ServerCommandTree MsgCommandTree()
    {
        var wire = new CommandTreeData(
        [
            new CommandNodeData(CommandNodeKind.Root, 0x00, [1], -1, null, null),
            new CommandNodeData(CommandNodeKind.Literal, 0x01, [2], -1, "msg", null),
            new CommandNodeData(
                CommandNodeKind.Argument, 0x02, [3], -1, "targets",
                new CommandArgumentData(0, "minecraft:entity", ArgumentParserProperties.Empty, null)),
            new CommandNodeData(
                CommandNodeKind.Argument, 0x02 | 0x04, [], -1, "message",
                new CommandArgumentData(0, "minecraft:message", ArgumentParserProperties.Empty, null)),
        ], 0);

        // The node data names its argument type outright, so the registry only supplies the id table; any era's serves. This mirrors the existing v3 command-send fixture rather than inventing a second way to build the same tree.
        return ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5);
    }

    private static ChatActions BuildChat(
        RecordingSink sink,
        int protocol,
        ChatSigningScope signing,
        ServerCommandTree? tree = null)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var state = new ClientState(new ClientFeatures().Normalized());
        state.ServerCommands.Tree = tree;
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions { ChatCooldown = TimeSpan.Zero },
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };

        return new ChatActions(
            sink,
            services,
            new CommandCompletionService(sink),
            new CommandService<ClientCommandSource>(),
            () => null!,
            signing);
    }

    private sealed class StubProvider(PlayerCertificates certificates) : IChatSigningProvider
    {
        public ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<PlayerCertificates?>(certificates);
    }
}
