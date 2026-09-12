using System.Security.Cryptography;
using System.Text;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>Byte-exact conformance for the v1 (1.19, protocol 759) signed-message body, which is the only signing generation that signs a JSON ENCODING of the message rather than its plain UTF-8 bytes.</summary>
/// <remarks>
/// <para>Vanilla builds the v1 payload as a fixed 32-byte big-endian header (salt, sender uuid MSB, sender uuid LSB, timestamp epoch SECONDS) followed by stable component JSON for the literal message in UTF-8. The stable writer leaves HTML-safe escaping disabled.</para>
/// <para>The stable form writes many legal BMP and astral code points verbatim, including U+00A0, the U+2000..U+200A space family, U+3000, U+007F, the C1 block, and U+E000..U+F8FF. Escaping any of them changes the signed bytes and causes the server to reject the message.</para>
/// <para>The expected values below are literal vectors, not produced by this encoder. They cover every well-formed BMP scalar, astral samples, and the mixed strings below.</para>
/// </remarks>
public sealed class SignedChatStableJsonConformanceTests
{
    /// <summary>The fixed 32-byte big-endian v1 header that precedes the encoded message.</summary>
    private const int V1HeaderBytes = 32;

    /// <summary>SHA-256 of the reference stable-JSON output for the whole corpus, concatenated in <c>Corpus</c> order and encoded UTF-8. Produced independently, not by this code.</summary>
    private const string VanillaCorpusSha256 =
        "d44690161f5ab5a03c439bee4c3fcaa3f0dcc0b3f4ae5807753799cd74cef2dc";

    /// <summary>Byte length of that same vanilla corpus. A second, independent shape check on the hash.</summary>
    private const int VanillaCorpusLength = 887125;

    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    /// <summary>Mixed strings covering every escape class. Written with C# escapes so this file stays ASCII.</summary>
    private static readonly string[] MixedCases =
    [
        "",
        "hello",
        "a\"b\\c",
        "tab\there",
        "nb\u00a0sp",
        "pua\ue000glyph",
        "\u2007\u200a",
        "\u3000wide",
        "emoji\U0001F600",
        "\u2028\u2029",
        "\u001f\u000b\u007f",
        "\u00a7section",
        "<html>&'=",
    ];

    /// <summary>The same mixed strings paired with their literal reference JSON. Read as a table: the left column is the chat text, the right is exactly what vanilla hashes.</summary>
    public static TheoryData<string, string> VanillaEncodings =>
        new()
        {
            { "", "{\"text\":\"\"}" },
            { "hello", "{\"text\":\"hello\"}" },
            { "a\"b\\c", "{\"text\":\"a\\\"b\\\\c\"}" },
            { "tab\there", "{\"text\":\"tab\\there\"}" },

            // The stable form writes each of these characters verbatim.
            { "nb\u00a0sp", "{\"text\":\"nb\u00a0sp\"}" },
            { "pua\ue000glyph", "{\"text\":\"pua\ue000glyph\"}" },
            { "\u2007\u200a", "{\"text\":\"\u2007\u200a\"}" },
            { "\u3000wide", "{\"text\":\"\u3000wide\"}" },
            { "emoji\U0001F600", "{\"text\":\"emoji\U0001F600\"}" },
            { "\u00a7section", "{\"text\":\"\u00a7section\"}" },
            { "<html>&'=", "{\"text\":\"<html>&'=\"}" },

            // The two Unicode line separators are escaped whatever htmlSafe says. C0 controls take the LOWERCASE \u00xx form, and U+007F is not in that table so it stays verbatim.
            { "\u2028\u2029", "{\"text\":\"\\u2028\\u2029\"}" },
            { "\u001f\u000b\u007f", "{\"text\":\"\\u001f\\u000b\u007f\"}" },
        };

    /// <summary>The whole-BMP pin. Anything that changes the encoding of any single code point moves the hash, which is what makes this catch a swapped escaper rather than only the characters someone thought to enumerate.</summary>
    [Fact]
    public void V1SignedBody_EncodesTheMessage_ExactlyAsVanillaGson()
    {
        byte[] corpus = Corpus();

        Assert.Equal(VanillaCorpusLength, corpus.Length);
        Assert.Equal(VanillaCorpusSha256, Convert.ToHexString(SHA256.HashData(corpus)).ToLowerInvariant());
    }

    /// <summary>The readable half of the same pin, one case at a time so a failure names the character.</summary>
    [Theory]
    [MemberData(nameof(VanillaEncodings))]
    public void V1SignedBody_MatchesVanillaGson_PerCase(string message, string expectedJson)
    {
        Assert.Equal(Encoding.UTF8.GetBytes(expectedJson), EncodedMessage(message));
    }

    /// <summary>The header the encoded message sits behind, pinned so the corpus slice is taken at the right offset and the whole-corpus hash cannot be passing over the wrong bytes. Salt, then the sender uuid big-endian MSB-then-LSB, then the timestamp in epoch SECONDS even though the packet field is millis.</summary>
    [Fact]
    public void V1SignedBody_HeaderIsThe32ByteBigEndianPrefix()
    {
        byte[] payload = new ChatSigningSession(Sender, Guid.Empty)
            .BuildPayload(ChatSignatureEra.V1_19, "x", Timestamp, salt: 0x0102030405060708L, []);

        // {"text":"x"} is 12 bytes.
        Assert.Equal(V1HeaderBytes + 12, payload.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, payload[..8]);
        Assert.Equal(Sender.ToByteArray(bigEndian: true), payload[8..24]);

        long seconds = Timestamp.ToUnixTimeSeconds();
        var expectedTime = new byte[8];
        for (int i = 0; i < 8; i++)
            expectedTime[i] = (byte)(seconds >> (56 - (8 * i)));

        Assert.Equal(expectedTime, payload[24..32]);
    }

    /// <summary>These characters must remain unchanged in the signed bytes, with no backslash escape in the encoding.</summary>
    [Theory]
    [InlineData('\u00a0')]
    [InlineData('\ue000')]
    [InlineData('\u2007')]
    [InlineData('\u3000')]
    [InlineData('\u007f')]
    [InlineData('\u0085')]
    public void V1SignedBody_DoesNotEscapeCharactersVanillaLeavesVerbatim(char c)
    {
        string json = Encoding.UTF8.GetString(EncodedMessage("a" + c + "b"));

        Assert.DoesNotContain("\\", json, StringComparison.Ordinal);
        Assert.Equal("{\"text\":\"a" + c + "b\"}", json);
    }

    /// <summary>The JSON encoding is era-LOCAL. v3 signs the plain UTF-8 message, so the raw text must appear in its payload and no component encoding may. Without this, moving the escaping rule could silently leak into the generation that is the live critical path.</summary>
    [Fact]
    public void V3SignedBody_CarriesThePlainMessage_NotJson()
    {
        const string Message = "quote\"and\\slash";
        byte[] payload = new ChatSigningSession(Sender, Guid.Empty)
            .BuildPayload(ChatSignatureEra.V1_19_3, Message, Timestamp, salt: 7, []);

        Assert.True(
            IndexOf(payload, Encoding.UTF8.GetBytes(Message)) >= 0,
            "the v3 payload must contain the plain UTF-8 message, not a JSON encoding of it");
        Assert.True(
            IndexOf(payload, Encoding.UTF8.GetBytes("{\"text\":")) < 0,
            "the v3 payload must not contain a JSON component encoding");
    }

    /// <summary>v2 signs a HEADER, not the message: <c>[precedingSignature] || senderUuid || sha256(body)</c>, so the message never appears in the signed payload at all and neither does any encoding of it.</summary>
    /// <remarks>The signed header updates with the previous signature when present, then the sender UUID, then the 32-byte SHA-256 body digest. The signing chain stores each produced signature as the next message's previous one, which is the chain link the growth assertion below pins.</remarks>
    [Fact]
    public void V2SignedBody_IsTheHeaderOverTheBodyDigest()
    {
        const int UuidBytes = 16;
        const int Sha256Bytes = 32;
        const string Message = "quote\"and\\slash";

        var session = new ChatSigningSession(Sender, Guid.Empty);
        byte[] first = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Timestamp, salt: 7, []);

        Assert.Equal(UuidBytes + Sha256Bytes, first.Length);
        Assert.Equal(Sender.ToByteArray(bigEndian: true), first[..UuidBytes]);
        Assert.True(
            IndexOf(first, Encoding.UTF8.GetBytes(Message)) < 0,
            "the v2 payload hashes the body, so the message must not appear in it");
        Assert.True(
            IndexOf(first, Encoding.UTF8.GetBytes("{\"text\":")) < 0,
            "the v2 payload must not contain a JSON component encoding");

        // Only the digest moves when the message does: the uuid prefix is fixed.
        byte[] other = new ChatSigningSession(Sender, Guid.Empty)
            .BuildPayload(ChatSignatureEra.V1_19_1, Message + "!", Timestamp, salt: 7, []);
        Assert.Equal(first[..UuidBytes], other[..UuidBytes]);
        Assert.NotEqual(first[UuidBytes..], other[UuidBytes..]);

        // Once the chain has a preceding signature it is prepended verbatim, exactly as vanilla's SignedMessageHeader does, so the payload grows by the signature length and starts with it.
        using RSA rsa = RSA.Create(2048);
        var certificates = new PlayerCertificates(
            rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem(), "sig", "sigv2",
            DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);

        byte[] signature = session.Sign(certificates, ChatSignatureEra.V1_19_1, Message, Timestamp, 7, []);
        byte[] chained = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Timestamp, salt: 7, []);

        Assert.Equal(signature.Length + UuidBytes + Sha256Bytes, chained.Length);
        Assert.Equal(signature, chained[..signature.Length]);
        Assert.Equal(first, chained[signature.Length..]);
    }

    /// <summary>The encoded message alone. <see cref="ChatSigningSession.BuildPayload"/> for the v1 era is the 32-byte header followed by exactly the UTF-8 of vanilla's stable JSON, so slicing the header off pins the production path rather than a test-only copy of it.</summary>
    private static byte[] EncodedMessage(string message) =>
        new ChatSigningSession(Sender, Guid.Empty)
            .BuildPayload(ChatSignatureEra.V1_19, message, Timestamp, salt: 0, [])[V1HeaderBytes..];

    /// <summary>The corpus, in the exact order the vanilla generator used: every well-formed BMP scalar (lone surrogates excluded, since they cannot come off a UTF-8 wire), then astral samples, then the mixed strings.</summary>
    private static byte[] Corpus()
    {
        var all = new List<byte>(VanillaCorpusLength);
        for (int c = 0x0000; c <= 0xFFFF; c++)
        {
            if (c is >= 0xD800 and <= 0xDFFF)
                continue;

            all.AddRange(EncodedMessage(((char)c).ToString()));
        }

        foreach (int codePoint in new[] { 0x10000, 0x1F600, 0x1F914, 0x2F800, 0x10FFFF })
            all.AddRange(EncodedMessage(char.ConvertFromUtf32(codePoint)));

        foreach (string mixed in MixedCases)
            all.AddRange(EncodedMessage(mixed));

        return [.. all];
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;

        return -1;
    }
}
