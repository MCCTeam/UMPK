using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Crypto;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>Options for the server login driver.</summary>
public sealed record JavaServerLoginOptions
{
    /// <summary>The process-scoped RSA key pair used for the encryption handshake (vanilla: one per server).</summary>
    public required RSA KeyPair { get; init; }

    /// <summary>The session verifier for online-mode logins; <see langword="null"/> forces offline mode.</summary>
    public IServerSessionVerifier? Verifier { get; init; }

    /// <summary>The compression threshold to negotiate; negative disables compression.</summary>
    public int CompressionThreshold { get; init; } = -1;

    /// <summary>The client's remote IP, forwarded to the session verifier for proxy prevention.</summary>
    public IPAddress? ClientIp { get; init; }

    /// <summary>The known packs the server offers in the configuration phase (empty by default).</summary>
    public IReadOnlyList<KnownPack> KnownPacks { get; init; } = [];

    /// <summary>The registry-data packets to emit during configuration (empty by default).</summary>
    public IReadOnlyList<ClientboundConfigRegistryDataPacket> RegistryData { get; init; } = [];
}

/// <summary>The result of a successful server-side login reach-play.</summary>
public sealed record ServerLoginResult(GameProfile Profile, bool OnlineMode, ProtocolPhase Phase);

/// <summary>The server mirror of <see cref="JavaClientLogin"/>: accepts a bound serverbound connection, runs the login sequence to the play phase, and keeps it there. On an online-mode login the server sends the encryption request, validates the returned verify token, decrypts the shared secret, and <b>enables encryption immediately</b>, and only then runs <see cref="IServerSessionVerifier.VerifyJoinAsync"/>, negotiates compression, and sends login-finished. Auth-failure disconnects therefore travel over the encrypted stream, which is what vanilla clients expect. On 1.20.2+ it then drives the configuration phase (registry data, known packs, finish) after the client's login-acknowledged.</summary>
public static class JavaServerLogin
{
    /// <summary>Runs the server login sequence through to the play phase.</summary>
    public static async Task<ServerLoginResult> AcceptAsync(
        JavaConnection connection, JavaVersion version, JavaServerLoginOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.KeyPair);

        ProtocolDescriptor descriptor = version.Protocol;
        bool hasConfigPhase = version.Features.ConfigurationPhase;

        // Route by the handshake next-state field: 2 is login and 3 is transfer; both enter this flow.
        if (connection.Phase == ProtocolPhase.Handshake)
        {
            ServerboundHandshakePacket handshake = await ReceiveAsync<ServerboundHandshakePacket>(
                connection, descriptor, ProtocolPhase.Handshake, Identifier.Minecraft("intention"), ct).ConfigureAwait(false);
            if (handshake.NextState != 2 && handshake.NextState != 3)
                throw new ProtocolViolationException($"Unexpected handshake next-state {handshake.NextState} for a login.");

            connection.SetPhase(ProtocolPhase.Login);
        }

        // Receive login-start.
        ServerboundHelloPacket loginStart = await ReceiveAsync<ServerboundHelloPacket>(
            connection, descriptor, ProtocolPhase.Login, Identifier.Minecraft("hello"), ct).ConfigureAwait(false);

        bool onlineMode = options.Verifier is not null;
        GameProfile profile = onlineMode
            ? await RunOnlineAuthAsync(connection, descriptor, options, loginStart.Username, ct).ConfigureAwait(false)
            : new GameProfile(OfflineUuid(loginStart.Username), loginStart.Username);

        // Compression negotiation (before login-finished so the finished packet is already compressed).
        if (options.CompressionThreshold >= 0)
        {
            await SendAsync(connection, descriptor, ProtocolPhase.Login,
                new ClientboundLoginCompressionPacket(options.CompressionThreshold), ct).ConfigureAwait(false);
            connection.EnableCompression(options.CompressionThreshold);
        }

        // Login finished / game profile. The session id is only serialized on the 26.2 era; the codec ignores this field on earlier eras, so passing Guid.Empty is safe on every era.
        await SendAsync(connection, descriptor, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(profile.Id, profile.Name, ToProperties(profile), Guid.Empty), ct)
            .ConfigureAwait(false);

        if (!hasConfigPhase)
        {
            connection.SetPhase(ProtocolPhase.Play);
            return new ServerLoginResult(profile, onlineMode, ProtocolPhase.Play);
        }

        // Await login-acknowledged, then drive configuration.
        await ReceiveAsync<ServerboundLoginAcknowledgedPacket>(
            connection, descriptor, ProtocolPhase.Login, Identifier.Minecraft("login_acknowledged"), ct).ConfigureAwait(false);
        connection.SetPhase(ProtocolPhase.Configuration);

        await RunConfigurationPhaseAsync(connection, descriptor, options, ct).ConfigureAwait(false);
        return new ServerLoginResult(profile, onlineMode, ProtocolPhase.Play);
    }

    private static async Task<GameProfile> RunOnlineAuthAsync(
        JavaConnection connection, ProtocolDescriptor descriptor, JavaServerLoginOptions options, string username, CancellationToken ct)
    {
        byte[] verifyToken = RandomNumberGenerator.GetBytes(4);
        byte[] publicKey = options.KeyPair.ExportSubjectPublicKeyInfo();

        // shouldAuthenticate is true for a real online-mode server (1.20.5+ carries the flag on the wire).
        await SendAsync(connection, descriptor, ProtocolPhase.Login,
            new ClientboundHelloPacket(string.Empty, publicKey, verifyToken, ShouldAuthenticate: true), ct).ConfigureAwait(false);

        ServerboundKeyPacket key = await ReceiveAsync<ServerboundKeyPacket>(
            connection, descriptor, ProtocolPhase.Login, Identifier.Minecraft("key"), ct).ConfigureAwait(false);

        // Validate the verify token (RSA-decrypt and compare) before touching the shared secret.
        byte[] decryptedToken = options.KeyPair.Decrypt(key.VerifyToken, RSAEncryptionPadding.Pkcs1);
        if (!CryptographicOperations.FixedTimeEquals(decryptedToken, verifyToken))
            throw new ProtocolViolationException("Encryption response verify token did not match.");

        byte[] sharedSecret = options.KeyPair.Decrypt(key.SharedSecret, RSAEncryptionPadding.Pkcs1);
        string serverIdHash = MinecraftServerId.Compute(string.Empty, sharedSecret, publicKey);

        // Enable encryption first so the failure-path disconnect below is sent encrypted.
        connection.EnableEncryption(sharedSecret);

        GameProfile? verified = await options.Verifier!
            .VerifyJoinAsync(username, serverIdHash, options.ClientIp, ct).ConfigureAwait(false);
        if (verified is null)
        {
            await DisconnectAsync(connection, descriptor,
                Umpk.Text.Component.Text("Failed to verify username."), ct).ConfigureAwait(false);
            throw new ConnectionClosedException(CloseReason.ProtocolViolation, "Session verification failed.");
        }

        return verified;
    }

    private static async Task RunConfigurationPhaseAsync(
        JavaConnection connection, ProtocolDescriptor descriptor, JavaServerLoginOptions options, CancellationToken ct)
    {
        PhaseRegistry configOut = descriptor.GetRegistry(ProtocolPhase.Configuration, PacketFlow.Clientbound);
        bool hasSelectKnownPacks = WireIdOf(configOut, Identifier.Minecraft("select_known_packs")) >= 0;

        // Offer known packs and let the client select before streaming registries. We still emit registry data regardless so the client has registries.
        if (hasSelectKnownPacks)
        {
            await SendAsync(connection, descriptor, ProtocolPhase.Configuration,
                new ClientboundSelectKnownPacksPacket(options.KnownPacks), ct).ConfigureAwait(false);
            await ReceiveAsync<ServerboundSelectKnownPacksPacket>(
                connection, descriptor, ProtocolPhase.Configuration, Identifier.Minecraft("select_known_packs"), ct)
                .ConfigureAwait(false);
        }

        foreach (ClientboundConfigRegistryDataPacket registry in options.RegistryData)
            await SendAsync(connection, descriptor, ProtocolPhase.Configuration, registry, ct).ConfigureAwait(false);

        // Finish configuration, then await the client's acknowledgment. The inbound read loop must stay in the configuration phase so the client's serverbound finish_configuration reply decodes with the configuration table; only after receiving it (the read loop parks at the terminal packet) do we acknowledge the transition to play, which also releases the parked gate.
        await SendAsync(connection, descriptor, ProtocolPhase.Configuration,
            new ClientboundFinishConfigurationPacket(), ct).ConfigureAwait(false);
        await ReceiveAsync<ServerboundFinishConfigurationPacket>(
            connection, descriptor, ProtocolPhase.Configuration, Identifier.Minecraft("finish_configuration"), ct)
            .ConfigureAwait(false);
        connection.SetPhase(ProtocolPhase.Play);
    }

    private static async Task DisconnectAsync(
        JavaConnection connection, ProtocolDescriptor descriptor, Umpk.Text.Component reason, CancellationToken ct)
    {
        // Login-phase disconnect (encrypted, since encryption is already enabled on the online failure path).
        try
        {
            await SendAsync(connection, descriptor, ProtocolPhase.Login,
                new ClientboundLoginDisconnectPacket(reason), ct).ConfigureAwait(false);
        }
        catch (ProtocolViolationException)
        {
            // No login-disconnect codec on this version's table; the caller still closes the connection.
        }
    }

    private static IReadOnlyList<GameProfileProperty> ToProperties(GameProfile profile)
    {
        if (profile.Properties.Count == 0)
            return [];

        var list = new GameProfileProperty[profile.Properties.Count];
        for (int i = 0; i < profile.Properties.Count; i++)
        {
            ProfileProperty p = profile.Properties[i];
            list[i] = new GameProfileProperty(p.Name, p.Value, p.Signature);
        }

        return list;
    }

    private static Guid OfflineUuid(string username)
    {
        byte[] input = Encoding.UTF8.GetBytes("OfflinePlayer:" + username);
        byte[] hash = MD5.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        int a = (hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3];
        short b = (short)((hash[4] << 8) | hash[5]);
        short c = (short)((hash[6] << 8) | hash[7]);
        return new Guid(a, b, c, hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]);
    }

    private static async Task<TPacket> ReceiveAsync<TPacket>(
        JavaConnection connection, ProtocolDescriptor descriptor, ProtocolPhase phase, Identifier expected, CancellationToken ct)
        where TPacket : class, IPacket
    {
        PhaseRegistry inbound = descriptor.GetRegistry(phase, PacketFlow.Serverbound);
        int expectedId = WireIdOf(inbound, expected);

        await foreach (InboundFrame frame in connection.ReceiveFramesAsync(ct).ConfigureAwait(false))
        {
            if (frame.WireId != expectedId || expectedId < 0)
            {
                // Ignore anything the client sends out of turn during login/config (e.g. cookie responses, client information, plugin answers); the driver only advances on the awaited packet.
                continue;
            }

            if (!inbound.TryGetInbound(expectedId, out BoundPacketCodec entry) || !entry.IsImplemented)
                throw new ProtocolViolationException($"No implemented codec for {expected} in {phase}.");

            return (TPacket)entry.Decode(frame.Payload, PacketCodecContext.Registryless);
        }

        throw new ConnectionClosedException(CloseReason.SocketEof, $"Connection closed while awaiting {expected}.");
    }

    private static Task SendAsync<TPacket>(
        JavaConnection connection, ProtocolDescriptor descriptor, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket =>
        LoginDriverWire.SendAsync(connection, descriptor, phase, PacketFlow.Clientbound, packet, ct);

    private static int WireIdOf(PhaseRegistry registry, Identifier id) => LoginDriverWire.WireIdOf(registry, id);
}
