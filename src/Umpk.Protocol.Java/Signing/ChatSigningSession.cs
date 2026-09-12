using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Umpk.Protocol.Java.Signing;

/// <summary>Selects which era's signed-chat payload layout a builder produces.</summary>
public enum ChatSignatureEra
{
    /// <summary>1.19.0: a fixed 32-byte big-endian header (salt, sender UUID MSB/LSB, timestamp epoch seconds) followed by the stable-JSON component encoding of the message.</summary>
    V1_19 = 0,

    /// <summary>1.19.1/1.19.2: a two-stage signature. The body (salt, timestamp, message, last-seen window) is hashed with SHA-256, then the header <c>[precedingSignature] || senderUuid || bodyDigest</c> is signed.</summary>
    V1_19_1 = 1,

    /// <summary>1.19.3+ (through the 1.21.5 v3 checksum era): the linked message body form: header int, sender/chat UUIDs, message index, salt, timestamp, length-prefixed message, and the ordered last-seen signatures. The 1.21.5 acknowledgment adds a checksum byte at the packet level; the signed payload is unchanged.</summary>
    V1_19_3 = 2,
}

/// <summary>The per-session chat-signing state machine. It owns the mutable chain state (message index and preceding signature) and the last-seen collector, builds the era-specific signature payload over a <see cref="PlayerCertificates"/> key pair, and signs it with RSA-SHA256. The verification side rebuilds the same payload from a peer's announced fields and checks it against the peer's public key.</summary>
public sealed class ChatSigningSession
{
    private readonly Guid _sender;

    private readonly Guid _sessionId;

    private int _messageIndex;

    private byte[]? _precedingSignature;

    /// <summary>Creates a signing session for a sender profile and chat session id.</summary>
    public ChatSigningSession(Guid sender, Guid sessionId)
    {
        _sender = sender;
        _sessionId = sessionId;
    }

    /// <summary>The number of messages signed so far in this session (the running message index).</summary>
    public int MessageIndex => _messageIndex;

    /// <summary>The last produced signature, chained into the next message on 1.19.1 era verification.</summary>
    public byte[]? PrecedingSignature => _precedingSignature;

    /// <summary>The sender profile id this session signs as.</summary>
    public Guid Sender => _sender;

    /// <summary>The chat session id (1.19.3+ message link).</summary>
    public Guid SessionId => _sessionId;

    /// <summary>Builds the signature payload for a message in the given era. <paramref name="lastSeen"/> is the ordered last-seen window (used by the 1.19.1 and 1.19.3 eras; ignored by 1.19.0). <paramref name="decoratedJson"/> is the 1.19.1/1.19.2 decorated component, as the raw JSON the wire carried; null on every other era and on an undecorated v2 message.</summary>
    public byte[] BuildPayload(
        ChatSignatureEra era, string message, DateTimeOffset timestamp, long salt,
        IReadOnlyList<AcknowledgedMessage> lastSeen, string? decoratedJson = null) =>
        era switch
        {
            ChatSignatureEra.V1_19 => BuildV1_19(message, timestamp, salt),
            ChatSignatureEra.V1_19_1 => BuildV1_19_1(message, timestamp, salt, lastSeen, decoratedJson),
            ChatSignatureEra.V1_19_3 => BuildV1_19_3(message, timestamp, salt, lastSeen),
            _ => throw new ArgumentOutOfRangeException(nameof(era)),
        };

    /// <summary>Signs a message: builds the era payload, produces an RSA-SHA256 signature over it, advances the message index, and records the signature as the preceding one for chaining.</summary>
    public byte[] Sign(
        PlayerCertificates certificates, ChatSignatureEra era, string message, DateTimeOffset timestamp, long salt,
        IReadOnlyList<AcknowledgedMessage> lastSeen, string? decoratedJson = null)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        byte[] payload = BuildPayload(era, message, timestamp, salt, lastSeen, decoratedJson);

        // Mojang mislabels the PEM armor (PKCS#8 body under an "RSA PRIVATE KEY" header), so RSA.ImportFromPem picks the PKCS#1 parser and throws. Import label-agnostically.
        using RSA rsa = RsaPemKeys.ImportPrivateKey(certificates.PrivateKeyPem);
        byte[] signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        _messageIndex++;
        _precedingSignature = signature;
        return signature;
    }

    /// <summary>Verifies a peer's signed message: rebuilds the era payload from the announced fields and checks the signature against the peer's public key. Pure (does not touch this session's chain state).</summary>
    public static bool Verify(
        string publicKeyPem, byte[] signature, ChatSignatureEra era, ChatVerificationContext context)
    {
        ArgumentNullException.ThrowIfNull(publicKeyPem);
        ArgumentNullException.ThrowIfNull(signature);

        byte[] payload = era switch
        {
            ChatSignatureEra.V1_19 => BuildV1_19Static(context.Sender, context.Message, context.Timestamp, context.Salt),
            ChatSignatureEra.V1_19_1 => BuildV1_19_1Static(
                context.Sender, context.PrecedingSignature, context.Message, context.Timestamp, context.Salt,
                context.LastSeen, context.DecoratedJson),
            ChatSignatureEra.V1_19_3 => BuildV1_19_3Static(
                context.Sender, context.SessionId, context.MessageIndex, context.Message, context.Timestamp, context.Salt, context.LastSeen),
            _ => throw new ArgumentOutOfRangeException(nameof(era)),
        };

        // Same mislabelled-armor problem as the signing key: the body is a SubjectPublicKeyInfo.
        using RSA rsa = RsaPemKeys.ImportPublicKey(publicKeyPem);
        return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    // Era payload builders

    private byte[] BuildV1_19(string message, DateTimeOffset timestamp, long salt) =>
        BuildV1_19Static(_sender, message, timestamp, salt);

    private static byte[] BuildV1_19Static(Guid sender, string message, DateTimeOffset timestamp, long salt)
    {
        // A fixed 32-byte big-endian header: salt (long) || uuid MSB (long) || uuid LSB (long) || timestamp epoch seconds (long), followed by the stable-JSON component encoding of the message as UTF-8: {"text":"<escaped>"}.
        var data = new List<byte>(32);
        WriteLongBe(data, salt);
        WriteGuid(data, sender);
        WriteLongBe(data, timestamp.ToUnixTimeSeconds());
        data.AddRange(Encoding.UTF8.GetBytes(EncodeStableJsonLiteral(message)));
        return data.ToArray();
    }

    private byte[] BuildV1_19_1(
        string message, DateTimeOffset timestamp, long salt, IReadOnlyList<AcknowledgedMessage> lastSeen,
        string? decoratedJson) =>
        BuildV1_19_1Static(_sender, _precedingSignature, message, timestamp, salt, lastSeen, decoratedJson);

    /// <summary>Builds the 1.19.1 body digest input: big-endian salt and epoch seconds, plain UTF-8 message, byte 0x46, optional stable decorated JSON, then each last-seen entry as byte 0x46, UUID most and least halves, and signature bytes. The decorated component sits between the 0x46 separator and the last-seen entries. Leaving it out is why every message from a server that formats chat failed verification.</summary>
    /// <remarks>A frame with no optional component is undecorated. A present component is decorated unless its canonical JSON is exactly <c>{"text": plain}</c>. This test preserves the signed-body rule without normalizing or reordering the supplied JSON.</remarks>
    private static byte[] BuildV1_19_1Body(
        string message, DateTimeOffset timestamp, long salt, IReadOnlyList<AcknowledgedMessage> lastSeen,
        string? decoratedJson)
    {
        var body = new List<byte>();
        WriteLongBe(body, salt);
        WriteLongBe(body, timestamp.ToUnixTimeSeconds());
        body.AddRange(Encoding.UTF8.GetBytes(message));
        body.Add(0x46);
        if (decoratedJson is not null
            && VanillaStableJson.TryCanonicalize(decoratedJson) is { } stable
            && !string.Equals(stable, VanillaStableJson.EncodeLiteral(message), StringComparison.Ordinal))
            body.AddRange(Encoding.UTF8.GetBytes(stable));

        foreach (AcknowledgedMessage entry in lastSeen)
        {
            body.Add(0x46);
            WriteGuid(body, entry.ProfileId);
            body.AddRange(entry.Signature);
        }

        return body.ToArray();
    }

    private static byte[] BuildV1_19_1Static(
        Guid sender, byte[]? precedingSignature, string message, DateTimeOffset timestamp, long salt,
        IReadOnlyList<AcknowledgedMessage> lastSeen, string? decoratedJson)
    {
        // 1.19.1/1.19.2 use a two-stage signature. The body is hashed with SHA-256, then the signed header is [precedingSignature] || senderUuid (big-endian) || bodyDigest. The preceding signature is the real chain link: it is null only for the first message.
        byte[] body = BuildV1_19_1Body(message, timestamp, salt, lastSeen, decoratedJson);
        byte[] digest = SHA256.HashData(body);

        var data = new List<byte>();
        if (precedingSignature is not null)
            data.AddRange(precedingSignature);

        WriteGuid(data, sender);
        data.AddRange(digest);
        return data.ToArray();
    }

    private byte[] BuildV1_19_3(string message, DateTimeOffset timestamp, long salt, IReadOnlyList<AcknowledgedMessage> lastSeen) =>
        BuildV1_19_3Static(_sender, _sessionId, _messageIndex, message, timestamp, salt, lastSeen);

    private static byte[] BuildV1_19_3Static(
        Guid sender, Guid sessionId, int messageIndex, string message, DateTimeOffset timestamp, long salt,
        IReadOnlyList<AcknowledgedMessage> lastSeen)
    {
        // 1.19.3+ SignedMessage#update: header int 1, sender uuid, session uuid, message index (be int), salt (be long), timestamp (be long), message length (be int) + UTF-8 message, last-seen count (be int) + ordered signatures.
        var data = new List<byte>();
        WriteIntBe(data, 1);
        WriteGuid(data, sender);
        WriteGuid(data, sessionId);
        WriteIntBe(data, messageIndex);
        WriteLongBe(data, salt);
        WriteLongBe(data, timestamp.ToUnixTimeSeconds());
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        WriteIntBe(data, messageBytes.Length);
        data.AddRange(messageBytes);
        WriteIntBe(data, lastSeen.Count);
        foreach (AcknowledgedMessage entry in lastSeen)
            data.AddRange(entry.Signature);

        return data.ToArray();
    }

    /// <summary>Produces the stable JSON form used to sign a literal message in protocol 759.</summary>
    /// <remarks>
    /// <para>A bare literal with no style and no siblings serializes to exactly <c>{"text": message}</c>. The writer keeps HTML-safe escaping disabled, so its escape rule is exactly: the two JSON-mandatory escapes, the five short control forms, LOWERCASE <c>\u00xx</c> for the remaining C0 controls, and <c>U+2028</c> / <c>U+2029</c>. Everything else is written verbatim.</para>
    /// <para><c>JsonEncodedText.Encode(..., JavaScriptEncoder.UnsafeRelaxedJsonEscaping)</c> is unsuitable because "unsafe relaxed" only relaxes the HTML-sensitive characters. It still escapes EVERY astral code point as a <c>\uD83D\uDE00</c> surrogate pair, so every emoji, plus 7883 further BMP code points that the protocol emits verbatim, among them U+00A0 NO-BREAK SPACE, the U+2000..U+200A space family, U+3000, U+007F, the C1 block and the entire 6400-code-point Private Use Area U+E000..U+F8FF. These characters are legal in chat, so on protocol 759 a different escape policy signs different bytes and the server drops the message without a client-visible error. Only v1 encodes the message this way; v2 and v3 sign the plain UTF-8 text.</para>
    /// </remarks>
    private static string EncodeStableJsonLiteral(string message) =>
        VanillaStableJson.EncodeLiteral(message);

    private static void WriteGuid(List<byte> data, Guid guid)
    {
        Span<byte> buffer = stackalloc byte[16];
        guid.TryWriteBytes(buffer, bigEndian: true, out _);
        data.AddRange(buffer.ToArray());
    }

    private static void WriteLongBe(List<byte> data, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        data.AddRange(buffer.ToArray());
    }

    private static void WriteIntBe(List<byte> data, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        data.AddRange(buffer.ToArray());
    }
}

/// <summary>The fields needed to reconstruct a peer's signed-chat payload for verification. Populated from the wire fields of the peer's chat packet plus the session/link fields the era requires.</summary>
/// <param name="Sender">The sender profile id that signed the message.</param>
/// <param name="SessionId">The chat session id (1.19.3+ message link).</param>
/// <param name="MessageIndex">The sender's running message index (1.19.3+ message link).</param>
/// <param name="Message">The message content that was signed.</param>
/// <param name="Timestamp">The message timestamp.</param>
/// <param name="Salt">The per-message salt.</param>
/// <param name="LastSeen">The ordered last-seen window carried by the 1.19.1 and 1.19.3 eras.</param>
/// <param name="PrecedingSignature">The sender's preceding message signature, chained into the 1.19.1/1.19.2 header; null for the first message in the chain and unused by the other eras.</param>
/// <param name="DecoratedJson">1.19.1/1.19.2 only: the raw JSON of the decorated component the frame carried beside the plain message, or null when the frame carried none. Its stable form is folded into the signed body, so a message from a server that formats chat cannot be verified without it. Null on 1.19 and on 1.19.3+, neither of which has a decorated slot in the signed body.</param>
public sealed record ChatVerificationContext(
    Guid Sender,
    Guid SessionId,
    int MessageIndex,
    string Message,
    DateTimeOffset Timestamp,
    long Salt,
    IReadOnlyList<AcknowledgedMessage> LastSeen,
    byte[]? PrecedingSignature = null,
    string? DecoratedJson = null);
