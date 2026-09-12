using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>The 1.19.1/1.19.2 (v2) signed body over a DECORATED message, pinned against bytes the real 1.19.2 server classes produced.</summary>
/// <remarks>
/// <para>The independently verified signed-body digest input is:</para>
/// <code>
/// salt as a long; timestamp in epoch seconds as a long; plain content as UTF-8; byte 70; stable decorated-component JSON when present; last-seen signature inputs.
/// </code>
/// <para>UMPK omitted the decorated branch entirely, so every message from a server that formats chat reconstructed a body the sender never signed, failed the signature, and (because a signature failure breaks the chain in <c>SignedChatVerifier</c>) left that peer permanently unverifiable for the rest of the session. Formatted chat is the normal case on any server with a chat plugin.</para>
/// <para>Every constant below was produced by an independent reference implementation over plain text "hello world", timestamp 1700000000, and salt 0x0123456789ABCDEF.</para>
/// <para>The <c>Wire</c> value in each row is what a receiving client actually reads off the socket and the <c>Stable</c> value is what gets hashed. They are two encodings of ONE JsonElement tree and they differ in key order, which is the whole reason a component MODEL cannot be used here and the raw JSON has to be.</para>
/// </remarks>
public sealed class DecoratedSignedChatBodyTests
{
    private static readonly Guid Sender = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid PeerA = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid PeerB = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly DateTimeOffset Ts = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private const long Salt = 0x0123456789ABCDEFL;
    private const string Message = "hello world";

    /// <summary>Reference digest with an UNDECORATED body and an empty window.</summary>
    private const string UndecoratedEmptyWindow =
        "085c510a5fb334d49cf1981e6330dd6df808d45a7c06bc5304f68f1460e7035f";

    /// <summary>Reference digest, undecorated, over the two-entry window below.</summary>
    private const string UndecoratedTwoEntryWindow =
        "dc023fb7dfe5a122ecb2df69abcf6121b6526d27a49dc5e79cc453636a09a9cf";

    /// <summary>The same two-entry window with the <see cref="SiblingWire"/> decoration folded in.</summary>
    private const string DecoratedTwoEntryWindow =
        "f22143104f2a7244d6981972d0379906cc77a0654e593e6c8f11218ec4e328d8";

    private const string SiblingWire =
        "{\"extra\":[{\"color\":\"gray\",\"text\":\"<Steve> \"},{\"text\":\"hello world\"}],\"text\":\"\"}";

    /// <summary>One vanilla row: the component as it rides the wire, the stable JSON vanilla hashes, and the body digest that produces over the empty window.</summary>
    public static TheoryData<string, string, string> VanillaRows() => new()
    {
        // A plain literal equal to the message is NOT decorated (isDecorated is a literal component of the plain text, so it hashes to the undecorated digest.
        {
            "{\"text\":\"hello world\"}",
            "{\"text\":\"hello world\"}",
            UndecoratedEmptyWindow
        },

        // The shape a chat-formatting server actually sends: a prefix sibling plus the message.
        {
            SiblingWire,
            SiblingWire,
            "6301287b2f55794ffd0fb9a122fa56ec483f54b611d7f3f9c5cfc54d63803061"
        },

        // A translate component with arguments.
        {
            "{\"translate\":\"chat.type.text\",\"with\":[{\"text\":\"Steve\"},{\"text\":\"hello world\"}]}",
            "{\"translate\":\"chat.type.text\",\"with\":[{\"text\":\"Steve\"},{\"text\":\"hello world\"}]}",
            "f2cb5fdb4ee70950ba54c10e19e16b687902e2d291a5d2817020ebbdabc434dc"
        },

        // KEY ORDER DISCRIMINATOR: the wire order is the serializer's insertion order and the hashed order is sorted. A build that hashed the wire text as-is fails this row and only this row.
        {
            "{\"bold\":true,\"italic\":false,\"color\":\"#AABBCC\",\"insertion\":\"x\",\"text\":\"a\"}",
            "{\"bold\":true,\"color\":\"#AABBCC\",\"insertion\":\"x\",\"italic\":false,\"text\":\"a\"}",
            "a0e85e8dd6307e42bbccc5779ae0a47e86a149254e5cd72578995c67ae051428"
        },

        // A named colour, already in sorted order on the wire.
        {
            "{\"bold\":true,\"color\":\"red\",\"text\":\"z\"}",
            "{\"bold\":true,\"color\":\"red\",\"text\":\"z\"}",
            "58a75abaadd0d95b55c3fd65e456ff83c47487be9f18c38ad01353427587ed95"
        },

        // HTML-SENSITIVE DISCRIMINATOR: '<', '>', '&' and '\'' are written VERBATIM. Other modes escape them as < etc. with HTML-safe escaping enabled; the stable form leaves that option off.
        {
            "{\"clickEvent\":{\"action\":\"open_url\",\"value\":\"https://example.com/?a=1&b=2\"},"
            + "\"hoverEvent\":{\"action\":\"show_text\",\"contents\":{\"text\":\"tip <b> & 'q'\"}},\"text\":\"q\"}",
            "{\"clickEvent\":{\"action\":\"open_url\",\"value\":\"https://example.com/?a=1&b=2\"},"
            + "\"hoverEvent\":{\"action\":\"show_text\",\"contents\":{\"text\":\"tip <b> & 'q'\"}},\"text\":\"q\"}",
            "30c2fc34a3f3b1bc4864adfa4cc247fde676095a2959770bdbca8c4eb0d41df7"
        },

        // U+2028 and U+2029 are escaped whatever htmlSafe says: they are legal in JSON but not in a JavaScript string literal, so the reference escape table always carries them.
        {
            "{\"text\":\"sep\\u2028and\\u2029\"}",
            "{\"text\":\"sep\\u2028and\\u2029\"}",
            "52aaad6b988798da9990ab2661e189f26cd4b98893286980d15698d4bc38cb1d"
        },

        // A nested object that is not a component.
        {
            "{\"score\":{\"name\":\"*\",\"objective\":\"obj\"}}",
            "{\"score\":{\"name\":\"*\",\"objective\":\"obj\"}}",
            "d4cd3bfd1ebf239cadf3582973ddcbeaf8d61a932caa1b566cd8e5abcae3201f"
        },

        // Siblings: vanilla's serializer has already normalised a bare-string sibling to an object by the time the component reaches the wire, so the wire form is canonical.
        {
            "{\"extra\":[{\"text\":\"bare string sibling\"}],\"text\":\"n\"}",
            "{\"extra\":[{\"text\":\"bare string sibling\"}],\"text\":\"n\"}",
            "7c08fa392e3a35cd869ce95cd499336d100078a67f3e7b4d65a9ef890ebf84f2"
        },

        {
            "{\"keybind\":\"key.jump\"}",
            "{\"keybind\":\"key.jump\"}",
            "df7040d8bc582c889c73f81c82218fd3d339a00fa43f573265f89b4a4cbfa509"
        },

        // SECOND KEY ORDER DISCRIMINATOR, with a boolean and a nested object in the mix.
        {
            "{\"nbt\":\"Inventory\",\"interpret\":true,\"separator\":{\"text\":\", \"},\"entity\":\"@s\"}",
            "{\"entity\":\"@s\",\"interpret\":true,\"nbt\":\"Inventory\",\"separator\":{\"text\":\", \"}}",
            "808098f11aca1cbbbed5f6cf7a366db32e4244b010c1180f81ac2591518292d1"
        },
    };

    /// <summary>The canonicaliser reproduces the stable JSON for each wire form, and the v2 payload built from that wire form carries vanilla's own body digest.</summary>
    [Theory]
    [MemberData(nameof(VanillaRows))]
    public void V2Body_WithDecoration_MatchesVanillaBytes(string wire, string stable, string digestHex)
    {
        Assert.Equal(stable, VanillaStableJson.TryCanonicalize(wire));

        var session = new ChatSigningSession(Sender, Guid.Empty);
        byte[] payload = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, [], wire);

        // No preceding signature yet, so the payload is the 16-byte sender uuid then the 32-byte digest.
        Assert.Equal(48, payload.Length);
        Assert.Equal(Convert.FromHexString(digestHex), payload.AsSpan(16).ToArray());
    }

    /// <summary>Dropping the decoration produces the undecorated digest for every decorated row, which is not the body the sender signed.</summary>
    [Theory]
    [MemberData(nameof(VanillaRows))]
    public void V2Body_DroppingTheDecoration_ReproducesTheOldWrongDigest(string wire, string stable, string digestHex)
    {
        var session = new ChatSigningSession(Sender, Guid.Empty);
        byte[] withoutDecoration = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, []);

        Assert.Equal(Convert.FromHexString(UndecoratedEmptyWindow), withoutDecoration.AsSpan(16).ToArray());

        // Only the literal equal to the plain message is undecorated. Derive that predicate from the canonical JSON, then compare it with the independently pinned digest for the row.
        bool isDecorated = !string.Equals(
            VanillaStableJson.EncodeLiteral(Message), stable, StringComparison.Ordinal);
        Assert.Equal(isDecorated, digestHex != UndecoratedEmptyWindow);

        byte[] withDecoration = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, [], wire);
        Assert.Equal(Convert.FromHexString(digestHex), withDecoration.AsSpan(16).ToArray());
        Assert.Equal(isDecorated, !withDecoration.SequenceEqual(withoutDecoration));
    }

    /// <summary>The decoration goes BETWEEN the 0x46 separator and the last-seen entries. A non-empty window is what makes that placement observable at all: with an empty window every ordering of a single trailing field produces the same bytes.</summary>
    [Fact]
    public void V2Body_DecorationPrecedesTheLastSeenEntries()
    {
        var session = new ChatSigningSession(Sender, Guid.Empty);

        byte[] undecorated = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, Window());
        Assert.Equal(Convert.FromHexString(UndecoratedTwoEntryWindow), undecorated.AsSpan(16).ToArray());

        byte[] decorated = session.BuildPayload(
            ChatSignatureEra.V1_19_1, Message, Ts, Salt, Window(), SiblingWire);
        Assert.Equal(Convert.FromHexString(DecoratedTwoEntryWindow), decorated.AsSpan(16).ToArray());

        Assert.NotEqual(undecorated, decorated);
    }

    /// <summary>A message is decorated when its component differs from a literal of the plain text. An absent optional component is treated as that literal. So a component that IS the bare literal must hash as undecorated even though it arrived as an explicit component, and any other component must not.</summary>
    [Fact]
    public void V2Body_ALiteralEqualToThePlainMessage_IsNotDecorated()
    {
        var session = new ChatSigningSession(Sender, Guid.Empty);
        byte[] absent = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, []);
        byte[] literal = session.BuildPayload(
            ChatSignatureEra.V1_19_1, Message, Ts, Salt, [], "{\"text\":\"hello world\"}");

        Assert.Equal(absent, literal);

        // The same literal spelled with a different key order, which the sorting collapses onto the same canonical form, is also undecorated. A build that compared the RAW text would fold this in.
        byte[] reordered = session.BuildPayload(
            ChatSignatureEra.V1_19_1, Message, Ts, Salt, [], "{ \"text\" : \"hello world\" }");
        Assert.Equal(absent, reordered);

        // A literal that differs from the plain message IS decorated.
        byte[] different = session.BuildPayload(
            ChatSignatureEra.V1_19_1, Message, Ts, Salt, [], "{\"text\":\"hello worlds\"}");
        Assert.NotEqual(absent, different);
    }

    /// <summary>A decoration that cannot be parsed is not guessed at: the canonicaliser returns null and the body is built without it, so the message fails verification instead of being verified against invented bytes.</summary>
    [Fact]
    public void V2Body_UnparseableDecoration_IsNotGuessedAt()
    {
        Assert.Null(VanillaStableJson.TryCanonicalize("{\"text\":"));
        Assert.Null(VanillaStableJson.TryCanonicalize("not json at all"));

        var session = new ChatSigningSession(Sender, Guid.Empty);
        byte[] absent = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, []);
        byte[] broken = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, [], "{\"text\":");
        Assert.Equal(absent, broken);
    }

    /// <summary>The other two eras have no decorated slot in their signed payloads, so the parameter must be inert there. 1.19 signs the stable JSON of the message literal and nothing else; 1.19.3+ signs the plain UTF-8 text.</summary>
    [Fact]
    public void OtherWireLayouts_IgnoreTheDecoration()
    {
        var session = new ChatSigningSession(Sender, Guid.Parse("99990000-aaaa-bbbb-cccc-ddddeeeeffff"));

        Assert.Equal(
            session.BuildPayload(ChatSignatureEra.V1_19, Message, Ts, Salt, []),
            session.BuildPayload(ChatSignatureEra.V1_19, Message, Ts, Salt, [], SiblingWire));

        Assert.Equal(
            session.BuildPayload(ChatSignatureEra.V1_19_3, Message, Ts, Salt, []),
            session.BuildPayload(ChatSignatureEra.V1_19_3, Message, Ts, Salt, [], SiblingWire));
    }

    /// <summary>A repeated key keeps the LAST value and one entry. A canonical frame never emits one, but a frame that carries one must preserve the last value rather than emitting the key twice.</summary>
    [Fact]
    public void StableJson_DuplicateKeys_KeepTheLastValue()
        => Assert.Equal(
            "{\"text\":\"b\"}",
            VanillaStableJson.TryCanonicalize("{\"text\":\"a\",\"text\":\"b\"}"));

    /// <summary>A bare JSON string becomes the object form <c>{"text":"just text"}</c> in stable JSON.</summary>
    [Fact]
    public void StableJson_BareString_LiftsToATextObject()
        => Assert.Equal(
            "{\"text\":\"just text\"}",
            VanillaStableJson.TryCanonicalize("\"just text\""));

    /// <summary>Numbers keep their wire lexeme.</summary>
    [Fact]
    public void StableJson_NumbersKeepTheirLexeme()
        => Assert.Equal(
            "{\"a\":1,\"b\":1.0,\"c\":-0.5,\"d\":1000000}",
            VanillaStableJson.TryCanonicalize("{\"d\":1000000,\"b\":1.0,\"a\":1,\"c\":-0.5}"));

    private static byte[] Sig(int seed)
    {
        var bytes = new byte[256];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((i * 31) + seed);

        return bytes;
    }

    private static AcknowledgedMessage[] Window() =>
        [new AcknowledgedMessage(PeerA, Sig(0x10)), new AcknowledgedMessage(PeerB, Sig(0x20))];
}
