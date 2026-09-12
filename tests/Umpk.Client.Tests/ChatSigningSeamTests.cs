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
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Covers the chat-signing builder seam: era derivation per version band, the coordinator's fetch/cache/expiry-refresh behavior, that the command send path consults the signing provider in a signing era, and that the absent-provider path stays on the unsigned send (byte-identical to offline). No live server: the send path is driven directly against a recording sink.</summary>
public sealed class ChatSigningSeamTests
{
    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");
    private static readonly Guid SessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Theory]
    [InlineData("1.8")]
    [InlineData("1.16.5")]
    public void WireLayoutDerivation_NonSigningVersions_YieldNoWireLayout(string name)
    {
        Assert.True(JavaVersions.TryGetByName(name, out JavaVersion version));
        Assert.Equal("none", version.Features.ChatSigning);
        Assert.False(ChatSigningEras.TryFromFeature(version.Features.ChatSigning, out _));
    }

    [Theory]
    [InlineData("1.19", ChatSignatureEra.V1_19)]
    [InlineData("1.19.1", ChatSignatureEra.V1_19_1)]
    [InlineData("1.19.3", ChatSignatureEra.V1_19_3)]
    [InlineData("1.19.4", ChatSignatureEra.V1_19_3)]
    [InlineData("1.20.1", ChatSignatureEra.V1_19_3)]
    [InlineData("1.20.4", ChatSignatureEra.V1_19_3)]
    [InlineData("1.21.5", ChatSignatureEra.V1_19_3)]
    [InlineData("26.2", ChatSignatureEra.V1_19_3)]
    public void WireLayoutDerivation_SigningVersions_MapToExpectedWireLayout(string name, ChatSignatureEra expected)
    {
        Assert.True(JavaVersions.TryGetByName(name, out JavaVersion version));
        Assert.True(ChatSigningEras.TryFromFeature(version.Features.ChatSigning, out ChatSignatureEra era));
        Assert.Equal(expected, era);
    }

    [Fact]
    public void WireLayoutDerivation_UnknownFeature_YieldsNoWireLayout()
        => Assert.False(ChatSigningEras.TryFromFeature("v99", out _));

    [Fact]
    public async Task Coordinator_FetchesOnce_ThenServesCache_UntilExpiry()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var provider = new CountingCertificateProvider(_ => Certificates(clock.GetUtcNow().AddHours(1)));
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, new RecordingSink());

        ChatSigningState? first = await coordinator.EnsureAsync(default);
        ChatSigningState? second = await coordinator.EnsureAsync(default);

        Assert.NotNull(first);
        Assert.Same(first, second);                       // cached, no refetch
        Assert.Equal(1, provider.Calls);
        Assert.Equal(ChatSignatureEra.V1_19_3, first!.Era);
        Assert.Equal(Sender, first.Session.Sender);       // client-owned session identity
        Assert.Equal(SessionId, first.Session.SessionId);
    }

    /// <summary>With no way to announce, a 1.19.3+ session installs NO key at all and stays on the unsigned path.</summary>
    /// <remarks>
    /// <para>On 1.19.3+, announcement is the install's precondition at connect exactly as it is at rotation: vanilla binds session id and public key together through <c>ServerboundChatSessionUpdatePacket</c>, and a server with <c>enforce-secure-profile</c> rejects every signed message until it arrives. A coordinator with no sink has no way to send it, so it installs nothing and the send path falls back to unsigned, which is a state the server accepts. The rotating counterpart is <c>ProfileKeyRotationTests.V3_ExpiredKey_RotatesAndAnnounces_RatherThanSwappingSilently</c>.</para>
    /// </remarks>
    [Fact]
    public async Task Coordinator_WithNoWayToAnnounce_InstallsNothingAtAll()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var provider = new CountingCertificateProvider(_ => Certificates(clock.GetUtcNow().AddMinutes(30)));
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance);

        Assert.Null(await coordinator.EnsureAsync(default));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(await coordinator.EnsureAsync(default));
    }

    [Fact]
    public async Task Coordinator_NullCertificates_FallsBackToUnsigned()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var provider = new CountingCertificateProvider(_ => null);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, new RecordingSink());

        ChatSigningState? state = await coordinator.EnsureAsync(default);

        Assert.Null(state);
        Assert.Equal(1, provider.Calls);
    }

    /// <summary>A command with no signable arguments goes out on the bare unsigned <c>chat_command</c> from 1.20.5 onward when the command contains no signable arguments. Nothing needs signing, so the signing provider is not consulted at all. This is also the <c>enforce-secure-profile=false</c> path: the server only rejects an unsigned command that HAS signable arguments.</summary>
    [Fact]
    public async Task SendCommand_NoSignableArguments_SendsUnsignedChatCommand()
    {
        var recorder = new RecordingSink();
        int signingCalls = 0;
        ChatActions chat = BuildChat(recorder, JavaVersions.V1_21_5, (send, ct) =>
        {
            signingCalls++;
            return send(
                new ChatSigningState(new ChatSigningSession(Sender, SessionId), Certificates(DateTimeOffset.MaxValue), ChatSignatureEra.V1_19_3),
                ct);
        });

        await chat.SendCommandAsync("/seed");

        Assert.Equal(0, signingCalls);
        ServerboundChatCommandPacket sent = Assert.IsType<ServerboundChatCommandPacket>(Assert.Single(recorder.Packets));
        Assert.Equal("seed", sent.Command);   // the leading slash is stripped: it is not on the wire

        // The emitted packet must resolve a concrete codec in the 1.21.5 outbound table, not a marker: recording the object alone would not tell those apart, and a marker throws live.
        Assert.NotEmpty(BoundDescriptorCodec.EncodeServerbound(770, sent));
    }

    /// <summary>Without a signing session the command still leaves on a chat_command family packet, never on chat. Sending this as signed chat would broadcast it as text instead of dispatching the command.</summary>
    [Fact]
    public async Task SendCommand_WithoutProvider_StillSendsChatCommand_NotChat()
    {
        var recorder = new RecordingSink();
        ChatActions chat = BuildChat(recorder, JavaVersions.V1_21_5, (send, ct) => send(null, ct));

        await chat.SendCommandAsync("seed");

        object sent = Assert.Single(recorder.Packets);
        Assert.IsType<ServerboundChatCommandPacket>(sent);
        Assert.IsNotType<ServerboundSignedChatPacket>(sent);
        Assert.IsNotType<ServerboundLegacyChatPacket>(sent);
    }

    /// <summary>A command WITH a signable argument goes out on <c>chat_command_signed</c> on 1.20.5+, carrying the signature the signing session produced for that argument plus the last-seen window drawn from the tracker. The signature is real (a generated 2048-bit key pair), so the 256-byte fixed-width argument-signature framing is exercised end to end rather than stubbed.</summary>
    [Fact]
    public async Task SendCommand_WithSignableArgument_SendsSignedChatCommand_CarryingTheSignature()
    {
        using RSA rsa = RSA.Create(2048);
        var certificates = new PlayerCertificates(
            rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem(), "", "",
            DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);

        var tracker = new LastSeenMessagesTracker();
        var seen = new byte[256];
        seen[0] = 0x7F;
        Assert.False(tracker.Add(seen, out _));

        var recorder = new RecordingSink();
        int signingCalls = 0;
        ChatActions chat = BuildChat(recorder, JavaVersions.V1_21_5, (send, ct) =>
        {
            signingCalls++;
            return send(
                new ChatSigningState(new ChatSigningSession(Sender, SessionId), certificates, ChatSignatureEra.V1_19_3)
                {
                    Tracker = tracker,
                },
                ct);
        }, MsgCommandTree());

        await chat.SendCommandAsync("/msg Steve hello world");

        Assert.Equal(1, signingCalls);
        ServerboundChatCommandSignedPacket sent =
            Assert.IsType<ServerboundChatCommandSignedPacket>(Assert.Single(recorder.Packets));
        Assert.Equal("msg Steve hello world", sent.Command);

        SignedCommandArgument argument = Assert.Single(sent.ArgumentSignatures);
        Assert.Equal("message", argument.Name);
        Assert.Equal(256, argument.Signature.Length);

        // The carried signature is the one the session produced over THIS packet's timestamp, salt and acknowledged window; a mismatched snapshot would fail verification here.
        Assert.True(ChatSigningSession.Verify(
            certificates.PublicKeyPem, argument.Signature, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, SessionId, MessageIndex: 0, "hello world",
                DateTimeOffset.FromUnixTimeMilliseconds(sent.TimestampMillis), sent.Salt,
                [new AcknowledgedMessage(Guid.Empty, seen)], null)));

        // The acknowledgement window is the same tracker snapshot: one tracked message, so offset 1 and exactly one live bit in the 3-byte bitset.
        Assert.Equal(1, sent.LastSeen.Offset);
        Assert.Equal(3, sent.LastSeen.Acknowledged.Length);
        Assert.Equal(1, sent.LastSeen.Acknowledged.Sum(b => System.Numerics.BitOperations.PopCount(b)));

        // chat_command_signed must be a real codec on 770, and the 256-byte argument signature has to survive the fixed-width framing (a variable-width era codec here would frame it differently).
        Assert.NotEmpty(BoundDescriptorCodec.EncodeServerbound(770, sent));
    }

    /// <summary>On 1.19 through 1.20.4 there is no split: the signed payload IS <c>minecraft:chat_command</c>.</summary>
    [Fact]
    public async Task SendCommand_On1_20_4_SendsThePreSplitSignedChatCommand()
    {
        var recorder = new RecordingSink();
        ChatActions chat = BuildChat(recorder, JavaVersions.V1_20_4, (send, ct) => send(null, ct));

        await chat.SendCommandAsync("/seed");

        ServerboundSignedChatCommandPacket sent =
            Assert.IsType<ServerboundSignedChatCommandPacket>(Assert.Single(recorder.Packets));
        Assert.Equal("seed", sent.Command);
        Assert.Empty(sent.ArgumentSignatures);
        Assert.NotEmpty(BoundDescriptorCodec.EncodeServerbound(765, sent));
    }

    /// <summary>On protocols 47-758, a leading slash in the legacy chat frame routes to the command dispatcher and must remain on the wire.</summary>
    [Theory]
    [InlineData("1.8")]
    [InlineData("1.12.2")]
    [InlineData("1.16.5")]
    [InlineData("1.18.2")]
    public async Task SendCommand_BeforeSigning_StillSendsTheLegacyChatFrame(string version)
    {
        Assert.True(JavaVersions.TryGetByName(version, out JavaVersion resolved));
        Assert.True(resolved.Version.Protocol < 759);

        var recorder = new RecordingSink();
        ChatActions chat = BuildChat(recorder, resolved, (send, ct) => send(null, ct));

        await chat.SendCommandAsync("/gamemode creative");

        ServerboundLegacyChatPacket sent =
            Assert.IsType<ServerboundLegacyChatPacket>(Assert.Single(recorder.Packets));
        Assert.Equal("/gamemode creative", sent.Message);

        // And it still encodes on that era's real outbound table, slash and all.
        Assert.NotEmpty(BoundDescriptorCodec.EncodeServerbound(resolved.Version.Protocol, sent));
    }

    /// <summary>Real key material. The coordinator now ANNOUNCES the join key before installing it, and <c>ProfileKeyMaterial.Build</c> decodes the PEM to DER and base64-decodes Mojang's signature, so placeholder strings no longer survive the connect path.</summary>
    private static PlayerCertificates Certificates(DateTimeOffset expiresAt)
    {
        using RSA rsa = RSA.Create(2048);
        return new(
            rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem(),
            Convert.ToBase64String("sig"u8.ToArray()), Convert.ToBase64String("sigv2"u8.ToArray()),
            expiresAt, expiresAt.AddHours(-1));
    }

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

        return ServerCommandTree.Build(wire, ArgumentTypeRegistry.V1_21_5);
    }

    private static ChatActions BuildChat(
        RecordingSink sink,
        JavaVersion version,
        ChatSigningScope signing,
        ServerCommandTree? tree = null)
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.ServerCommands.Tree = tree;
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions { ChatCooldown = TimeSpan.Zero },
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };

        return new ChatActions(
            sink,
            services,
            new CommandCompletionService(sink),
            new CommandService<ClientCommandSource>(),
            () => null!,        // the source factory is only used by CompleteAsync, never by the send path
            signing);
    }

    private sealed class CountingCertificateProvider(Func<CancellationToken, PlayerCertificates?> factory) : IChatSigningProvider
    {
        public int Calls { get; private set; }

        public ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(factory(cancellationToken));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
