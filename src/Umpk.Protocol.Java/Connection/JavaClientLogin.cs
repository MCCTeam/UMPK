using System.Buffers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Crypto;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Umpk.Protocol.Java.Transport;

namespace Umpk.Protocol.Java;

/// <summary>Options for the client login driver. Offline mode needs only the username and dialed host/port; online mode additionally supplies an <see cref="ISessionAuthenticator"/> and the profile <see cref="Credentials"/> so the driver can answer the server's encryption request.</summary>
public sealed record JavaLoginOptions
{
    /// <summary>The username to log in with (must match the credentials profile name in online mode).</summary>
    public required string Username { get; init; }

    /// <summary>The server host as dialed, sent in the handshake.</summary>
    public required string ServerHost { get; init; }

    /// <summary>The server port as dialed, sent in the handshake.</summary>
    public required ushort ServerPort { get; init; }

    /// <summary>The player profile id sent in login-start (usually derived from the username offline).</summary>
    public Guid ProfileId { get; init; } = Guid.Empty;

    /// <summary>The profile public key attached to login-start on the 1.19/1.19.1 signing eras (protocols 759/760). Null on offline sends and on every other era; the 1.19.3+ eras move the key to the <c>chat_session_update</c> packet, so the login-start codec never emits it there.</summary>
    public Packets.ProfilePublicKeyData? ProfileKey { get; init; }

    /// <summary>
    /// The certificates paired with <see cref="ProfileKey"/>, required whenever it is set. On the 1.19/1.19.1 signing eras (protocols 759/760), the server already holds the profile public key from login-start by the time it sends its encryption request, so it expects the encryption response to carry a signed challenge (proving possession of the matching PRIVATE key) instead of the usual RSA-encrypted verify token; see <c>LoginCodecs.KeyV1_19</c>. Must be non-null whenever <see cref="ProfileKey"/> is set, on any protocol, or the driver fails fast rather than sending a hello the server will accept and a key response it cannot validate.
    /// <para>On every OTHER protocol the encryption response has no second arm to put a signature in, so the driver keeps sending the RSA-encrypted verify token and never consults these certificates at login (<c>LoginCodecs.KeyPacketIsEitherWrapped</c> is the single era condition governing both the arm and the bound codec). Outside protocols 759-760, the verify token must remain intact even when certificates are supplied.</para>
    /// </summary>
    public PlayerCertificates? ProfileCertificates { get; init; }

    /// <summary>The session authenticator used to prove ownership before the server enables encryption. When <see langword="null"/> the driver runs offline mode and ignores any encryption request.</summary>
    public ISessionAuthenticator? Authenticator { get; init; }

    /// <summary>The profile plus access token proving ownership, required when <see cref="Authenticator"/> is set.</summary>
    public ProfileCredentials? Credentials { get; init; }

    /// <summary>The static registries (item registry etc.) to install into the codec context at the transition into the Play phase, before the first play packet is decoded. When <see langword="null"/> the codec context keeps its empty registries, which cannot resolve non-air item ids. A composition root that has the version's data supplies this (see <c>Umpk.Data.Java.JavaGameData.Registries</c>).</summary>
    public Umpk.Game.Registries.RegistryAccess? PlayRegistries { get; init; }

    /// <summary>
    /// Invoked, in frame order and awaited, for each configuration-phase packet the driver decodes but does not itself act on, so a host can track configuration-phase state instead of losing it.
    /// <para>The driver only decodes what it needs: today that is the 1.21.6+ dialog pair (<c>minecraft:show_dialog</c> and <c>minecraft:clear_dialog</c>), which a server can send during configuration and which the host must see and be able to answer before configuration ends. Other configuration traffic stays frame-level here.</para>
    /// </summary>
    public Func<ObservedConfigurationPacket, CancellationToken, ValueTask>? ConfigurationPacketObserver { get; init; }

    /// <summary>The client-information announce sent during the configuration phase (1.20.2+). Defaults to the documented <see cref="ClientInformationOptions"/> values.</summary>
    public ClientInformationOptions ClientInformation { get; init; } = new();

    /// <summary>The client brand announced alongside the client information, or <see langword="null"/> to announce none. See <see cref="JavaConfigurationOptions.AnnounceBrand"/> for the shape and the ordering.</summary>
    public string? ClientBrand { get; init; }

    /// <summary>The login-phase plugin channels a host has claimed. A <c>minecraft:custom_query</c> whose channel is claimed is answered with whatever the responder returns; every other channel receives <c>understood = false</c>. <see langword="null"/> (the default) leaves every channel unclaimed.</summary>
    public LoginQueryResponders? LoginQueries { get; init; }

    /// <summary>The per-session cookie store used in login and configuration.</summary>
    public CookieStore? Cookies { get; init; }

    /// <summary>Whether the handshake intention is a server-directed transfer rather than a normal login.</summary>
    public bool TransferIntent { get; init; }

    /// <summary>The configuration-to-play finalizer propagated to the phase driver.</summary>
    public Func<CancellationToken, ValueTask<Umpk.Game.Registries.RegistryAccess?>>? BeforePlay { get; init; }

    /// <summary>True when this login should run the online-mode encryption handshake.</summary>
    public bool IsOnlineMode => Authenticator is not null;

    /// <summary>Projects the login options onto the phase-independent configuration options. Login announces the client information; see <see cref="JavaConfigurationOptions.AnnounceClientInformation"/>.</summary>
    internal JavaConfigurationOptions ToConfigurationOptions() => new()
    {
        PacketObserver = ConfigurationPacketObserver,
        AnnounceClientInformation = ClientInformation,
        AnnounceBrand = ClientBrand,
        PlayRegistries = PlayRegistries,
        Cookies = Cookies,
        BeforePlay = BeforePlay,
    };
}

/// <summary>The result of a successful login reach-play.</summary>
public sealed record LoginResult(Guid Uuid, string Username, ProtocolPhase Phase);

/// <summary>Drives an offline-mode client login on a bound <see cref="JavaConnection"/> to the play phase and keeps it there. Handles handshake, login start, set-compression, login success, the 1.20.2+ login-acknowledged handoff, and the configuration phase (client information, known packs, registry data drain, finish configuration). Answers every login-plugin request nothing claims with <c>understood = false</c>, allowing the server to continue past an unsupported query.</summary>
/// <remarks>The driver reads at the frame level so it can respond to registered-but-unimplemented packets (custom_query, ping, keep-alive) without needing their codecs, decoding only the handful it acts on.</remarks>
public static class JavaClientLogin
{
    /// <summary>Runs the login handshake through to the play phase on an offline server.</summary>
    public static async Task<LoginResult> LoginAsync(
        JavaConnection connection, JavaVersion version, JavaLoginOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(options);

        ProtocolDescriptor descriptor = version.Protocol;
        bool hasConfigPhase = version.Features.ConfigurationPhase;

        // Handshake (next state = 2, login).
        await SendAsync(connection, descriptor, ProtocolPhase.Handshake,
            new ServerboundHandshakePacket(
                version.Version.Protocol, options.ServerHost, options.ServerPort, options.TransferIntent ? 3 : 2), ct)
            .ConfigureAwait(false);
        connection.SetPhase(ProtocolPhase.Login);

        // Login start.
        await SendAsync(connection, descriptor, ProtocolPhase.Login,
            new ServerboundHelloPacket(options.Username, options.ProfileId) { ProfileKey = options.ProfileKey },
            ct).ConfigureAwait(false);

        LoginResult result = await RunLoginPhaseAsync(connection, descriptor, options, hasConfigPhase, ct).ConfigureAwait(false);

        if (hasConfigPhase)
            await RunConfigurationPhaseAsync(connection, descriptor, options.ToConfigurationOptions(), ct)
                .ConfigureAwait(false);

        return result with { Phase = ProtocolPhase.Play };
    }

    /// <summary>Runs the configuration phase on a connection that is ALREADY in it, to the point where the server ends it with <c>finish_configuration</c> and this driver acknowledges and returns to play. This is also used for play-to-configuration re-entry (1.20.2+), with the same packet sequence for both entry paths.</summary>
    /// <remarks>The caller owns the phase hop INTO configuration (send <c>minecraft:configuration_acknowledged</c> while the connection is still in play, then <see cref="JavaConnection.SetPhase"/>); this method owns the hop back out. It reads frames directly off the connection, so the caller must not be concurrently draining <see cref="JavaConnection.ReceiveAsync"/> for the duration.</remarks>
    public static Task RunConfigurationPhaseAsync(
        JavaConnection connection, JavaVersion version, JavaConfigurationOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(options);
        return RunConfigurationPhaseAsync(connection, version.Protocol, options, ct);
    }

    private static async Task<LoginResult> RunLoginPhaseAsync(
        JavaConnection connection, ProtocolDescriptor descriptor, JavaLoginOptions options, bool hasConfigPhase, CancellationToken ct)
    {
        PhaseRegistry loginIn = descriptor.GetRegistry(ProtocolPhase.Login, PacketFlow.Clientbound);
        int helloId = WireIdOf(loginIn, Identifier.Minecraft("hello"));
        int customQueryId = WireIdOf(loginIn, Identifier.Minecraft("custom_query"));
        int compressionId = WireIdOf(loginIn, Identifier.Minecraft("login_compression"));
        int finishedId = WireIdOf(loginIn, Identifier.Minecraft("login_finished"));
        int disconnectId = WireIdOf(loginIn, Identifier.Minecraft("login_disconnect"));
        int cookieRequestId = WireIdOf(loginIn, Identifier.Minecraft("cookie_request"));

        await foreach (InboundFrame frame in connection.ReceiveFramesAsync(ct).ConfigureAwait(false))
        {
            if (frame.WireId == helloId && helloId >= 0)
                await HandleEncryptionRequestAsync(connection, descriptor, loginIn, options, frame.CopyPayload(), ct)
                    .ConfigureAwait(false);

            else if (frame.WireId == compressionId && compressionId >= 0)
            {
                int threshold = ReadFirstVarInt(frame.Payload);
                if (threshold >= 0)
                    connection.EnableCompression(threshold);

            }
            else if (frame.WireId == customQueryId && customQueryId >= 0)
            {
                // CopyPayload, not frame.Payload: a claimed channel's responder is awaited, and a span cannot cross an await. Unclaimed channels take the same copy, which costs one small array per query on a path that runs at most a handful of times per login.
                await RespondLoginPluginQueryAsync(connection, descriptor, options, frame.CopyPayload(), ct)
                    .ConfigureAwait(false);
            }
            else if (frame.WireId == cookieRequestId && cookieRequestId >= 0)
            {
                if (!loginIn.TryGetInbound(cookieRequestId, out BoundPacketCodec cookieEntry)
                    || !cookieEntry.IsImplemented)
                    throw new ProtocolViolationException("login cookie_request has no implemented codec.");

                var request = (ClientboundLoginCookieRequestPacket)DecodeRequired(
                    connection, cookieEntry, frame.Payload, BindingContext(connection));
                byte[]? payload = await ResolveCookieAsync(options.Cookies, request.Key, ct).ConfigureAwait(false);
                await SendAsync(connection, descriptor, ProtocolPhase.Login,
                    new ServerboundLoginCookieResponsePacket(request.Key, payload), ct).ConfigureAwait(false);
            }
            else if (frame.WireId == finishedId && finishedId >= 0)
            {
                if (!loginIn.TryGetInbound(finishedId, out BoundPacketCodec entry) || !entry.IsImplemented)
                    throw new ProtocolViolationException("login_finished has no implemented codec.");

                var finished = (ClientboundLoginFinishedPacket)DecodeRequired(
                    connection, entry, frame.Payload, BindingContext(connection));

                if (hasConfigPhase)
                {
                    // Acknowledge login, entering configuration (terminal packet).
                    connection.SetPhase(ProtocolPhase.Configuration);
                    await SendAsync(connection, descriptor, ProtocolPhase.Login,
                        new ServerboundLoginAcknowledgedPacket(), ct).ConfigureAwait(false);
                }
                else
                {
                    // No config phase (pre-1.20.2): the read loop is parked at this terminal packet, so install the static registries into the codec context before Play decoding resumes.
                    InstallPlayRegistries(connection, options.PlayRegistries);
                    connection.SetPhase(ProtocolPhase.Play);
                }

                return new LoginResult(finished.Uuid, finished.Username,
                    hasConfigPhase ? ProtocolPhase.Configuration : ProtocolPhase.Play);
            }
            else if (frame.WireId == disconnectId && disconnectId >= 0)
            {
                // The reason component is the whole content of a login kick (whitelist, ban, version mismatch), and the socket closes right behind this frame, so decoding it here is the only chance to name it. There is no login-phase observer, so the flattened text reaches the caller through the exception message; the decoded component rides along on ConnectionClosedException.Disconnect so a caller is not limited to that flattened form.
                (string message, Umpk.Text.Component? reason) = LoginKickDetails(loginIn, frame);
                throw new ConnectionClosedException(CloseReason.DisconnectMessage, message) { Disconnect = reason };
            }

        }

        throw new ConnectionClosedException(CloseReason.SocketEof, "Server closed during login.");
    }

    /// <summary>
    /// Handles the server's encryption request (online mode): generate a 16-byte shared secret, RSA-encrypt the secret (PKCS#1 v1.5), compute the server-id hash, prove session ownership through the authenticator, send the key response, then enable AES-CFB8 encryption on the connection. Offline logins that unexpectedly receive a hello with no authenticator configured fail fast rather than stalling.
    /// <para>The verify token itself is handled two ways: normally it is RSA-encrypted with the server's key and echoed back, but on the 1.19/1.19.1 signing eras (protocols 759/760), when <see cref="JavaLoginOptions.ProfileKey"/> was attached to login-start, the server already holds that profile public key and its wire reader for THIS packet unconditionally expects a signed challenge (the token plus a salt, signed with the paired private key) instead - see <c>LoginCodecs.KeyV1_19</c> for the wire evidence. Sending the plain form there desyncs the frame and the server's decoder closes the connection without a graceful login disconnect.</para>
    /// </summary>
    private static async Task HandleEncryptionRequestAsync(
        JavaConnection connection, ProtocolDescriptor descriptor, PhaseRegistry loginIn,
        JavaLoginOptions options, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (!loginIn.TryGetInbound(WireIdOf(loginIn, Identifier.Minecraft("hello")), out BoundPacketCodec entry) || !entry.IsImplemented)
            throw new ProtocolViolationException("Encryption request (hello) has no implemented codec.");

        var hello = (ClientboundHelloPacket)DecodeRequired(
            connection, entry, payload.Span, BindingContext(connection), suppressEvidence: true);

        if (!options.IsOnlineMode || options.Authenticator is null || options.Credentials is null)
            throw new ProtocolViolationException(
                "Server requested encryption but no session authenticator was configured (online mode required).");

        if (options.ProfileKey is not null && options.ProfileCertificates is null)
        {
            // The hello already told the server we have a key, so its ServerboundKeyPacket reader now unconditionally expects a signed challenge (see LoginCodecs.KeyV1_19); sending the plain form here would desynchronize the frame. Fail fast instead.
            throw new ProtocolViolationException(
                "JavaLoginOptions.ProfileKey was set without ProfileCertificates; cannot sign the " +
                "1.19/1.19.1 encryption-response challenge without the paired private key.");
        }

        // Generate the AES shared secret (16 bytes) used for both the session hash and the stream cipher.
        byte[] sharedSecret = RandomNumberGenerator.GetBytes(16);

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(hello.PublicKey, out _);
        byte[] encryptedSecret = rsa.Encrypt(sharedSecret, RSAEncryptionPadding.Pkcs1);

        ServerboundKeyPacket keyPacket = BuildKeyResponse(
            descriptor.Version.Protocol, options, encryptedSecret, hello.VerifyToken, rsa);

        string serverIdHash = MinecraftServerId.Compute(hello.ServerId, sharedSecret, hello.PublicKey);
        await options.Authenticator.JoinServerAsync(serverIdHash, options.Credentials, ct).ConfigureAwait(false);

        await SendAsync(connection, descriptor, ProtocolPhase.Login, keyPacket, ct).ConfigureAwait(false);

        // Vanilla enables encryption immediately after sending the key response, before the next read.
        connection.EnableEncryption(sharedSecret);
    }

    /// <summary>Builds the encryption response, choosing which of vanilla's two arms to fill in. The signed challenge is built only when the protocol's <c>key</c> packet actually HAS a second arm - the 1.19/1.19.1 Either shape, per <c>LoginCodecs.KeyPacketIsEitherWrapped</c>, which is the same era condition that binds <c>LoginCodecs.KeyV1_19</c> - and only when a profile key was presented at login-start, which is what makes the server demand it.</summary>
    /// <remarks>The era gate is not redundant with the binding: the arm and the shape are two decisions, and the plain codec writes <see cref="ServerboundKeyPacket.VerifyToken"/> unconditionally while the signed arm leaves that token empty on purpose. Keying the arm on profile-key presence alone would put a zero-length challenge on the wire for any key-carrying login outside 759-760. Pinned per era by <c>LoginEraBindingTests.KeyResponseArm_FollowsTheSameEraConditionAsTheBinding</c>.</remarks>
    internal static ServerboundKeyPacket BuildKeyResponse(
        int protocol, JavaLoginOptions options, byte[] encryptedSecret, byte[] verifyToken, RSA serverKey)
    {
        if (LoginCodecs.KeyPacketIsEitherWrapped(protocol)
            && options.ProfileKey is not null
            && options.ProfileCertificates is { } certificates)
        {
            byte[] saltBytes = RandomNumberGenerator.GetBytes(8);
            long salt = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(saltBytes);
            byte[] signature = ProfileKeyChallenge.Sign(certificates, verifyToken, salt);
            return new ServerboundKeyPacket(encryptedSecret, [])
            {
                SignedChallenge = new SignedChallengeData(salt, signature),
            };
        }

        return new ServerboundKeyPacket(encryptedSecret, serverKey.Encrypt(verifyToken, RSAEncryptionPadding.Pkcs1));
    }

    private static async Task RunConfigurationPhaseAsync(
        JavaConnection connection, ProtocolDescriptor descriptor,
        JavaConfigurationOptions options, CancellationToken ct)
    {
        PhaseRegistry configIn = descriptor.GetRegistry(ProtocolPhase.Configuration, PacketFlow.Clientbound);
        int keepAliveId = WireIdOf(configIn, Identifier.Minecraft("keep_alive"));
        int pingId = WireIdOf(configIn, Identifier.Minecraft("ping"));
        int selectPacksId = WireIdOf(configIn, Identifier.Minecraft("select_known_packs"));
        int finishId = WireIdOf(configIn, Identifier.Minecraft("finish_configuration"));
        int disconnectId = WireIdOf(configIn, Identifier.Minecraft("disconnect"));
        int cookieRequestId = WireIdOf(configIn, Identifier.Minecraft("cookie_request"));
        int storeCookieId = WireIdOf(configIn, Identifier.Minecraft("store_cookie"));
        int transferId = WireIdOf(configIn, Identifier.Minecraft("transfer"));
        int resourcePackPushId = WireIdOf(configIn, Identifier.Minecraft("resource_pack_push"));

        // A server may show a dialog during configuration and wait on the answer before finishing, so the pair is decoded and surfaced to the host here rather than dropped with the rest of the configuration traffic. Both are markers before 1.21.6, where WireIdOf returns -1.
        int showDialogId = options.PacketObserver is null
            ? -1
            : WireIdOf(configIn, Identifier.Minecraft("show_dialog"));
        int clearDialogId = options.PacketObserver is null
            ? -1
            : WireIdOf(configIn, Identifier.Minecraft("clear_dialog"));

        // On 1.20.2+ the server announces its brand on the minecraft:brand plugin channel during CONFIGURATION and never again during play (the send moved out of PlayerList into ServerConfigurationPacketListenerImpl). Dropping configuration custom payloads therefore loses the brand outright on every modern server, so surface them to the host.
        int customPayloadId = options.PacketObserver is null
            ? -1
            : WireIdOf(configIn, Identifier.Minecraft("custom_payload"));

        // From 1.20.2 the server's own (datapack-customized) registries arrive HERE and nowhere else. Consuming them silently is what left RegistryAccess.DimensionTypes empty on every modern protocol, so a spawn-info dimension-type id had nothing at all to resolve against and the client had to guess a dimension's vertical bounds from its name. Surfaced through the same observer the dialogs and the brand already use; decoding is not applying, and Umpk.Client decides which registries it keeps.
        int registryDataId = options.PacketObserver is null
            ? -1
            : WireIdOf(configIn, Identifier.Minecraft("registry_data"));

        // Send client information immediately so the server can proceed. Skipped on a play-to-configuration re-entry, where vanilla's client sends the acknowledgement and nothing else: see JavaConfigurationOptions.AnnounceClientInformation. Brand BEFORE client information, which is the order vanilla sends them in; see JavaConfigurationOptions.AnnounceBrand.
        if (options.AnnounceBrand is { } clientBrand)
            await SendAsync(connection, descriptor, ProtocolPhase.Configuration,
                new ServerboundConfigCustomPayloadPacket(BrandChannel, EncodeBrand(clientBrand)), ct)
                .ConfigureAwait(false);

        if (options.AnnounceClientInformation is { } announce)
            await SendAsync(connection, descriptor, ProtocolPhase.Configuration,
                announce.ToConfigurationPacket(), ct).ConfigureAwait(false);

        await foreach (InboundFrame frame in connection.ReceiveFramesAsync(ct).ConfigureAwait(false))
        {
            if (frame.WireId == keepAliveId && keepAliveId >= 0)
            {
                long id = ReadFirstLong(frame.Payload);
                await SendAsync(connection, descriptor, ProtocolPhase.Configuration,
                    new ServerboundConfigKeepAlivePacket(id), ct).ConfigureAwait(false);
            }
            else if (frame.WireId == pingId && pingId >= 0)
            {
                // Config ping echoes an int id via the serverbound pong; respond at frame level.
                await RespondConfigPong(connection, descriptor, frame.Payload, ct).ConfigureAwait(false);
            }
            else if (frame.WireId == selectPacksId && selectPacksId >= 0)
            {
                // This client has no pack cache and therefore cannot prove it owns any offered tuple. Echoing an offer falsely claims exact reconstruction and can make the server omit registry data the client still needs.
                if (configIn.TryGetInbound(selectPacksId, out BoundPacketCodec entry) && entry.IsImplemented)
                {
                    _ = (ClientboundSelectKnownPacksPacket)DecodeRequired(
                        connection, entry, frame.Payload, BindingContext(connection));
                    await SendAsync(connection, descriptor, ProtocolPhase.Configuration,
                        new ServerboundSelectKnownPacksPacket([]), ct).ConfigureAwait(false);
                }
                else
                    await SendAsync(connection, descriptor, ProtocolPhase.Configuration,
                        new ServerboundSelectKnownPacksPacket([]), ct).ConfigureAwait(false);

            }
            else if (frame.WireId == cookieRequestId && cookieRequestId >= 0)
            {
                if (!configIn.TryGetInbound(cookieRequestId, out BoundPacketCodec cookieEntry)
                    || !cookieEntry.IsImplemented)
                    throw new ProtocolViolationException("configuration cookie_request has no implemented codec.");

                var request = (ClientboundConfigCookieRequestPacket)DecodeRequired(
                    connection, cookieEntry, frame.Payload, BindingContext(connection));
                byte[]? payload = await ResolveCookieAsync(options.Cookies, request.Key, ct).ConfigureAwait(false);
                await SendAsync(connection, descriptor, ProtocolPhase.Configuration,
                    new ServerboundConfigCookieResponsePacket(request.Key, payload), ct).ConfigureAwait(false);
            }
            else if (frame.WireId == storeCookieId && storeCookieId >= 0)
            {
                if (!configIn.TryGetInbound(storeCookieId, out BoundPacketCodec storeEntry)
                    || !storeEntry.IsImplemented)
                    throw new ProtocolViolationException("configuration store_cookie has no implemented codec.");

                var store = (ClientboundConfigStoreCookiePacket)DecodeRequired(
                    connection, storeEntry, frame.Payload, BindingContext(connection));
                options.Cookies?.Set(store.Key, store.Payload);
            }
            else if ((frame.WireId == showDialogId && showDialogId >= 0)
                || (frame.WireId == clearDialogId && clearDialogId >= 0)
                || (frame.WireId == customPayloadId && customPayloadId >= 0)
                || (frame.WireId == registryDataId && registryDataId >= 0)
                || (frame.WireId == transferId && transferId >= 0)
                || (frame.WireId == resourcePackPushId && resourcePackPushId >= 0))
            {
                if (configIn.TryGetInbound(frame.WireId, out BoundPacketCodec observedEntry) && observedEntry.IsImplemented)
                {
                    object observedPacket = DecodeRequired(
                        connection, observedEntry, frame.Payload, BindingContext(connection));
                    await options.PacketObserver!(
                            new ObservedConfigurationPacket(observedPacket, frame.WireId, frame.Payload.Length), ct)
                        .ConfigureAwait(false);
                    if (frame.WireId == transferId)
                        throw new ConnectionClosedException(
                            CloseReason.SocketEof, "Server requested a transfer during configuration.");
                }
            }
            else if (frame.WireId == finishId && finishId >= 0)
            {
                // The read loop is parked at this terminal packet; install the static registries into the codec context before Play decoding resumes so item ids resolve on the first play packet.
                Umpk.Game.Registries.RegistryAccess? finalized = options.BeforePlay is null
                    ? options.PlayRegistries
                    : await options.BeforePlay(ct).ConfigureAwait(false);
                InstallPlayRegistries(connection, finalized);
                connection.SetPhase(ProtocolPhase.Play);
                await SendAsync(connection, descriptor, ProtocolPhase.Configuration,
                    new ServerboundFinishConfigurationPacket(), ct).ConfigureAwait(false);
                return;
            }
            else if (frame.WireId == disconnectId && disconnectId >= 0)
                throw await ConfigurationKickAsync(connection, configIn, options, frame, ct).ConfigureAwait(false);

            // update_tags, resource-pack pushes, etc. are consumed and ignored; the offline reach-play driver only needs to stay in sync. registry_data is no longer in that list: it is surfaced above so Umpk.Client can keep the registries it needs.
        }

        throw new ConnectionClosedException(CloseReason.SocketEof, "Server closed during configuration.");
    }

    private static async ValueTask<byte[]?> ResolveCookieAsync(
        CookieStore? cookies, Identifier key, CancellationToken ct)
    {
        if (cookies is null)
            return null;

        try
        {
            byte[]? payload = await cookies.ResolveAsync(key, ct).ConfigureAwait(false);
            return payload is { Length: <= Codecs.LoginConfigWire.CookieMaxPayloadLength } ? payload : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            byte[]? stored = cookies.Get(key);
            return stored is { Length: <= Codecs.LoginConfigWire.CookieMaxPayloadLength } ? stored : null;
        }
    }

    /// <summary>The message and the decoded reason for a login-phase kick: the server's own reason when the frame decodes (both flattened into the message and kept structured for <see cref="ConnectionClosedException.Disconnect"/>), and the bare statement with a null reason when it does not. A decode failure must not replace the close, so it degrades rather than throwing.</summary>
    private static (string Message, Umpk.Text.Component? Reason) LoginKickDetails(PhaseRegistry loginIn, InboundFrame frame)
    {
        const string Fallback = "Server rejected login.";
        try
        {
            if (loginIn.TryGetInbound(frame.WireId, out BoundPacketCodec entry)
                && entry.IsImplemented
                && entry.Decode(frame.Payload, PacketCodecContext.Registryless) is ClientboundLoginDisconnectPacket kick)
                return ($"Server rejected login: {kick.Reason.ToPlainText()}", kick.Reason);

        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ($"{Fallback} ({ex.GetType().Name} while decoding the reason.)", null);
        }

        return (Fallback, null);
    }

    /// <summary>Builds the exception that ends the configuration phase on a server-sent <c>disconnect</c>, after putting the decoded kick through the same observer every other surfaced configuration packet uses.</summary>
    /// <remarks>
    /// The receive loop turns this exception into a <c>DisconnectInfo</c> with a <see cref="CloseReason.DisconnectMessage"/> reason and a null <c>Message</c>. The server then closes the socket, so the frame is the only carrier of the reason. Routing it through the observer reaches <c>ConnectionApplier</c>'s config-disconnect arm, which records the kick before this exception unwinds the driver and the transport close lands behind it.
    /// <para>A reason that fails to decode does not change the outcome: the phase still ends with <see cref="CloseReason.DisconnectMessage"/>, and the decode fault rides along as the inner exception rather than replacing the close.</para>
    /// </remarks>
    private static async ValueTask<ConnectionClosedException> ConfigurationKickAsync(
        JavaConnection connection, PhaseRegistry configIn, JavaConfigurationOptions options,
        InboundFrame frame, CancellationToken ct)
    {
        const string Fallback = "Server disconnected during configuration.";
        object kick;
        try
        {
            if (!configIn.TryGetInbound(frame.WireId, out BoundPacketCodec entry) || !entry.IsImplemented)
                return new ConnectionClosedException(CloseReason.DisconnectMessage, Fallback);

            kick = entry.Decode(frame.Payload, BindingContext(connection));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionClosedException(CloseReason.DisconnectMessage, Fallback, ex);
        }

        if (options.PacketObserver is { } observer)
            await observer(new ObservedConfigurationPacket(kick, frame.WireId, frame.Payload.Length), ct)
                .ConfigureAwait(false);

        string message = kick is ClientboundConfigDisconnectPacket typed
            ? $"Server disconnected during configuration: {typed.Reason.ToPlainText()}"
            : Fallback;
        Umpk.Text.Component? reason = kick is ClientboundConfigDisconnectPacket withReason ? withReason.Reason : null;
        return new ConnectionClosedException(CloseReason.DisconnectMessage, message) { Disconnect = reason };
    }

    /// <summary>Installs the static registries into the connection's codec context, if supplied. Called only at a terminal-packet phase pause (into Play), where the memory model allows a codec-state swap.</summary>
    private static void InstallPlayRegistries(JavaConnection connection, Umpk.Game.Registries.RegistryAccess? registries)
    {
        if (registries is not null)
            connection.SetCodecState(registries, IConnectionCodecState.Empty);

    }

    /// <summary>Answers one login-phase <c>minecraft:custom_query</c>. A channel a host has claimed through <see cref="JavaLoginOptions.LoginQueries"/> is answered with whatever its responder returns; every other channel, and a responder that returns <see langword="null"/>, gets <c>understood = false</c>, which is the required response for an unknown channel. The server BLOCKS on this reply (a forwarding proxy will not proceed without it), so every path here ends in a frame going out.</summary>
    private static async Task RespondLoginPluginQueryAsync(
        JavaConnection connection, ProtocolDescriptor descriptor, JavaLoginOptions options,
        ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        // custom_query: VarInt messageId, Identifier channel, remaining bytes.
        int messageId = ReadFirstVarInt(payload.Span);
        ReadOnlyMemory<byte>? answer = null;

        if (options.LoginQueries is { Count: > 0 } responders
            && TryReadQueryChannel(payload, out Identifier channel, out int dataOffset)
            && responders.TryGetResponder(channel, out Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>?>>? responder))
        {
            try
            {
                answer = await responder!(payload[dataOffset..], ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Degrade to the vanilla answer rather than killing the login: a responder is host code on the driver's read loop, and a claim that faults is a bug in the claimant, not a reason for a session that would otherwise join to fail.
                connection.Logger.LogError(
                    ex, "The login query responder for {Channel} threw; answering not understood.", channel);
                answer = null;
            }
        }

        await RespondLoginPluginQueryAnswerAsync(connection, descriptor, messageId, answer, ct).ConfigureAwait(false);
    }

    /// <summary>Reads the channel identifier out of a <c>custom_query</c> payload and reports where its data begins. False when the frame is short or the channel is not a valid identifier, in which case the query is unclaimable and takes the "not understood" path.</summary>
    private static bool TryReadQueryChannel(ReadOnlyMemory<byte> payload, out Identifier channel, out int dataOffset)
    {
        channel = default;
        dataOffset = 0;
        try
        {
            var reader = new PacketReader(payload.Span);
            _ = reader.ReadVarInt();
            string name = reader.ReadString();
            dataOffset = reader.Position;
            return Identifier.TryParse(name, out channel);
        }
        catch (ProtocolViolationException)
        {
            return false;
        }
    }

    private static Task RespondLoginPluginQueryAnswerAsync(
        JavaConnection connection, ProtocolDescriptor descriptor, int messageId,
        ReadOnlyMemory<byte>? data, CancellationToken ct)
    {
        // Respond with the same messageId; understood is the presence of a payload. The response is a serverbound login packet.
        PhaseRegistry loginOut = descriptor.GetRegistry(ProtocolPhase.Login, PacketFlow.Serverbound);
        int responseId = WireIdOf(loginOut, Identifier.Minecraft("custom_query_answer"));
        if (responseId < 0)
        {
            return Task.CompletedTask; // No response id in this version's table; nothing to send.
        }

        var body = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(body);
        w.WriteVarInt(messageId);
        if (data is { } understood)
        {
            w.WriteBool(true);
            w.WriteBytes(understood.Span);
        }
        else
            w.WriteBool(false);

        return connection.SendFrameAsync(responseId, body.WrittenMemory, ct).AsTask();
    }

    private static Task RespondConfigPong(
        JavaConnection connection, ProtocolDescriptor descriptor, ReadOnlySpan<byte> payload, CancellationToken ct)
    {
        // config ping: int id. pong echoes it.
        int id = payload.Length >= 4 ? ReadFirstInt(payload) : 0;
        PhaseRegistry configOut = descriptor.GetRegistry(ProtocolPhase.Configuration, PacketFlow.Serverbound);
        int pongId = WireIdOf(configOut, Identifier.Minecraft("pong"));
        if (pongId < 0)
            return Task.CompletedTask;

        var body = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(body);
        w.WriteInt(id);
        return connection.SendFrameAsync(pongId, body.WrittenMemory, ct).AsTask();
    }

    private static int ReadFirstInt(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        return reader.ReadInt();
    }

    /// <summary>The channel a client announces its brand on, from 1.13 onward.</summary>
    private static readonly Identifier BrandChannel = Identifier.Minecraft("brand");

    /// <summary>Encodes a brand as the custom payload's body: one length-prefixed UTF-8 string.</summary>
    private static byte[] EncodeBrand(string brand)
    {
        var body = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new PacketWriter(body);
        writer.WriteString(brand);
        return body.WrittenSpan.ToArray();
    }

    private static Task SendAsync<TPacket>(
        JavaConnection connection, ProtocolDescriptor descriptor, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket =>
        LoginDriverWire.SendAsync(connection, descriptor, phase, PacketFlow.Serverbound, packet, ct);

    private static PacketCodecContext BindingContext(JavaConnection _) => PacketCodecContext.Registryless;

    /// <summary>Decodes a packet that this driver requires in order to make forward progress. The connection read loop intentionally delivers these frames undecoded, so this is their single accountable fatal boundary. Callers that deliberately recover a decode error (kick text and unsupported login queries) do not use this helper and therefore do not emit a fatal record.</summary>
    private static object DecodeRequired(
        JavaConnection connection,
        BoundPacketCodec entry,
        ReadOnlySpan<byte> payload,
        PacketCodecContext context,
        bool suppressEvidence = false)
    {
        try
        {
            return entry.Decode(payload, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            connection.ReportFatalDecodeFailure(
                entry.WireId, payload, ex, entry, suppressEvidence);
            throw;
        }
    }

    private static int WireIdOf(PhaseRegistry registry, Identifier id) => LoginDriverWire.WireIdOf(registry, id);

    private static int ReadFirstVarInt(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            return -1;

        var reader = new PacketReader(payload);
        return reader.ReadVarInt();
    }

    private static long ReadFirstLong(ReadOnlySpan<byte> payload)
    {
        var reader = new PacketReader(payload);
        return reader.ReadLong();
    }
}
