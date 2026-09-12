using System.Security.Cryptography;
using System.Text;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.TestKit.Server;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>Per-protocol binding pins for the whole login family: for every supported protocol number, what the registrar ACTUALLY resolves and what that codec puts on the wire.</summary>
/// <remarks>
/// <para>The login timeline must select a codec whose fields exactly match each protocol's wire layout.</para>
/// <para>A test naming a codec directly does not verify the timeline that chooses between codecs. Every assertion here therefore goes through <see cref="BoundCodec.LoginAt"/>, which asks the registrar what it binds at a protocol number, and runs frames through <see cref="BoundPacketCodec"/>, the same dispatcher entry point, so a byte too few or too many raises the same fault a live session sees.</para>
/// <para>The login handshake is only partly exercised offline. An offline-mode server never sends an encryption request at all, so no offline test, and no packet corpus captured against one, can reach the <c>hello</c> codec. The frames below are therefore realistic rather than minimal: a full-length RSA key and a real verify token, so the field after any truncation point is actually read.</para>
/// <para>The registration fixtures cannot cover this class either: they record codec-versus-marker, not codec identity, so swapping one era codec for another leaves them byte-identical.</para>
/// </remarks>
public class LoginCodecBindingTests
{
    /// <summary>The supported protocol numbers. Listed rather than read from the version catalog because this assembly deliberately does not reference the dataset package; the sibling conformance suite walks the real catalog and would fail first if a version were added without landing here.</summary>
    private static readonly int[] All =
    [
        47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404,
        477, 480, 485, 490, 498, 573, 575, 578,
        735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763,
        764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776,
    ];

    /// <summary>The literal table's protocol column, read by <c>AllProtocolTableCoverageTests</c>.</summary>
    public static IReadOnlyList<int> Protocols() => All;

    /// <summary>Every supported protocol number, in order.</summary>
    public static TheoryData<int> AllProtocols => Subset(static _ => true);

    /// <summary>Protocols carrying the login plugin request/answer round trip (1.13+).</summary>
    public static TheoryData<int> CustomQueryProtocols => Subset(static p => p >= 393);

    /// <summary>Protocols predating the login plugin round trip (1.8-1.12.2).</summary>
    public static TheoryData<int> PreCustomQueryProtocols => Subset(static p => p < 393);

    /// <summary>Protocols spelling the login plugin answer <c>custom_query</c> (1.13-1.20.1).</summary>
    public static TheoryData<int> LegacyAnswerNameProtocols => Subset(static p => p is >= 393 and <= 763);

    // Realistic encryption-request material

    /// <summary>A server id at the vanilla maximum of 20 characters. Vanilla sends the empty string in practice, but the field is a UTF-8 string capped at 20 characters, and a full-width value keeps the length arithmetic honest.</summary>
    private const string ServerId = "0123456789abcdefghij";

    /// <summary>A real-length DER <c>SubjectPublicKeyInfo</c> for a 1024-bit RSA key: 162 bytes. The content is filler; only the length matters, and it matters a lot, because 162 crosses the single-byte VarInt boundary.</summary>
    private static byte[] PublicKey { get; } = Filled(162, 0x30);

    /// <summary>The 4-byte verify token / challenge vanilla generates for every encryption request.</summary>
    private static byte[] VerifyToken { get; } = [0xDE, 0xAD, 0xBE, 0xEF];

    /// <summary>The encoded size of an encryption request without the authenticate flag: VarInt(20) + 20 server id bytes, VarInt(162) + 162 key bytes, VarInt(4) + 4 token bytes. 162 needs a two-byte VarInt.</summary>
    private const int HelloSizeWithoutFlag = (1 + 20) + (2 + 162) + (1 + 4);

    /// <summary>The 1.20.5+ encryption request: the same fields plus one boolean byte.</summary>
    private const int HelloSizeWithFlag = HelloSizeWithoutFlag + 1;

    /// <summary>The kick reason under test carries a click event and a hover event, so the encoded bytes actually differ between the component dialects. A plain-text reason encodes identically under both, which is exactly why a dialect misbinding survives ordinary round-trip tests.</summary>
    private static Component KickReason { get; } = new(
        new TextContent("Banned. Appeal here."),
        new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://example.invalid/appeal"),
            HoverEvent = new HoverShowText(Component.Text("opens the appeal form")),
        });

    // clientbound hello (encryption request)

    /// <summary>The authenticate flag exists only from 1.20.5 (protocol 766). Earlier protocols carry three fields, while protocol 766 and later carry four; using the four-field form earlier reads past the end of the encryption request.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void ServerHello_AuthenticateFlagBoundary_IsProtocol766(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:hello");
        byte[] frame = bound.Encode(new ClientboundHelloPacket(ServerId, PublicKey, VerifyToken, ShouldAuthenticate: true));

        Assert.Equal(protocol >= 766 ? HelloSizeWithFlag : HelloSizeWithoutFlag, frame.Length);
    }

    /// <summary>The resolved codec must read back exactly the frame it wrote, through the live decode entry point that enforces frame-exactness. Any era whose field count disagrees with the bound codec raises here rather than silently returning a mangled packet.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void ServerHello_RoundTripsARealisticEncryptionRequest_OnEveryProtocol(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:hello");
        byte[] frame = bound.Encode(new ClientboundHelloPacket(ServerId, PublicKey, VerifyToken, ShouldAuthenticate: true));

        var decoded = Assert.IsType<ClientboundHelloPacket>(bound.DecodeFrame(frame));
        Assert.Equal(ServerId, decoded.ServerId);
        Assert.Equal(PublicKey, decoded.PublicKey);
        Assert.Equal(VerifyToken, decoded.VerifyToken);
        Assert.True(decoded.ShouldAuthenticate);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    /// <summary>The 1.20.5 era carries the flag as a real field, so a false value survives the round trip. On older eras omit the flag and therefore decode with authentication enabled.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void ServerHello_CarriesTheFlagOnlyWhereVanillaHasIt(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:hello");
        byte[] frame = bound.Encode(new ClientboundHelloPacket(ServerId, PublicKey, VerifyToken, ShouldAuthenticate: false));
        var decoded = Assert.IsType<ClientboundHelloPacket>(bound.DecodeFrame(frame));

        if (protocol >= 766)
        {
            Assert.Equal(0x00, frame[^1]);
            Assert.False(decoded.ShouldAuthenticate);
        }
        else
            Assert.True(decoded.ShouldAuthenticate);

    }

    /// <summary>Cross-era rejection, first direction: the pre-1.20.5 form must REFUSE a frame carrying the authenticate flag rather than accept it and drop a byte. The bound decoder is frame-exact, so the extra byte surfaces as a protocol violation naming the packet.</summary>
    [Fact]
    public void PreAuthenticateWireLayout_RejectsAFrameCarryingTheFlag()
    {
        byte[] modern = BoundCodec.LoginAt(766, PacketFlow.Clientbound, "minecraft:hello")
            .Encode(new ClientboundHelloPacket(ServerId, PublicKey, VerifyToken, ShouldAuthenticate: true));
        Assert.Equal(HelloSizeWithFlag, modern.Length);

        BoundPacketCodec legacy = BoundCodec.LoginAt(765, PacketFlow.Clientbound, "minecraft:hello");
        var ex = Assert.Throws<ProtocolViolationException>(() => legacy.DecodeFrame(modern));
        Assert.Equal(1, ex.RemainingBytes);
    }

    /// <summary>Cross-era rejection in the other direction: the 1.20.5 form fed a pre-1.20.5 encryption request runs off the end of the payload.</summary>
    [Fact]
    public void AuthenticateWireLayout_RejectsAFrameWithoutTheFlag()
    {
        byte[] legacy = BoundCodec.LoginAt(765, PacketFlow.Clientbound, "minecraft:hello")
            .Encode(new ClientboundHelloPacket(ServerId, PublicKey, VerifyToken, ShouldAuthenticate: true));
        Assert.Equal(HelloSizeWithoutFlag, legacy.Length);

        BoundPacketCodec modern = BoundCodec.LoginAt(766, PacketFlow.Clientbound, "minecraft:hello");
        var ex = Assert.Throws<ProtocolViolationException>(() => modern.DecodeFrame(legacy));
        Assert.Contains("minecraft:hello", ex.Message, StringComparison.Ordinal);
    }

    // clientbound login_disconnect

    /// <summary>The login disconnect reason is a length-prefixed JSON string on EVERY supported version. It never took the network-NBT transport the play and configuration disconnects took in 1.20.3, because the login phase has no registry access. A leading TAG_Compound byte here would mean the login kick reason is unreadable on the affected band.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void LoginDisconnect_IsAlwaysAJsonString(int protocol)
    {
        byte[] frame = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_disconnect")
            .Encode(new ClientboundLoginDisconnectPacket(KickReason));

        Assert.NotEqual(0x0A, frame[0]);
        Assert.Equal((byte)'{', frame[SkipVarInt(frame)]);
    }

    /// <summary>The only thing that moves in the login disconnect is the component dialect, and it moves at 1.21.5 (protocol 770) with the rest of the component surface, not at 1.19. Protocols 759-769 require <c>clickEvent</c>; protocol 770 and later require <c>click_event</c>.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void LoginDisconnect_DialectBoundary_IsProtocol770(int protocol)
    {
        byte[] frame = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_disconnect")
            .Encode(new ClientboundLoginDisconnectPacket(KickReason));

        if (protocol < 770)
        {
            Assert.True(Contains(frame, "clickEvent"), $"login disconnect @ {protocol} must use clickEvent.");
            Assert.True(Contains(frame, "hoverEvent"), $"login disconnect @ {protocol} must use hoverEvent.");
            Assert.False(Contains(frame, "click_event"), $"login disconnect @ {protocol} must not use click_event.");
        }
        else
        {
            Assert.True(Contains(frame, "click_event"), $"login disconnect @ {protocol} must use click_event.");
            Assert.True(Contains(frame, "hover_event"), $"login disconnect @ {protocol} must use hover_event.");
            Assert.False(Contains(frame, "clickEvent"), $"login disconnect @ {protocol} must not use clickEvent.");
        }
    }

    /// <summary>The login disconnect and the play disconnect share the same component dialect, so their dialects must flip on the same protocol even though their transports do not (play moves to NBT at 765, login never does).</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void LoginDisconnect_DialectAgreesWithThePlayDisconnect(int protocol)
    {
        byte[] login = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_disconnect")
            .Encode(new ClientboundLoginDisconnectPacket(KickReason));
        byte[] play = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:disconnect")
            .Encode(new ClientboundDisconnectPacket(KickReason));

        Assert.Equal(Contains(play, "click_event"), Contains(login, "click_event"));
        Assert.Equal(Contains(play, "clickEvent"), Contains(login, "clickEvent"));
    }

    /// <summary>A SECOND axis moves on the same field, one release earlier: a pure-literal reason is spelled <c>{"text":"..."}</c> through 764 and collapses to a bare JSON string from 765. This is the ninth and last wire call site that was still writing the collapsed form on every protocol.</summary>
    /// <remarks>
    /// <para>Protocols through 764 serialize the literal component <c>bare</c> as <c>{"text":"bare"}</c>. Protocol 765 and later serialize it as <c>"bare"</c>.</para>
    /// <para>Asserted as a WIDTH, because that is what separates the two: 15 JSON bytes against 6, plus the one-byte VarInt length prefix. A round trip cannot see this at all, which is exactly why it survived: the peer reads a bare string as a literal component on every version, and so does this one, so both spellings decode identically on both sides of the boundary. That also makes a cross-era REJECTION impossible to write honestly here - there is nothing to reject - so the value-identity half is asserted instead, below.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void LoginDisconnect_LiteralFormBoundary_IsProtocol765(int protocol)
    {
        byte[] frame = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_disconnect")
            .Encode(new ClientboundLoginDisconnectPacket(Component.Text("bare")));

        string json = Encoding.UTF8.GetString(frame, SkipVarInt(frame), frame.Length - SkipVarInt(frame));

        if (protocol < 765)
        {
            Assert.Equal("{\"text\":\"bare\"}", json);
            Assert.Equal(1 + 15, frame.Length);
        }
        else
        {
            Assert.Equal("\"bare\"", json);
            Assert.Equal(1 + 6, frame.Length);
        }
    }

    /// <summary>The two spellings are byte-distinct and value-identical, and every protocol reads BOTH. Dropping the collapse from the 47-764 writer must not narrow the reader there, because a non-vanilla server on an old protocol may still send the collapsed form.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void LoginDisconnect_ReadsBothLiteralSpellings(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_disconnect");
        byte[] objectForm = JsonFrame("{\"text\":\"bare\"}");
        byte[] collapsed = JsonFrame("\"bare\"");

        Assert.NotEqual(objectForm.Length, collapsed.Length);
        Assert.Equal(Component.Text("bare"), ((ClientboundLoginDisconnectPacket)bound.DecodeFrame(objectForm)).Reason);
        Assert.Equal(Component.Text("bare"), ((ClientboundLoginDisconnectPacket)bound.DecodeFrame(collapsed)).Reason);
    }

    /// <summary>A styled reason takes the pre-1.20.3 GSON KEY ORDER on 47-764 as well, since the collapse and the order are two effects of the same 1.20.3 serializer swap. This test pins the exact output for literal text <c>h</c> styled with insertion <c>zz</c>, <c>run_command</c>, and <c>show_text</c>.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void LoginDisconnect_StyledReason_TakesTheWireLayoutKeyOrder(int protocol)
    {
        var reason = new Component(
            new TextContent("h"),
            new Style
            {
                Insertion = "zz",
                ClickEvent = new ClickEvent(ClickEventAction.RunCommand, "/say hi"),
                HoverEvent = new HoverShowText(Component.Text("tip")),
            });

        byte[] frame = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_disconnect")
            .Encode(new ClientboundLoginDisconnectPacket(reason));
        string json = Encoding.UTF8.GetString(frame, SkipVarInt(frame), frame.Length - SkipVarInt(frame));

        if (protocol < 765)
            Assert.Equal(
                "{\"insertion\":\"zz\",\"clickEvent\":{\"action\":\"run_command\",\"value\":\"/say hi\"},"
                + "\"hoverEvent\":{\"action\":\"show_text\",\"contents\":{\"text\":\"tip\"}},\"text\":\"h\"}",
                json);

        else
        {
            // 1.20.3+ is key-complete and value-exact but deliberately not byte-ordered against vanilla;
            // see the note in ComponentJson for the DFU-nesting evidence.
            Assert.StartsWith("{\"text\":\"h\"", json, StringComparison.Ordinal);
        }
    }

    /// <summary>Wraps a JSON string in the VarInt length prefix the login disconnect frame carries.</summary>
    private static byte[] JsonFrame(string json)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        Assert.InRange(utf8.Length, 0, 127);
        return [(byte)utf8.Length, .. utf8];
    }

    /// <summary>The login driver decodes the kick frame and keeps the <see cref="Component"/>, not only the flattened string it also builds for <see cref="Exception.Message"/>. Driven end to end through <see cref="JavaClientLogin.LoginAsync"/> over a real <see cref="JavaConnection"/> against a <see cref="FakeJavaServer"/>, with a minimal descriptor that registers only the three packets this path touches (handshake, login-start, login-disconnect) so it exercises the real timeline-resolved codec rather than a hand-decoded frame.</summary>
    [Fact]
    public async Task LoginKick_CarriesTheDecodedReasonComponent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken ct = cts.Token;

        const int protocol = 770;
        var builder = new ProtocolDescriptorBuilder(new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Handshake, PacketFlow.Serverbound, 0, "minecraft:intention");
        PacketRegistrar.Register(builder, ProtocolPhase.Login, PacketFlow.Serverbound, 0, "minecraft:hello");
        PacketRegistrar.Register(builder, ProtocolPhase.Login, PacketFlow.Clientbound, 0, "minecraft:login_disconnect");
        ProtocolDescriptor descriptor = builder.Build();
        var version = new JavaVersion(new GameVersion(GameEdition.Java, "test", protocol), descriptor, new ProtocolFeatures());

        await using FakeJavaServer server = FakeJavaServer.Create();
        var client = new JavaConnection(server.ClientPipe, new JavaConnectionOptions { ReadIdleTimeout = TimeSpan.Zero });
        client.BindCodec(new DescriptorFrameCodecBinding(descriptor), PacketFlow.Clientbound);
        client.Start();

        await using (client)
        {
            Task<LoginResult> loginTask = JavaClientLogin.LoginAsync(client, version, new JavaLoginOptions
            {
                Username = "Tester",
                ServerHost = "localhost",
                ServerPort = 25565,
            }, ct);

            await server.NextFrameAsync(ct); // handshake
            server.ServerConnection.SetPhase(ProtocolPhase.Login);
            await server.NextFrameAsync(ct); // hello

            byte[] frame = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_disconnect")
                .Encode(new ClientboundLoginDisconnectPacket(Component.Text("Banned")));
            await server.SendFrameAsync(0, frame, ct);

            ConnectionClosedException ex = await Assert.ThrowsAsync<ConnectionClosedException>(() => loginTask);
            Assert.Equal(CloseReason.DisconnectMessage, ex.Reason);
            Assert.NotNull(ex.Disconnect);
            Assert.Equal("Banned", ex.Disconnect!.ToPlainText());
            Assert.Contains("Banned", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task LoginFinished_ManualDecodeFailure_IsReportedOnceWithoutPayloadEvidence()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken ct = cts.Token;

        const int protocol = 770;
        ProtocolDescriptorBuilder builder = LoginDriverDescriptor(protocol);
        PacketRegistrar.Register(builder, ProtocolPhase.Login, PacketFlow.Clientbound, 2, "minecraft:login_finished");
        ProtocolDescriptor descriptor = builder.Build();
        var version = new JavaVersion(
            new GameVersion(GameEdition.Java, "test", protocol), descriptor, new ProtocolFeatures());

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using var client = new JavaConnection(
            server.ClientPipe, new JavaConnectionOptions { ReadIdleTimeout = TimeSpan.Zero });
        client.BindCodec(new DescriptorFrameCodecBinding(descriptor), PacketFlow.Clientbound);
        client.SetDecodeFilter(PacketDecodeFilter.None);

        int calls = 0;
        PacketDecodeFailure? reported = null;
        client.PacketDecodeFailed += failure =>
        {
            Interlocked.Increment(ref calls);
            reported = failure;
        };
        client.Start();

        Task<LoginResult> loginTask = JavaClientLogin.LoginAsync(client, version, LoginOptions(), ct);
        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello

        await server.SendFrameAsync(2, [], ct);

        await Assert.ThrowsAsync<ProtocolViolationException>(() => loginTask);
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.NotNull(reported);
        Assert.Equal(protocol, reported.Protocol);
        Assert.Equal(ProtocolPhase.Login, reported.Phase);
        Assert.Equal(Identifier.Minecraft("login_finished"), reported.PacketId);
        Assert.Equal("LoginCodecs.FinishedV1_19", reported.CodecIdentity);
        Assert.Equal(0, reported.PayloadLength);
        Assert.Empty(reported.Evidence.ToArray());
        Assert.False(reported.EvidenceTruncated);
    }

    [Fact]
    public async Task ConfigurationPacket_ManualDecodeFailure_IsReportedOnceWithAttribution()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken ct = cts.Token;

        const int protocol = 770;
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures { ConfigurationPhase = true });
        PacketRegistrar.Register(
            builder, ProtocolPhase.Configuration, PacketFlow.Clientbound, 3, "minecraft:select_known_packs");
        ProtocolDescriptor descriptor = builder.Build();
        var version = new JavaVersion(
            new GameVersion(GameEdition.Java, "test", protocol), descriptor,
            new ProtocolFeatures { ConfigurationPhase = true });

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using var client = new JavaConnection(
            server.ClientPipe, new JavaConnectionOptions { ReadIdleTimeout = TimeSpan.Zero });
        client.BindCodec(new DescriptorFrameCodecBinding(descriptor), PacketFlow.Clientbound);
        client.SetDecodeFilter(PacketDecodeFilter.None);
        client.SetPhase(ProtocolPhase.Configuration);

        int calls = 0;
        PacketDecodeFailure? reported = null;
        client.PacketDecodeFailed += failure =>
        {
            Interlocked.Increment(ref calls);
            reported = failure;
        };
        client.Start();

        Task configuration = JavaClientLogin.RunConfigurationPhaseAsync(
            client, version, new JavaConfigurationOptions(), ct);
        await server.SendFrameAsync(3, [0x80], ct); // truncated pack-count VarInt

        await Assert.ThrowsAsync<ProtocolViolationException>(() => configuration);
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.NotNull(reported);
        Assert.Equal(protocol, reported.Protocol);
        Assert.Equal(ProtocolPhase.Configuration, reported.Phase);
        Assert.Equal(Identifier.Minecraft("select_known_packs"), reported.PacketId);
        Assert.Equal("ConfigurationCodecs.SelectPacksClient", reported.CodecIdentity);
        Assert.Equal(new byte[] { 0x80 }, reported.Evidence.ToArray());
        Assert.False(reported.EvidenceTruncated);
    }

    [Fact]
    public async Task MalformedLoginKickReason_UsesFallbackWithoutFatalDecodeReport()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken ct = cts.Token;

        const int protocol = 770;
        ProtocolDescriptorBuilder builder = LoginDriverDescriptor(protocol);
        PacketRegistrar.Register(builder, ProtocolPhase.Login, PacketFlow.Clientbound, 0, "minecraft:login_disconnect");
        ProtocolDescriptor descriptor = builder.Build();
        var version = new JavaVersion(
            new GameVersion(GameEdition.Java, "test", protocol), descriptor, new ProtocolFeatures());

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using var client = new JavaConnection(
            server.ClientPipe, new JavaConnectionOptions { ReadIdleTimeout = TimeSpan.Zero });
        client.BindCodec(new DescriptorFrameCodecBinding(descriptor), PacketFlow.Clientbound);
        client.SetDecodeFilter(PacketDecodeFilter.None);

        int failures = 0;
        client.PacketDecodeFailed += _ => Interlocked.Increment(ref failures);
        client.Start();

        Task<LoginResult> loginTask = JavaClientLogin.LoginAsync(client, version, LoginOptions(), ct);
        await server.NextFrameAsync(ct);
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct);
        await server.SendFrameAsync(0, [0x80], ct);

        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(() => loginTask);
        Assert.Equal(CloseReason.DisconnectMessage, closed.Reason);
        Assert.Contains("while decoding the reason", closed.Message, StringComparison.Ordinal);
        Assert.Equal(0, Volatile.Read(ref failures));
    }

    // clientbound login_finished

    /// <summary>Login success moved four times: 1.16 swapped the dashed-string UUID for a raw 16-byte one, 1.19 added the profile property array, 1.20.5 added a strict-error-handling boolean that 1.21.2 removed again, and 26.2 appended a session id. Sizes are exact, so an off-by-one era shows up as a length mismatch rather than as a plausible-looking packet.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void LoginFinished_ShapeMatchesItsWireLayout(int protocol)
    {
        var uuid = new Guid("12345678-90ab-cdef-1234-567890abcdef");
        var session = new Guid("fedcba09-8765-4321-fedc-ba0987654321");
        GameProfileProperty[] properties = [new("textures", "eyJ0aW1lc3RhbXAiOjB9", "sig")];

        BoundPacketCodec bound = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_finished");
        byte[] frame = bound.Encode(
            new ClientboundLoginFinishedPacket(uuid, "Notch", properties, session) { StrictErrorHandling = true });

        // VarInt(36) + the dashed uuid text, or the raw 16 bytes; then VarInt(5) + "Notch".
        int uuidSize = protocol < 735 ? 1 + 36 : 16;
        int nameSize = 1 + 5;

        // VarInt count, then per property VarInt+name, VarInt+value, present flag, VarInt+signature.
        bool hasProperties = protocol >= 759;
        int propertiesSize = hasProperties ? 1 + (1 + 8) + (1 + 20) + 1 + (1 + 3) : 0;
        int strictSize = protocol is >= 766 and <= 767 ? 1 : 0;
        int sessionSize = protocol >= 776 ? 16 : 0;

        Assert.Equal(uuidSize + nameSize + propertiesSize + strictSize + sessionSize, frame.Length);

        var decoded = Assert.IsType<ClientboundLoginFinishedPacket>(bound.DecodeFrame(frame));
        Assert.Equal(uuid, decoded.Uuid);
        Assert.Equal("Notch", decoded.Username);
        Assert.Equal(hasProperties ? 1 : 0, decoded.Properties.Count);
        Assert.Equal(protocol >= 776 ? session : null, decoded.SessionId);
        Assert.Equal(strictSize == 1, decoded.StrictErrorHandling);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    // serverbound hello (login start)

    /// <summary>Login start is name-only through 1.18.2; 1.19 appends an optional profile public key, 1.19.1 adds an optional uuid beside it, 1.19.3 drops the key again, and 1.20.2 settles on a non-optional uuid. The send path is the offline/unsigned one, so both optionals are written absent.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void ClientHello_ShapeMatchesItsWireLayout(int protocol)
    {
        var profileId = new Guid("12345678-90ab-cdef-1234-567890abcdef");
        BoundPacketCodec bound = BoundCodec.LoginAt(protocol, PacketFlow.Serverbound, "minecraft:hello");
        byte[] frame = bound.Encode(new ServerboundHelloPacket("Notch", profileId));

        int nameSize = 1 + 5;
        int keyFlagSize = protocol is 759 or 760 ? 1 : 0;
        int idSize = protocol switch
        {
            < 759 => 0,
            759 => 0,
            >= 760 and <= 763 => 1 + 16,
            _ => 16,
        };

        Assert.Equal(nameSize + keyFlagSize + idSize, frame.Length);

        var decoded = Assert.IsType<ServerboundHelloPacket>(bound.DecodeFrame(frame));
        Assert.Equal("Notch", decoded.Username);
        Assert.Equal(protocol >= 760 ? profileId : null, decoded.ProfileId);
        Assert.Null(decoded.ProfileKey);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    // login plugin request / answer

    /// <summary>The login plugin round trip is how every proxy that uses modern forwarding (Velocity, BungeeCord) completes a login. Both request and answer must resolve from protocol 393; the answer uses its legacy identifier through 1.20.1 and its renamed identifier from 1.20.2.</summary>
    [Theory]
    [MemberData(nameof(CustomQueryProtocols))]
    public void CustomQuery_RoundTripsInBothDirections_FromProtocol393(int protocol)
    {
        var channel = Identifier.Parse("velocity:player_info");
        byte[] payload = [0x01, 0x02, 0x03, 0x04];

        BoundPacketCodec request = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:custom_query");
        byte[] requestFrame = request.Encode(new ClientboundLoginCustomQueryPacket(42, channel, payload));
        var decodedRequest = Assert.IsType<ClientboundLoginCustomQueryPacket>(request.DecodeFrame(requestFrame));
        Assert.Equal(42, decodedRequest.TransactionId);
        Assert.Equal(channel, decodedRequest.Channel);
        Assert.Equal(payload, decodedRequest.Data);

        BoundPacketCodec answer = BoundCodec.LoginAt(
            protocol, PacketFlow.Serverbound, protocol >= 764 ? "minecraft:custom_query_answer" : "minecraft:custom_query");
        byte[] answerFrame = answer.Encode(new ServerboundLoginCustomQueryAnswerPacket(42, payload));
        var decodedAnswer = Assert.IsType<ServerboundLoginCustomQueryAnswerPacket>(answer.DecodeFrame(answerFrame));
        Assert.Equal(42, decodedAnswer.TransactionId);
        Assert.Equal(payload, decodedAnswer.Data);
    }

    /// <summary>1.20.2 renamed the answer packet without touching its wire, so the legacy name must resolve the same bytes the modern name does. Both spellings are checked against a fixed frame rather than against each other alone, so a shared regression cannot hide.</summary>
    [Theory]
    [MemberData(nameof(LegacyAnswerNameProtocols))]
    public void CustomQueryAnswer_LegacyName_ResolvesTheModernWire(int protocol)
    {
        byte[] expected = [0x2A, 0x01, 0x07];

        BoundPacketCodec legacy = BoundCodec.LoginAt(protocol, PacketFlow.Serverbound, "minecraft:custom_query");
        Assert.Equal(expected, legacy.Encode(new ServerboundLoginCustomQueryAnswerPacket(42, [0x07])));

        BoundPacketCodec modern = BoundCodec.LoginAt(764, PacketFlow.Serverbound, "minecraft:custom_query_answer");
        Assert.Equal(expected, modern.Encode(new ServerboundLoginCustomQueryAnswerPacket(42, [0x07])));

        // An unanswered channel is a bare false flag, which is vanilla's default answer.
        Assert.Equal([0x2A, 0x00], legacy.Encode(new ServerboundLoginCustomQueryAnswerPacket(42, null)));
    }

    /// <summary>The lower edge. Vanilla added the login plugin round trip in 1.13, so on 1.8-1.12.2 it must stay an honest marker rather than resolve a codec for a packet the version does not have.</summary>
    [Theory]
    [MemberData(nameof(PreCustomQueryProtocols))]
    public void CustomQuery_IsNotBound_BeforeProtocol393(int protocol)
    {
        Assert.False(BoundCodec.LoginEntryAt(protocol, PacketFlow.Clientbound, "minecraft:custom_query").IsImplemented);
        Assert.False(BoundCodec.LoginEntryAt(protocol, PacketFlow.Serverbound, "minecraft:custom_query").IsImplemented);
    }

    // the rest of the family, and the era-free members

    /// <summary>Set-compression has one wire shape across every version that carries it, so it must resolve a codec everywhere rather than only where somebody happened to test. The encryption response has that SAME plain shape everywhere EXCEPT the two 1.19 signing eras. On 759/760 it has an unconditional leading discriminator boolean, then either the plain verify token (true/"left") or a salt+signature pair (false/"right") regardless of whether the login-start carried a profile key. Omitting the discriminator desynchronizes every later byte and closes the connection. Protocol 761 and later revert to the plain shape because the profile key moved out of login-start into chat_session_update at exactly that boundary, so the server can never hold one at KEY time again.</summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void CompressionAndKey_AreBound_OnEveryProtocol(int protocol)
    {
        BoundPacketCodec compression = BoundCodec.LoginAt(protocol, PacketFlow.Clientbound, "minecraft:login_compression");
        Assert.Equal([0x80, 0x02], compression.Encode(new ClientboundLoginCompressionPacket(256)));

        BoundPacketCodec key = BoundCodec.LoginAt(protocol, PacketFlow.Serverbound, "minecraft:key");

        if (protocol is 759 or 760)
        {
            // Plain/"left" branch: no profile key was presented at login-start, so the wire still carries a verify token behind the leading discriminator boolean.
            byte[] plainFrame = key.Encode(new ServerboundKeyPacket([0x11, 0x22], [0x33]));
            Assert.Equal([0x02, 0x11, 0x22, 0x01, 0x01, 0x33], plainFrame);
            var decodedPlain = Assert.IsType<ServerboundKeyPacket>(key.DecodeFrame(plainFrame));
            Assert.Equal(new byte[] { 0x33 }, decodedPlain.VerifyToken);
            Assert.Null(decodedPlain.SignedChallenge);

            // Signed/"right" branch: a profile key WAS presented, so the token is replaced by a salt + SHA256withRSA signature pair: a salt long followed by a length-prefixed signature.
            var challenge = new SignedChallengeData(0x0102030405060708L, [0xAA, 0xBB]);
            byte[] signedFrame = key.Encode(new ServerboundKeyPacket([0x11, 0x22], []) { SignedChallenge = challenge });
            Assert.Equal(
                [0x02, 0x11, 0x22, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x02, 0xAA, 0xBB],
                signedFrame);
            var decodedSigned = Assert.IsType<ServerboundKeyPacket>(key.DecodeFrame(signedFrame));
            Assert.NotNull(decodedSigned.SignedChallenge);
            Assert.Equal(challenge.Salt, decodedSigned.SignedChallenge!.Salt);
            Assert.Equal(challenge.Signature, decodedSigned.SignedChallenge.Signature);
        }
        else
        {
            byte[] frame = key.Encode(new ServerboundKeyPacket([0x11, 0x22], [0x33]));
            Assert.Equal([0x02, 0x11, 0x22, 0x01, 0x33], frame);
            Assert.IsType<ServerboundKeyPacket>(key.DecodeFrame(frame));
        }
    }

    // the encryption-response ARM, and its agreement with the bound era codec

    /// <summary>The codec chooses the wire SHAPE per era; the login driver chooses which ARM to fill in. Those two decisions must be made from the same era condition, because they are not independently valid: the plain codec (every protocol except 759/760) has no discriminator and writes <c>ServerboundKeyPacket.VerifyToken</c> unconditionally, while the signed arm deliberately leaves that token EMPTY because on 759/760 it is replaced on the wire by the salt+signature pair. Pick the signed arm on a plain-shape protocol and the frame is structurally valid but semantically dead: a zero-length verify token, which the server rejects as a protocol error.</summary>
    /// <remarks>
    /// <para>Asserted through the driver's own arm-choice entry point AND the registrar-resolved codec, so the test measures the pair rather than either half: the packet is built by <c>JavaClientLogin.BuildKeyResponse</c> and then encoded by whatever <see cref="BoundCodec.LoginAt"/> resolves at that protocol - exactly the two components that cooperate. Protocols 758 and 761 are the immediate neighbours of the Either band; 759 and 760 are the positive controls for the signed arm.</para>
    /// </remarks>
    [Theory]
    [InlineData(758)]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    public void KeyResponseArm_FollowsTheSameWireLayoutConditionAsTheBinding(int protocol)
    {
        using RSA serverKey = RSA.Create(2048);
        using RSA profileKey = RSA.Create(2048);
        var options = new JavaLoginOptions
        {
            Username = "Notch",
            ServerHost = "localhost",
            ServerPort = 25565,
            ProfileKey = new ProfilePublicKeyData(0L, profileKey.ExportSubjectPublicKeyInfo(), [0x01]),
            ProfileCertificates = new PlayerCertificates(
                profileKey.ExportSubjectPublicKeyInfoPem(), profileKey.ExportPkcs8PrivateKeyPem(),
                string.Empty, string.Empty, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow),
        };

        ServerboundKeyPacket built = JavaClientLogin.BuildKeyResponse(
            protocol, options, [0x11, 0x22], VerifyToken, serverKey);

        BoundPacketCodec bound = BoundCodec.LoginAt(protocol, PacketFlow.Serverbound, "minecraft:key");
        var decoded = Assert.IsType<ServerboundKeyPacket>(bound.DecodeFrame(bound.Encode(built)));

        if (protocol is 759 or 760)
        {
            // The Either band: the server holds the profile key from login-start, so it demands the signed arm and the token is legitimately absent from the wire.
            Assert.NotNull(decoded.SignedChallenge);
            Assert.True(
                ProfileKeyChallenge.Verify(
                    options.ProfileCertificates!.PublicKeyPem, VerifyToken,
                    decoded.SignedChallenge!.Salt, decoded.SignedChallenge.Signature),
                $"the signed arm at protocol {protocol} must verify against the profile public key");
            Assert.Empty(decoded.VerifyToken);
        }
        else
        {
            // Plain shape: the token is the ONLY challenge the server can check, so it must be the real RSA-encrypted verify token and never the signed arm's empty placeholder.
            Assert.Null(decoded.SignedChallenge);
            Assert.NotEmpty(decoded.VerifyToken);
            Assert.Equal(VerifyToken, serverKey.Decrypt(decoded.VerifyToken, RSAEncryptionPadding.Pkcs1));
        }
    }

    /// <summary>The drift guard for the era condition above. <c>LoginCodecs.KeyPacketIsEitherWrapped</c> and the <c>Serverbound.Key</c> timeline state one boundary twice - the predicate as a comparison against <c>JavaProtocols.V1_19</c>/<c>V1_19_3</c>, the timeline as <c>.From</c> entries on the same two markers - and that is only safe while something checks they agree. This walks every supported protocol and asserts the predicate matches the codec the registrar ACTUALLY resolves, so moving either one alone is a red test rather than a silently mismatched arm.</summary>
    [Fact]
    public void KeyPacketEitherWireLayoutCondition_AgreesWithTheResolvedCodec_OnEveryProtocol()
    {
        foreach (int protocol in All)
        {
            bool eitherWrapped = LoginCodecs.KeyPacketIsEitherWrapped(protocol);
            string identity = BoundCodec
                .LoginAt(protocol, PacketFlow.Serverbound, "minecraft:key")
                .CodecIdentity;

            Assert.Equal(protocol is 759 or 760, eitherWrapped);
            Assert.Equal(
                eitherWrapped ? "LoginCodecs.KeyV1_19" : "LoginCodecs.Key",
                identity);
        }
    }

    // configuration family: the resource-pack exchange and server links

    /// <summary>1.20.2 carries one configuration resource-pack packet, <c>minecraft:resource_pack</c>, with no pack uuid; 1.20.3 splits it into push/pop and adds the uuid. The prompt is a JSON string in 1.20.2 (the component transport moves to network NBT at 765), and its dialect moves at 770.</summary>
    [Theory]
    [InlineData(764, false, false, false)]
    [InlineData(765, true, true, false)]
    [InlineData(769, true, true, false)]
    [InlineData(770, true, true, true)]
    [InlineData(776, true, true, true)]
    public void ConfigResourcePackPush_UsesTheWireLayoutOfItsProtocol(int protocol, bool hasId, bool nbtPrompt, bool modernPrompt)
    {
        var id = new Guid("12345678-90ab-cdef-1234-567890abcdef");
        BoundPacketCodec bound = ConfigBound(
            protocol, PacketFlow.Clientbound, protocol >= 765 ? "minecraft:resource_pack_push" : "minecraft:resource_pack");
        byte[] frame = bound.Encode(new ClientboundConfigResourcePackPushPacket(
            id, "https://packs.example.invalid/x.zip", "abcdef", Required: true, KickReason));

        // Walk the fixed prefix so the prompt's first byte is found exactly rather than guessed: [uuid?] VarInt+url, VarInt+hash, required flag, present flag, prompt.
        int at = hasId ? 16 : 0;
        Assert.Equal(hasId, at == 16 && frame[0] == 0x12 && frame[15] == 0xEF);
        at = SkipLengthPrefixed(frame, at);
        at = SkipLengthPrefixed(frame, at);
        Assert.Equal(0x01, frame[at]);     // required
        Assert.Equal(0x01, frame[at + 1]); // prompt present
        at += 2;

        // A network-NBT prompt opens with the TAG_Compound id 0x0A; a JSON one is a length-prefixed string whose body starts with '{'.
        if (nbtPrompt)
            Assert.Equal(0x0A, frame[at]);

        else
            Assert.Equal((byte)'{', frame[at + SkipVarInt(frame[at..])]);

        Assert.Equal(modernPrompt, Contains(frame, "click_event"));
        Assert.Equal(!modernPrompt, Contains(frame, "clickEvent"));

        var decoded = Assert.IsType<ClientboundConfigResourcePackPushPacket>(bound.DecodeFrame(frame));
        Assert.Equal(hasId ? id : Guid.Empty, decoded.Id);
        Assert.Equal("https://packs.example.invalid/x.zip", decoded.Url);
        Assert.NotNull(decoded.Prompt);
        Assert.Equal(frame, bound.Encode(decoded));
    }

    /// <summary>The client's answer. On 1.20.2 it is the action ordinal alone; the pack uuid arrives with the 1.20.3 split. Writing the UUID form on 1.20.2 puts 16 bytes in front of the ordinal, causing the server to read UUID bytes as the action.</summary>
    [Theory]
    [InlineData(764, 1)]
    [InlineData(765, 17)]
    [InlineData(770, 17)]
    [InlineData(776, 17)]
    public void ConfigResourcePackResponse_CarriesTheUuidOnlyFrom765(int protocol, int expectedLength)
    {
        var id = new Guid("12345678-90ab-cdef-1234-567890abcdef");
        BoundPacketCodec bound = ConfigBound(protocol, PacketFlow.Serverbound, "minecraft:resource_pack");
        byte[] frame = bound.Encode(new ServerboundConfigResourcePackPacket(id, 3));

        Assert.Equal(expectedLength, frame.Length);
        Assert.Equal(0x03, frame[^1]);

        var decoded = Assert.IsType<ServerboundConfigResourcePackPacket>(bound.DecodeFrame(frame));
        Assert.Equal(3, decoded.Action);
        Assert.Equal(protocol >= 765 ? id : Guid.Empty, decoded.Id);
    }

    /// <summary>Server links arrived in 1.21, three releases before the component dialect moved, so a custom link label must use the legacy spelling on 767-769. The play-phase copy already had both eras; the configuration copy was pinned to the modern one from 767.</summary>
    [Theory]
    [InlineData(767, false)]
    [InlineData(768, false)]
    [InlineData(769, false)]
    [InlineData(770, true)]
    [InlineData(776, true)]
    public void ConfigServerLinks_LabelDialectBoundary_IsProtocol770(int protocol, bool modern)
    {
        byte[] frame = ConfigBound(protocol, PacketFlow.Clientbound, "minecraft:server_links")
            .Encode(new ClientboundConfigServerLinksPacket([new ServerLinkEntry(null, KickReason, "https://example.invalid")]));

        Assert.Equal(modern, Contains(frame, "click_event"));
        Assert.Equal(!modern, Contains(frame, "clickEvent"));
    }

    // helpers

    /// <summary>The bound CONFIGURATION-phase codec for an identifier at a protocol number.</summary>
    private static BoundPacketCodec ConfigBound(int protocol, PacketFlow flow, string identifier)
    {
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Configuration, flow, 0, identifier);
        ProtocolDescriptor descriptor = builder.Build();

        Assert.True(descriptor.GetRegistry(ProtocolPhase.Configuration, flow).TryGetInbound(0, out BoundPacketCodec bound));
        Assert.True(bound.IsImplemented, $"configuration {identifier} is a marker at protocol {protocol}");
        return bound;
    }

    private static TheoryData<int> Subset(Func<int, bool> predicate)
    {
        var data = new TheoryData<int>();
        foreach (int protocol in All.Where(predicate))
            data.Add(protocol);

        return data;
    }

    private static byte[] Filled(int length, byte value)
    {
        byte[] bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }

    private static ProtocolDescriptorBuilder LoginDriverDescriptor(int protocol)
    {
        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Handshake, PacketFlow.Serverbound, 0, "minecraft:intention");
        PacketRegistrar.Register(builder, ProtocolPhase.Login, PacketFlow.Serverbound, 0, "minecraft:hello");
        return builder;
    }

    private static JavaLoginOptions LoginOptions() => new()
    {
        Username = "Tester",
        ServerHost = "localhost",
        ServerPort = 25565,
    };

    private static bool Contains(byte[] frame, string marker) =>
        frame.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker).AsSpan()) >= 0;

    /// <summary>The index just past a leading VarInt (the JSON string's length prefix).</summary>
    private static int SkipVarInt(ReadOnlySpan<byte> frame)
    {
        int i = 0;
        while ((frame[i] & 0x80) != 0)
            i++;

        return i + 1;
    }

    /// <summary>The index just past a VarInt-length-prefixed field starting at <paramref name="at"/>.</summary>
    private static int SkipLengthPrefixed(byte[] frame, int at)
    {
        int length = 0;
        int shift = 0;
        while (true)
        {
            byte b = frame[at++];
            length |= (b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
                break;

        }

        return at + length;
    }
}
