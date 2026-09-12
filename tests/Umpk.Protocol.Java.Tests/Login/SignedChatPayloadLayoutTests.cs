using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

// Signed-chat payload byte-layout vectors. Expected bytes are hand-built from the wire contract rather than UMPK's builders, so they independently pin the era-1 and era-2 layouts.
public class SignedChatPayloadLayoutTests
{
    private static readonly Guid Sender = Guid.Parse("11112222-3333-4444-5555-666677778888");
    private static readonly Guid Session = Guid.Parse("99990000-aaaa-bbbb-cccc-ddddeeeeffff");
    private static readonly DateTimeOffset Ts = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private const long Salt = 0x0102030405060708L;
    private const string Msg = "hello world";

    private static byte[] GuidBe(Guid g)
    {
        Span<byte> b = stackalloc byte[16];
        g.TryWriteBytes(b, bigEndian: true, out _);
        return b.ToArray();
    }

    private static byte[] LongBe(long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        return b.ToArray();
    }

    private static byte[] IntBe(int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        return b.ToArray();
    }

    // Protocol 759 writes salt, sender UUID halves, timestamp seconds, then stable JSON content.
    // byte[32] BE: putLong(salt); putLong(uuid.MSB); putLong(uuid.LSB); putLong(timestamp.epochSecond);
    // Header bytes followed by the stable JSON of the literal message in UTF-8. i.e. {"text":"hello world"}.
    [Fact]
    public void WireLayout1_V1_19_Payload_MatchesDecompiledLayout()
    {
        var expected = new List<byte>();
        expected.AddRange(LongBe(Salt));            // salt FIRST
        expected.AddRange(GuidBe(Sender));          // uuid MSB||LSB
        expected.AddRange(LongBe(Ts.ToUnixTimeSeconds())); // timestamp seconds
        expected.AddRange(Encoding.UTF8.GetBytes("{\"text\":\"hello world\"}")); // JSON, not plaintext

        var session = new ChatSigningSession(Sender, Session);
        byte[] actual = session.BuildPayload(
            ChatSignatureEra.V1_19, Msg, Ts, Salt, Array.Empty<AcknowledgedMessage>());

        Assert.Equal(expected.ToArray(), actual);
    }

    // Stable-JSON content encoding must escape JSON metacharacters.
    [Fact]
    public void WireLayout1_V1_19_Payload_EscapesQuotesInContent()
    {
        const string quoted = "say \"hi\"";
        var expected = new List<byte>();
        expected.AddRange(LongBe(Salt));
        expected.AddRange(GuidBe(Sender));
        expected.AddRange(LongBe(Ts.ToUnixTimeSeconds()));
        expected.AddRange(Encoding.UTF8.GetBytes("{\"text\":\"say \\\"hi\\\"\"}"));

        var session = new ChatSigningSession(Sender, Session);
        byte[] actual = session.BuildPayload(
            ChatSignatureEra.V1_19, quoted, Ts, Salt, Array.Empty<AcknowledgedMessage>());

        Assert.Equal(expected.ToArray(), actual);
    }

    // Protocol 760 signs a two-stage structure: body = salt || timestampSecBE || UTF8(msg) || 0x46 || (per entry: 0x46 || uuidBE || sig) digest = SHA256(body) signed = [precedingSignature] || senderUuidBE || digest For the first message precedingSignature is null => signed content is 16 + 32 = 48 bytes.
    [Fact]
    public void WireLayout2_V1_19_1_Payload_MatchesDecompiledLayout()
    {
        var body = new List<byte>();
        body.AddRange(LongBe(Salt));
        body.AddRange(LongBe(Ts.ToUnixTimeSeconds()));
        body.AddRange(Encoding.UTF8.GetBytes(Msg));
        body.Add(0x46);

        byte[] digest = SHA256.HashData(body.ToArray());

        var expected = new List<byte>();
        expected.AddRange(GuidBe(Sender)); // precedingSignature is null for first message
        expected.AddRange(digest);

        var session = new ChatSigningSession(Sender, Session);
        byte[] actual = session.BuildPayload(
            ChatSignatureEra.V1_19_1, Msg, Ts, Salt, Array.Empty<AcknowledgedMessage>());

        Assert.Equal(expected.ToArray(), actual);
    }

    // The preceding signature must be prepended once the chain has produced one, and the last-seen window must contribute (0x46 || uuid || sig) entries to the hashed body.
    [Fact]
    public void WireLayout2_V1_19_1_Payload_ChainsPrecedingSignatureAndLastSeen()
    {
        using RSA rsa = RSA.Create(2048);
        var certs = new PlayerCertificates(
            rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem(), "", "",
            DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);

        var lastSeenSig = new byte[256];
        lastSeenSig[0] = 0xAB;
        var seenSender = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffff00001111");
        var lastSeen = new List<AcknowledgedMessage> { new(seenSender, lastSeenSig) };

        var session = new ChatSigningSession(Sender, Session);
        // First message establishes the preceding signature.
        byte[] firstSig = session.Sign(certs, ChatSignatureEra.V1_19_1, "first", Ts, Salt, Array.Empty<AcknowledgedMessage>());

        // Second message: build the expected two-stage payload by hand with the recorded preceding sig.
        var body = new List<byte>();
        body.AddRange(LongBe(Salt));
        body.AddRange(LongBe(Ts.ToUnixTimeSeconds()));
        body.AddRange(Encoding.UTF8.GetBytes(Msg));
        body.Add(0x46);
        body.Add(0x46);
        body.AddRange(GuidBe(seenSender));
        body.AddRange(lastSeenSig);
        byte[] digest = SHA256.HashData(body.ToArray());

        var expected = new List<byte>();
        expected.AddRange(firstSig);         // preceding signature chained in
        expected.AddRange(GuidBe(Sender));
        expected.AddRange(digest);

        byte[] actual = session.BuildPayload(ChatSignatureEra.V1_19_1, Msg, Ts, Salt, lastSeen);
        Assert.Equal(expected.ToArray(), actual);
    }

    // Control: era 3 must remain byte-exact. Protocol 761 writes the message-link index and sender UUID before the signed body digest.
    [Fact]
    public void WireLayout3_V1_19_3_Payload_MatchesDecompiledLayout()
    {
        var expected = new List<byte>();
        expected.AddRange(IntBe(1));                 // header int 1
        expected.AddRange(GuidBe(Sender));           // link sender
        expected.AddRange(GuidBe(Session));          // link session
        expected.AddRange(IntBe(0));                 // message index
        expected.AddRange(LongBe(Salt));             // salt
        expected.AddRange(LongBe(Ts.ToUnixTimeSeconds())); // timestamp
        byte[] mb = Encoding.UTF8.GetBytes(Msg);
        expected.AddRange(IntBe(mb.Length));         // content length
        expected.AddRange(mb);                       // content
        expected.AddRange(IntBe(0));                 // last-seen count

        var session = new ChatSigningSession(Sender, Session);
        byte[] actual = session.BuildPayload(
            ChatSignatureEra.V1_19_3, Msg, Ts, Salt, Array.Empty<AcknowledgedMessage>());

        Assert.Equal(expected.ToArray(), actual);
    }
}
