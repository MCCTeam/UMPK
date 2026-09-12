using System.Buffers;
using Umpk.Game.Items;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>Round-trips the update_advancements codec on both eras (770 = 1.21.5 icon components, 776 = 26.2), covering display-info present and absent, the frame types, requirement groups, and criterion progress maps (obtained and unobtained). Uses the item test registry context so the DisplayInfo icon resolves.</summary>
public class UpdateAdvancementsCodecTests
{
    private static PacketCodecContext Ctx => ItemTestRegistries.Context;

    private static ItemStack Icon() => new(ItemTestRegistries.Item(ItemTestRegistries.Stone), 1);

    private static T Cycle<T>(PacketCodec<T> codec, T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, value, Ctx);
        byte[] bytes = buffer.WrittenSpan.ToArray();

        var reader = new PacketReader(bytes);
        T decoded = codec.Decode(ref reader, Ctx);
        Assert.Equal(0, reader.Remaining);

        // Byte-stability: re-encode must reproduce the same bytes.
        var buffer2 = new ArrayBufferWriter<byte>();
        var writer2 = new PacketWriter(buffer2);
        codec.Encode(ref writer2, decoded, Ctx);
        Assert.Equal(bytes, buffer2.WrittenSpan.ToArray());
        return decoded;
    }

    public static TheoryData<bool> Eras => new() { false, true };

    private static PacketCodec<ClientboundUpdateAdvancementsPacket> CodecFor(bool era776) =>
        era776 ? AdvancementCodecs.UpdateAdvancementsV26_2 : AdvancementCodecs.UpdateAdvancementsV1_21_5;

    [Theory]
    [MemberData(nameof(Eras))]
    public void Empty_RoundTrips(bool era776)
    {
        var p = new ClientboundUpdateAdvancementsPacket(Reset: true, [], [], [], ShowAdvancements: false);
        var d = Cycle(CodecFor(era776), p);
        Assert.True(d.Reset);
        Assert.Empty(d.Added);
        Assert.Empty(d.Removed);
        Assert.Empty(d.Progress);
        Assert.False(d.ShowAdvancements);
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void WithDisplayInfo_AllFrames_RoundTrip(bool era776)
    {
        var display = new AdvancementDisplayInfo(
            Component.Text("Title"), Component.Text("Description"), Icon(),
            AdvancementFrameType.Challenge, Background: Identifier.Minecraft("textures/gui/advancements/backgrounds/adventure.png"),
            ShowToast: true, Hidden: false, X: 1.5f, Y: -2.25f);
        var node = new AdvancementNode(
            Parent: Identifier.Minecraft("story/root"),
            Display: display,
            Criteria: [],
            Requirements: [["criterion_a", "criterion_b"], ["criterion_c"]],
            SendsTelemetryEvent: true);
        var added = new AdvancementEntry(Identifier.Minecraft("story/mine_stone"), node);

        var progress = new AdvancementProgressEntry(
            Identifier.Minecraft("story/mine_stone"),
            [
                new CriterionProgressEntry("get_stone", 1_700_000_000_000L),
                new CriterionProgressEntry("never_done", null),
            ]);

        var p = new ClientboundUpdateAdvancementsPacket(
            Reset: false,
            Added: [added],
            Removed: [Identifier.Minecraft("story/old")],
            Progress: [progress],
            ShowAdvancements: true);

        var d = Cycle(CodecFor(era776), p);
        Assert.Single(d.Added);
        AdvancementDisplayInfo? di = d.Added[0].Value.Display;
        Assert.NotNull(di);
        Assert.Equal(AdvancementFrameType.Challenge, di!.Frame);
        Assert.Equal("Title", di.Title.ToPlainText());
        Assert.NotNull(di.Background);
        Assert.Equal(1.5f, di.X);
        Assert.Equal(2, d.Added[0].Value.Requirements.Count);
        Assert.Equal("minecraft:story/root", d.Added[0].Value.Parent!.ToString());
        Assert.Single(d.Removed);
        Assert.Equal(1_700_000_000_000L, d.Progress[0].Criteria[0].ObtainedEpochMillis);
        Assert.Null(d.Progress[0].Criteria[1].ObtainedEpochMillis);
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void DisplayAbsent_And_NoBackground_RoundTrip(bool era776)
    {
        // A node with no display info, and another with display but no background (flags bit 1 clear).
        var noDisplay = new AdvancementEntry(
            Identifier.Minecraft("a/x"),
            new AdvancementNode(Parent: null, Display: null, Criteria: [], Requirements: [], SendsTelemetryEvent: false));

        var noBackground = new AdvancementEntry(
            Identifier.Minecraft("a/y"),
            new AdvancementNode(
                Parent: null,
                Display: new AdvancementDisplayInfo(
                    Component.Text("t"), Component.Text("d"), Icon(), AdvancementFrameType.Goal,
                    Background: null, ShowToast: false, Hidden: true, X: 0f, Y: 0f),
                Criteria: [],
                Requirements: [],
                SendsTelemetryEvent: false));

        var p = new ClientboundUpdateAdvancementsPacket(false, [noDisplay, noBackground], [], [], true);
        var d = Cycle(CodecFor(era776), p);
        Assert.Null(d.Added[0].Value.Display);
        Assert.NotNull(d.Added[1].Value.Display);
        Assert.Null(d.Added[1].Value.Display!.Background);
        Assert.True(d.Added[1].Value.Display!.Hidden);
        Assert.Equal(AdvancementFrameType.Goal, d.Added[1].Value.Display!.Frame);
    }
}
