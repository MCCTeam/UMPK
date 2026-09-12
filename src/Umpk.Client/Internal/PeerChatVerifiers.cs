using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Umpk.Game.Players;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;

namespace Umpk.Client.Internal;

/// <summary>Resolves the per-peer <see cref="SignedChatVerifier"/> the chat applier verifies inbound <c>player_chat</c> signatures with. This is the source of the peer public keys, which arrive with the player-info roster: the 1.19.3+ <c>INITIALIZE_CHAT</c> action carries a <see cref="RemoteChatSession"/> (session id + key expiry + DER key + Mojang signature), and the 759/760 legacy <c>ADD_PLAYER</c> action carries the same key without a session id (<see cref="ProfilePublicKeyData"/>). Both land on <see cref="TabListEntry.ChatSession"/>.</summary>
/// <remarks>
/// <para>One verifier per peer, cached, because the verifier owns that peer's CHAIN state (message index, preceding signature, last accepted timestamp). The cache entry is rebuilt when the peer announces a different key or session and dropped when the peer leaves the roster, so a rejoin starts a fresh chain rather than inheriting a broken one.</para>
/// <para>UMPK has no trusted Mojang root for validating the certificate signature. It checks key expiry, DER shape, and signature presence, then fully verifies each message against the announced key. A hostile server could therefore substitute its own key; verification standing remains advisory and never suppresses delivery.</para>
/// <para>Not thread safe; it is driven from the session loop, like every applier.</para>
/// </remarks>
internal sealed class PeerChatVerifiers
{
    private readonly ClientState _state;
    private readonly ChatSignatureEra _era;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Dictionary<Guid, CachedVerifier> _cache = [];

    internal PeerChatVerifiers(ClientState state, ChatSignatureEra era, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _state = state;
        _era = era;
        _time = time;
        _logger = logger;
    }

    /// <summary>Builds the resolver for a session, or null when the negotiated version has no chat signing at all (protocols 47-758), where no peer ever announces a key and every inbound message is insecure.</summary>
    /// <remarks>The single construction site for the resolver: both <c>UmpkClient</c> and the applier test harness call this, so the seam cannot be wired in one and left null in the other.</remarks>
    public static Func<Guid, SignedChatVerifier?>? CreateResolver(
        ClientState state, string chatSigningFeature, TimeProvider time, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(chatSigningFeature);
        return ChatSigningEras.TryFromFeature(chatSigningFeature, out ChatSignatureEra era)
            ? new PeerChatVerifiers(state, era, time, logger).Resolve
            : null;
    }

    /// <summary>The verifier for a sender, or null when the roster holds no usable key for them (no entry, no announced chat session, an expired key, or a key that does not parse).</summary>
    public SignedChatVerifier? Resolve(Guid sender)
    {
        if (!_state.TabList.TryGet(sender, out TabListEntry? entry)
            || !TryReadAnnouncedKey(entry.ChatSession, out AnnouncedKey announced))
        {
            _cache.Remove(sender);
            return null;
        }

        if (_cache.TryGetValue(sender, out CachedVerifier cached) && cached.Announced.Matches(announced))
            return cached.Verifier;

        SignedChatVerifier? verifier = Build(sender, announced);
        if (verifier is null)
        {
            _cache.Remove(sender);
            return null;
        }

        _cache[sender] = new CachedVerifier(announced, verifier);
        return verifier;
    }

    private static bool TryReadAnnouncedKey(object? chatSession, out AnnouncedKey announced)
    {
        switch (chatSession)
        {
            // 1.19.3+: the INITIALIZE_CHAT payload, which binds the key to a chat session id. The session id is part of the v3 signed payload, so it must ride into the verifier.
            case RemoteChatSession session:
                announced = new AnnouncedKey(session.SessionId, session.ExpiresAtMillis, session.PublicKey, session.KeySignature);
                return true;

            // 759/760: the legacy add-player profile key. There is no chat session on those protocols;
            // the v1 and v2 signed payloads do not carry one, so the empty uuid is correct rather than a placeholder.
            case ProfilePublicKeyData key:
                announced = new AnnouncedKey(Guid.Empty, key.ExpiresAtMillis, key.KeyDer, key.KeySignature);
                return true;

            default:
                announced = default;
                return false;
        }
    }

    private SignedChatVerifier? Build(Guid sender, AnnouncedKey announced)
    {
        DateTimeOffset expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(announced.ExpiresAtMillis);
        if (expiresAt <= _time.GetUtcNow())
        {
            _logger.LogDebug(
                "Peer {Sender} announced a chat key that expired at {ExpiresAt}; inbound chat from them stays unverified.",
                sender, expiresAt);
            return null;
        }

        // A missing Mojang signature makes the announced key unusable even when the trust root is absent.
        if (announced.KeySignature.Length == 0)
        {
            _logger.LogDebug(
                "Peer {Sender} announced a chat key with no Mojang signature; inbound chat from them stays unverified.", sender);
            return null;
        }

        string pem;
        try
        {
            pem = ToPublicKeyPem(announced.KeyDer);
        }
        catch (CryptographicException ex)
        {
            _logger.LogDebug(
                ex, "Peer {Sender} announced a chat key that is not a readable RSA SubjectPublicKeyInfo.", sender);
            return null;
        }

        return new SignedChatVerifier(_era, pem, expiresAt, sender, announced.SessionId);
    }

    // The wire carries the key as DER SubjectPublicKeyInfo; the signing library takes PEM. Importing it here doubles as the shape check: a blob that is not an RSA SPKI throws instead of producing a verifier that would fail every message for the wrong reason.
    private static string ToPublicKeyPem(byte[] der)
    {
        using RSA rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(der, out _);
        return rsa.ExportSubjectPublicKeyInfoPem();
    }

    /// <summary>The peer key fields as announced on the roster, used both to build and to invalidate.</summary>
    private readonly record struct AnnouncedKey(Guid SessionId, long ExpiresAtMillis, byte[] KeyDer, byte[] KeySignature)
    {
        /// <summary>Whether a freshly announced key is the same one a cached verifier was built from. Compared by VALUE: the roster hands out new arrays on every packet, so reference equality would rebuild the verifier (and reset the peer's chain) on every player-info update.</summary>
        public bool Matches(AnnouncedKey other) =>
            SessionId == other.SessionId
            && ExpiresAtMillis == other.ExpiresAtMillis
            && KeyDer.AsSpan().SequenceEqual(other.KeyDer)
            && KeySignature.AsSpan().SequenceEqual(other.KeySignature);
    }

    private readonly record struct CachedVerifier(AnnouncedKey Announced, SignedChatVerifier Verifier);
}
