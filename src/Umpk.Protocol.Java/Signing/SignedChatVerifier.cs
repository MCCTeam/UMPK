using System.Security.Cryptography;

namespace Umpk.Protocol.Java.Signing;

/// <summary>The outcome of verifying one inbound signed message against the chain. Each violation records whether the connection should be dropped. <see cref="ChatVerificationStatus.Ok"/> is the only accepting outcome.</summary>
public enum ChatVerificationStatus
{
    /// <summary>The message verified against the peer key and advanced the chain.</summary>
    Ok = 0,

    /// <summary>The chain is exhausted or already broken (vanilla <c>chat.disabled.chain_broken</c>, no disconnect).</summary>
    ChainBroken = 1,

    /// <summary>The peer's profile key has expired (vanilla <c>chat.disabled.expiredProfileKey</c>, no disconnect).</summary>
    ExpiredProfileKey = 2,

    /// <summary>The message timestamp is before the last accepted one (vanilla <c>multiplayer.disconnect.out_of_order_chat</c>, disconnect).</summary>
    OutOfOrderChat = 3,

    /// <summary>The signature did not verify over the reconstructed payload (vanilla <c>multiplayer.disconnect.unsigned_chat</c>, disconnect).</summary>
    UnsignedChat = 4,
}

/// <summary>The typed result of a single <see cref="SignedChatVerifier.Verify"/> call: the status and whether the host should disconnect. The <see cref="ReasonKey"/> is the vanilla translation key for the violation, carried for structured logging (not a user-facing string; the host renders its own message).</summary>
/// <param name="Status">The verification status.</param>
/// <param name="ShouldDisconnect">True when vanilla would drop the connection for this violation.</param>
/// <param name="ReasonKey">The vanilla translation key describing the violation, or null on success.</param>
public readonly record struct ChatVerificationOutcome(
    ChatVerificationStatus Status, bool ShouldDisconnect, string? ReasonKey)
{
    /// <summary>True only when the message verified (<see cref="ChatVerificationStatus.Ok"/>).</summary>
    public bool IsValid => Status == ChatVerificationStatus.Ok;

    /// <summary>The accepting outcome.</summary>
    public static ChatVerificationOutcome Ok { get; } = new(ChatVerificationStatus.Ok, false, null);
}

/// <summary>The decode-side chain validator for inbound signed chat. One instance tracks a single peer's chain: it reconstructs each message's signed payload (via the same era layout <see cref="ChatSigningSession"/> produces on the signing side), checks it against the peer's public key, and enforces the four vanilla violation outcomes (chain-broken, expired-key, out-of-order timestamp, signature mismatch). The message index (1.19.3+ link), preceding signature (1.19.1/2 chain), and last-accepted timestamp are the mutable chain state and live on this instance, so it is not thread safe: drive it from the connection's read loop.</summary>
public sealed class SignedChatVerifier
{
    private readonly ChatSignatureEra _era;

    private readonly string _publicKeyPem;

    private readonly DateTimeOffset _keyExpiresAt;

    private readonly Guid _sender;

    private readonly Guid _sessionId;

    private int _nextIndex;

    private DateTimeOffset _lastTimestamp = DateTimeOffset.MinValue;

    private byte[]? _lastSignature;

    private bool _broken;

    /// <summary>Creates a verifier for one peer's signed-chat chain.</summary>
    /// <param name="era">The signature payload era to reconstruct (selected by the peer's version).</param>
    /// <param name="publicKeyPem">The peer's profile public key in PEM form.</param>
    /// <param name="keyExpiresAt">The peer profile key's expiry; messages after it are rejected.</param>
    /// <param name="sender">The peer's profile id (the 1.19.3+ link root sender).</param>
    /// <param name="sessionId">The peer's chat session id (the 1.19.3+ link root session).</param>
    public SignedChatVerifier(
        ChatSignatureEra era, string publicKeyPem, DateTimeOffset keyExpiresAt, Guid sender, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(publicKeyPem);
        _era = era;
        _publicKeyPem = publicKeyPem;
        _keyExpiresAt = keyExpiresAt;
        _sender = sender;
        _sessionId = sessionId;
    }

    /// <summary>The index the next in-order message is expected to carry (1.19.3+ link).</summary>
    public int NextIndex => _nextIndex;

    /// <summary>True once a violation has broken the chain; every later message returns <see cref="ChatVerificationStatus.ChainBroken"/>.</summary>
    public bool IsBroken => _broken;

    /// <summary>Verifies the next inbound message and advances the chain on success. The checks run in vanilla order: chain-broken, then key expiry, then timestamp ordering, then signature. A signature failure or an out-of-order timestamp breaks the chain (vanilla disconnects, ending it). On success the index, preceding signature, and last-accepted timestamp advance.</summary>
    /// <param name="message">The message content that was signed.</param>
    /// <param name="timestamp">The message timestamp.</param>
    /// <param name="salt">The per-message salt.</param>
    /// <param name="lastSeen">The ordered last-seen window the message acknowledged.</param>
    /// <param name="signature">The signature bytes carried by the message.</param>
    /// <param name="precedingSignature">The preceding-message signature the MESSAGE ITSELF announced, from the 1.19.1/1.19.2 signed header. Null on the first message of a chain, and null on every other era: 1.19.0 has no chain at all and 1.19.3+ carries continuity as the message index instead.</param>
    /// <param name="now">The current time, used for the key-expiry check.</param>
    /// <param name="decoratedJson">The 1.19.1/1.19.2 decorated component as the RAW JSON the frame carried, or null when the frame carried none. Its stable JSON is folded into the body digest between the 0x46 separator and the last-seen entries, so without it every message from a server that formats chat reconstructs a different body, fails the signature, and breaks that peer's chain permanently. Ignored on 1.19 and 1.19.3+, whose signed bodies have no decorated slot.</param>
    public ChatVerificationOutcome Verify(
        string message, DateTimeOffset timestamp, long salt,
        IReadOnlyList<AcknowledgedMessage> lastSeen, byte[] signature, byte[]? precedingSignature,
        DateTimeOffset now, string? decoratedJson = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(lastSeen);
        ArgumentNullException.ThrowIfNull(signature);

        if (_broken || _nextIndex == int.MaxValue)
            return new ChatVerificationOutcome(
                ChatVerificationStatus.ChainBroken, false, "chat.disabled.chain_broken");

        if (_keyExpiresAt <= now)
            return new ChatVerificationOutcome(
                ChatVerificationStatus.ExpiredProfileKey, false, "chat.disabled.expiredProfileKey");

        if (timestamp < _lastTimestamp)
        {
            _broken = true;
            return new ChatVerificationOutcome(
                ChatVerificationStatus.OutOfOrderChat, true, "multiplayer.disconnect.out_of_order_chat");
        }

        // 1.19.1/1.19.2 chain continuity: accept while this side has accepted nothing yet, otherwise require the ANNOUNCED preceding signature to be the one we last accepted.
        //
        // This is a separate check from the payload precisely because the payload is rebuilt from the announced value. The peer signs the header it sent, including that header's previous signature. Reconstructing the header from our own tracked signature instead is wrong the moment the two differ, and they differ for every peer whose chain started before this session joined. Rebuilding from a null preceding signature yields a 48-byte payload instead of the peer's 304-byte payload, invalidating the signature and the remainder of that peer's chain.
        if (_era == ChatSignatureEra.V1_19_1
            && _lastSignature is not null
            && !_lastSignature.AsSpan().SequenceEqual(precedingSignature ?? []))
        {
            _broken = true;
            return new ChatVerificationOutcome(
                ChatVerificationStatus.ChainBroken, false, "chat.disabled.chain_broken");
        }

        var context = new ChatVerificationContext(
            _sender, _sessionId, _nextIndex, message, timestamp, salt, lastSeen, precedingSignature,
            decoratedJson);
        bool valid;
        try
        {
            valid = ChatSigningSession.Verify(_publicKeyPem, signature, _era, context);
        }
        catch (CryptographicException)
        {
            valid = false;
        }

        if (!valid)
        {
            _broken = true;
            return new ChatVerificationOutcome(
                ChatVerificationStatus.UnsignedChat, true, "multiplayer.disconnect.unsigned_chat");
        }

        _lastTimestamp = timestamp;
        _lastSignature = signature;
        _nextIndex++;
        return ChatVerificationOutcome.Ok;
    }
}
