using System.Buffers;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The chat filter mask has to change what a CONSUMER sees, not merely land in a field. These feed real <c>player_chat</c> frames carrying each of the three mask variants through the registrar-bound codec and the real applier chain, and assert on the published event.
/// <para>Asserting the decoded <c>FilterType</c> and <c>FilterBits</c> would prove nothing: both were already populated before this work, and the mask was then read by nobody, so a consumer saw the UNFILTERED text the server had told the client to hide. The assertions below are therefore about the displayed component and about the raw text being ABSENT from it.</para>
/// <para>The display rule has three outcomes, with <c>HASH = '#'</c> for filtered characters, (<c>FILTERED_STYLE</c>),  (<c>apply</c>) and  (<c>applyWithFormatting</c>).</para>
/// </summary>
public sealed class ChatFilterMaskApplicationTests
{
    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");

    /// <summary>26 characters. The mask below hides the six of "secret", which is what makes a leaked raw body obvious in an assertion rather than a subtle difference.</summary>
    private const string Content = "the secret word is banana!";

    /// <summary>Bits 4..9 set, i.e. the six characters of "secret" at indices 4-9 of <see cref="Content"/>. The wire uses a VarInt-counted array of longs, little-endian bit order within each long, so bits 4..9 are 0b11_1111 &lt;&lt; 4 = 0x3F0.</summary>
    private const long SecretMaskWord = 0x3F0L;

    private const string MaskedContent = "the ###### word is banana!";

    /// <summary>Both protocols that carry a filter mask AND a global index (770 = 1.21.5, 776 = 26.2).</summary>
    public static TheoryData<int> ModernVersions => [770, 776];

    /// <summary>PARTIALLY_FILTERED: the consumer must receive the masked text, and must NOT be able to recover the original. Body and Message must not both expose the raw <see cref="Content"/>.</summary>
    [Theory]
    [MemberData(nameof(ModernVersions))]
    public async Task PartiallyFilteredChat_ReachesTheConsumer_Masked(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        await harness.ApplyAsync(DecodePlayerChat(version!, BuildFrame(filterType: 2, filterWords: [SecretMaskWord])));

        Assert.NotNull(seen);
        Assert.Equal(ChatFilterMaskType.PartiallyFiltered, seen!.Filter);

        // The body the server told the client to mask, masked.
        Assert.Equal(MaskedContent, seen.Body.ToPlainText());
        Assert.Equal($"<Notch> {MaskedContent}", seen.Message.ToPlainText());

        // The event must not expose the unfiltered form.
        Assert.DoesNotContain("secret", seen.Body.ToPlainText(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", seen.Message.ToPlainText(), StringComparison.Ordinal);
    }

    /// <summary>The masked run carries vanilla's <c>FILTERED_STYLE</c>: dark grey with a <c>chat.filtered</c> tooltip. A plain string of hashes would hide the text but would also lie to a renderer about why.</summary>
    [Fact]
    public async Task PartiallyFilteredChat_MaskedRun_CarriesVanillaFilteredStyle()
    {
        var harness = new ApplierHarness(JavaVersions.V26_2);
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        await harness.ApplyAsync(
            DecodePlayerChat(JavaVersions.V26_2, BuildFrame(filterType: 2, filterWords: [SecretMaskWord])));

        Assert.NotNull(seen);
        Component masked = Assert.Single(
            seen!.Body.Children, c => c.Content is TextContent { Text: "######" });
        Assert.Equal(TextColor.DarkGray, masked.Style.Color);
        HoverShowText hover = Assert.IsType<HoverShowText>(masked.Style.HoverEvent);
        Assert.Equal("chat.filtered", Assert.IsType<TranslatableContent>(hover.Text.Content).Key);

        // The unmasked runs are plain and unstyled, so only the hidden part is marked.
        Assert.Contains(seen.Body.Children, c => c.Content is TextContent { Text: "the " } && c.Style.IsEmpty);
        Assert.Contains(seen.Body.Children, c => c.Content is TextContent { Text: " word is banana!" } && c.Style.IsEmpty);
    }

    /// <summary>A partial mask discards the server's unsigned override, because the mask's bit indices were computed against the signed characters. The partial-mask arm applies the filter to signed content and never touches the decorated-content override. Preferring the override in the partial-mask arm would have defeated the filter completely: a server that filters and decorates would have had its decorated, UNFILTERED text displayed.</summary>
    [Fact]
    public async Task PartiallyFilteredChat_IgnoresTheUnsignedOverride()
    {
        var harness = new ApplierHarness(JavaVersions.V26_2);
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        await harness.ApplyAsync(DecodePlayerChat(
            JavaVersions.V26_2,
            BuildFrame(filterType: 2, filterWords: [SecretMaskWord], unsignedOverride: "the secret word is banana! (decorated)")));

        Assert.NotNull(seen);
        Assert.Equal(MaskedContent, seen!.Body.ToPlainText());
        Assert.DoesNotContain("decorated", seen.Body.ToPlainText(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", seen.Body.ToPlainText(), StringComparison.Ordinal);
    }

    /// <summary>FULLY_FILTERED: vanilla renders nothing at all because fully filtered messages are not displayed. No <see cref="ChatMessageReceived"/> may be published, because the frame still carries the whole signed body and publishing it in any form hands over text the server withheld. A distinct <see cref="ChatMessageSuppressed"/> is published instead, so a consumer can tell a suppressed message from a message that was never sent.</summary>
    [Fact]
    public async Task FullyFilteredChat_PublishesNoMessage_ButIsStillObservable()
    {
        var harness = new ApplierHarness(JavaVersions.V26_2);
        var received = new List<ChatMessageReceived>();
        var suppressed = new List<ChatMessageSuppressed>();
        harness.Events.Subscribe<ChatMessageReceived>(received.Add);
        harness.Events.Subscribe<ChatMessageSuppressed>(suppressed.Add);

        await harness.ApplyAsync(DecodePlayerChat(JavaVersions.V26_2, BuildFrame(filterType: 1)));

        Assert.Empty(received);
        ChatMessageSuppressed only = Assert.Single(suppressed);
        Assert.Equal(Sender, only.SenderId);
        Assert.Equal(ChatFilterMaskType.FullyFiltered, only.Filter);
    }

    /// <summary>A message the client did not display advances the last-seen offset but is NOT acknowledged in the bitset. The tracker stores a pending signature only when the message was displayed. Acknowledging a suppressed message would tell the server the client had seen text it deliberately withheld.</summary>
    [Fact]
    public async Task FullyFilteredChat_AdvancesTheAckOffset_WithoutAcknowledgingTheSignature()
    {
        var harness = new ApplierHarness(JavaVersions.V26_2);
        await harness.ApplyAsync(DecodePlayerChat(JavaVersions.V26_2, BuildFrame(filterType: 1)));

        LastSeenMessagesUpdate update = harness.LastSeenTracker.Generate(out IReadOnlyList<byte[]> acknowledged);
        Assert.Equal(1, update.Offset);
        Assert.Empty(acknowledged);
        Assert.Equal(new byte[3], update.Acknowledged);
    }

    /// <summary>The displayed control: a pass-through message IS acknowledged, so the arm above is a real branch.</summary>
    [Fact]
    public async Task PassThroughChat_IsAcknowledged()
    {
        var harness = new ApplierHarness(JavaVersions.V26_2);
        await harness.ApplyAsync(DecodePlayerChat(JavaVersions.V26_2, BuildFrame(filterType: 0)));

        LastSeenMessagesUpdate update = harness.LastSeenTracker.Generate(out IReadOnlyList<byte[]> acknowledged);
        Assert.Equal(1, update.Offset);
        Assert.Single(acknowledged);
    }

    /// <summary>PASS_THROUGH is the unfiltered path and must be untouched: the full text, the unsigned override honoured, and the filter reported as pass-through.</summary>
    [Fact]
    public async Task PassThroughChat_IsUnchanged()
    {
        var harness = new ApplierHarness(JavaVersions.V26_2);
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        await harness.ApplyAsync(DecodePlayerChat(
            JavaVersions.V26_2, BuildFrame(filterType: 0, unsignedOverride: "decorated body")));

        Assert.NotNull(seen);
        Assert.Equal(ChatFilterMaskType.PassThrough, seen!.Filter);
        Assert.Equal("decorated body", seen.Body.ToPlainText());
    }

    // ChatFilterMask unit pins.

    /// <summary>Pass-through returns the text, fully filtered returns null, and partial replaces each set bit's character with '#'.</summary>
    [Fact]
    public void Apply_MatchesVanillaPerType()
    {
        Assert.Equal(Content, ChatFilterMask.PassThrough.Apply(Content));
        Assert.Null(ChatFilterMask.FullyFiltered.Apply(Content));
        Assert.Equal(MaskedContent, ChatFilterMask.Read(2, [SecretMaskWord]).Apply(Content));
    }

    /// <summary>A mask whose set bits run past the end of the string must not throw and must not pad: vanilla's loop stops at the shorter of the message and mask lengths.</summary>
    [Fact]
    public void Apply_StopsAtTheEndOfTheString()
    {
        ChatFilterMask mask = ChatFilterMask.Read(2, [-1L]);
        Assert.Equal("####", mask.Apply("abcd"));
    }

    /// <summary>An unknown mask type degrades to pass-through rather than throwing. Vanilla's <c>readEnum</c> would reject it, but a chat line must not be able to fault a session, and the codec has already consumed the frame by the time the value gets here.</summary>
    [Fact]
    public void Read_UnknownType_DegradesToPassThrough()
    {
        Assert.Equal(ChatFilterMaskType.PassThrough, ChatFilterMask.Read(7, []).Type);
        Assert.Equal(Content, ChatFilterMask.Read(7, []).Apply(Content));
    }

    /// <summary>The mask indexes UTF-16 code units. A surrogate pair therefore occupies two bits and masking it costs two hashes. Normalising to runes here would shift every index after the first astral character and hide the wrong text.</summary>
    [Fact]
    public void Apply_MasksUtf16CodeUnits_NotRunes()
    {
        // "a" + U+1F600 (two code units) + "b"; bits 1 and 2 are the surrogate pair.
        const string text = "a😀b";
        Assert.Equal("a##b", ChatFilterMask.Read(2, [0b110L]).Apply(text));
    }

    /// <summary><c>applyWithFormatting</c> run boundaries: a mask starting at index 0 opens with a masked run, and text past the last set bit is a final unmasked run.</summary>
    [Fact]
    public void ApplyWithFormatting_RunBoundaries()
    {
        Component? c = ChatFilterMask.Read(2, [0b0011L]).ApplyWithFormatting("abcd");
        Assert.NotNull(c);
        Assert.Equal("##cd", c!.ToPlainText());
        Assert.Equal(2, c.Children.Count);
        Assert.Equal(ChatFilterMask.FilteredStyle, c.Children[0].Style);
        Assert.True(c.Children[1].Style.IsEmpty);
    }

    /// <summary>Fully filtered has no displayable form at all, which is why the applier arm returns early.</summary>
    [Fact]
    public void ApplyWithFormatting_FullyFiltered_IsNull() =>
        Assert.Null(ChatFilterMask.FullyFiltered.ApplyWithFormatting(Content));

    /// <summary>Resolves the wire id and codec the registrar actually bound for <c>player_chat</c> on this version and decodes frame-exactly through it, exactly as <c>InboundPlayerChatDeliveryTests</c> does. Nothing here names a codec, so a marker or a misbound era fails on the spot.</summary>
    private static object DecodePlayerChat(JavaVersion version, byte[] frame)
    {
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        Assert.True(registry.TryGetOutbound(UiPackets.Clientbound.PlayerChat, out int wireId, out BoundPacketCodec _));
        Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec bound));
        Assert.True(bound.IsImplemented);
        return bound.Decode(frame, PacketCodecContext.Registryless);
    }

    /// <summary>A 770+ <c>player_chat</c> frame built field by field. The filter mask contains the type enum ordinal as a VarInt, then for PARTIALLY_FILTERED a VarInt-counted array of longs.</summary>
    private static byte[] BuildFrame(int filterType, long[]? filterWords = null, string? unsignedOverride = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);

        w.WriteVarInt(0);           // globalIndex
        w.WriteUuid(Sender);
        w.WriteVarInt(4);           // per-sender index
        w.WriteBool(true);
        var sig = new byte[256];
        Array.Fill(sig, (byte)0x5C);
        w.WriteBytes(sig);

        w.WriteString(Content, 256);
        w.WriteLong(1_700_000_000_000L);
        w.WriteLong(0x0123_4567_89AB_CDEFL);
        w.WriteVarInt(0);           // no last-seen entries

        w.WriteBool(unsignedOverride is not null);
        if (unsignedOverride is not null)
            WriteModernComponent(ref w, Component.Text(unsignedOverride));

        w.WriteVarInt(filterType);
        if (filterType == 2)
        {
            long[] words = filterWords ?? [];
            w.WriteVarInt(words.Length);
            foreach (long word in words)
                w.WriteLong(word);

        }

        w.WriteVarInt(1);           // bound chat type holder
        WriteModernComponent(ref w, Component.Text("Notch"));
        w.WriteBool(false);

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteModernComponent(ref PacketWriter w, Component c) =>
        w.WriteComponent(c, ComponentWireEra.Modern, NbtWireFormat.JavaRootTagOrString);
}
