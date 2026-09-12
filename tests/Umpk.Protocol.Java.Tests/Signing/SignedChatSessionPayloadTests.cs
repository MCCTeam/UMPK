using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>The 1.19.1/1.19.2 (v2) signed payload over a non-empty last-seen window, pinned to fixed expected bytes.</summary>
/// <remarks>
/// <para>Unlike the hand-built vectors in <c>SignedChatPayloadLayoutTests</c>, this suite uses fixed reference digests and payload bytes to cover a non-empty last-seen window.</para>
/// <para>The signed-body and signed-header byte sequence is:</para>
/// <code>
/// Body digest input: salt long, timestamp in epoch seconds, plain content, byte 70, optional stable decorated JSON, then each last-seen entry as byte 70, UUID MSB, UUID LSB, and signature bytes. Signature-header input: optional preceding signature, sender UUID bytes, then the body digest.
/// </code>
/// <para>The last line is the point: each entry contributes ITS OWN uuid. That is why the window entries have to keep the profile ids the wire gave them, and why substituting the chat message's sender for all of them produced a different digest and a signature mismatch on every non-empty window.</para>
/// </remarks>
public sealed class SignedChatSessionPayloadTests
{
    private static readonly Guid Sender = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid PeerA = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid PeerB = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly DateTimeOffset Ts = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private const long Salt = 0x0123456789ABCDEFL;
    private const string Message = "hello world";

    /// <summary>Reference signature-header output for the two-entry window, 48 bytes: no preceding signature, then the 16-byte sender uuid, then the 32-byte body digest.</summary>
    private const string VanillaPayloadTwoEntriesHex =
        "11111111222233334444555555555555" +
        "dc023fb7dfe5a122ecb2df69abcf6121b6526d27a49dc5e79cc453636a09a9cf";

    /// <summary>Reference body digest for the two-entry window, 32 bytes.</summary>
    private const string VanillaBodyDigestTwoEntriesHex =
        "dc023fb7dfe5a122ecb2df69abcf6121b6526d27a49dc5e79cc453636a09a9cf";

    /// <summary>Reference body digest for the same message with an EMPTY window.</summary>
    private const string VanillaBodyDigestEmptyHex =
        "085c510a5fb334d49cf1981e6330dd6df808d45a7c06bc5304f68f1460e7035f";

    private static byte[] Sig(int seed)
    {
        var bytes = new byte[256];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((i * 31) + seed);

        return bytes;
    }

    private static AcknowledgedMessage[] Window() =>
        [new AcknowledgedMessage(PeerA, Sig(0x10)), new AcknowledgedMessage(PeerB, Sig(0x20))];

    /// <summary>UMPK's v2 payload over a two-entry window is byte for byte what vanilla produced for the same inputs. The payload is 48 bytes because the chain has no preceding signature yet.</summary>
    [Fact]
    public void V2Payload_OverNonEmptyWindow_MatchesVanillaBytes()
    {
        byte[] expected = Convert.FromHexString(VanillaPayloadTwoEntriesHex);
        Assert.Equal(48, expected.Length);

        var session = new ChatSigningSession(Sender, Guid.Empty);
        byte[] actual = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, Window());

        Assert.Equal(expected, actual);

        // The trailing 32 bytes are the reference body digest for the same window.
        Assert.Equal(Convert.FromHexString(VanillaBodyDigestTwoEntriesHex), actual.AsSpan(16).ToArray());
    }

    /// <summary>The window is load bearing, and each entry's OWN uuid is the part that carries the load. Rebuilding the same window with the chat sender's uuid substituted for every entry, which is what the inbound verification path must not do, yields a payload the sender never produces. Without this assertion the test above could pass against a build that hashed any uuid at all.</summary>
    [Fact]
    public void V2Payload_SubstitutingTheSenderForEachEntry_DivergesFromVanilla()
    {
        byte[] vanilla = Convert.FromHexString(VanillaPayloadTwoEntriesHex);
        var session = new ChatSigningSession(Sender, Guid.Empty);

        AcknowledgedMessage[] substituted =
            [new AcknowledgedMessage(Sender, Sig(0x10)), new AcknowledgedMessage(Sender, Sig(0x20))];
        Assert.NotEqual(vanilla, session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, substituted));

        // Swapping the two entries' uuids over is also detectable, so the pairing is pinned, not just the set.
        AcknowledgedMessage[] swapped =
            [new AcknowledgedMessage(PeerB, Sig(0x10)), new AcknowledgedMessage(PeerA, Sig(0x20))];
        Assert.NotEqual(vanilla, session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, swapped));

        // And an empty window is a different payload again, which is the vanilla empty-window digest.
        byte[] empty = session.BuildPayload(ChatSignatureEra.V1_19_1, Message, Ts, Salt, []);
        Assert.Equal(Convert.FromHexString(VanillaBodyDigestEmptyHex), empty.AsSpan(16).ToArray());
        Assert.NotEqual(vanilla, empty);
    }

    /// <summary>v3 provably ignores the profile id: 1.19.3 onward has none in the structure at all, so its payload builder must produce identical bytes whatever uuids the window entries carry. This is the unreachability half of the model change, asserted rather than assumed.</summary>
    [Fact]
    public void V3Payload_IsUnchangedByTheWindowProfileIds()
    {
        var session = new ChatSigningSession(Sender, Guid.Parse("99990000-aaaa-bbbb-cccc-ddddeeeeffff"));

        byte[] named = session.BuildPayload(ChatSignatureEra.V1_19_3, Message, Ts, Salt, Window());
        byte[] blank = session.BuildPayload(
            ChatSignatureEra.V1_19_3, Message, Ts, Salt,
            [new AcknowledgedMessage(Guid.Empty, Sig(0x10)), new AcknowledgedMessage(Guid.Empty, Sig(0x20))]);

        Assert.Equal(named, blank);

        // And the signatures themselves still are in it: an empty window is a different payload.
        Assert.NotEqual(named, session.BuildPayload(ChatSignatureEra.V1_19_3, Message, Ts, Salt, []));
    }
}
