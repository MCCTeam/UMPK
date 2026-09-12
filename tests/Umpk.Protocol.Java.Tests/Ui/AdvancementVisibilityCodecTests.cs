using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>Tests for the 768/769 (1.21.2-1.21.4) update-advancements era: no trailing show-advancements bool. Protocol 770 appends <c>showAdvancements</c>; everything before it is wire-identical.</summary>
public sealed class AdvancementVisibilityCodecTests
{
    private static ClientboundUpdateAdvancementsPacket SamplePacket(bool show) => new(
        Reset: true,
        Added:
        [
            new AdvancementEntry(
                Identifier.Minecraft("story/root"),
                new AdvancementNode(
                    Parent: null,
                    Display: null,
                    Criteria: [],
                    Requirements: [["crafted_stone"], ["mined_dirt", "mined_grass"]],
                    SendsTelemetryEvent: true)),
        ],
        Removed: [Identifier.Minecraft("story/gone")],
        Progress:
        [
            new AdvancementProgressEntry(
                Identifier.Minecraft("story/root"),
                [
                    new CriterionProgressEntry("crafted_stone", 1234567890123L),
                    new CriterionProgressEntry("mined_dirt", null),
                ]),
        ],
        ShowAdvancements: show);

    [Fact]
    public void UpdateAdvancementsV1_21_2_RoundTrips_AndForcesShow()
    {
        ClientboundUpdateAdvancementsPacket decoded =
            CodecRoundTrip.Cycle(AdvancementCodecs.UpdateAdvancementsV1_21_2, SamplePacket(show: true));

        Assert.True(decoded.Reset);
        AdvancementEntry added = Assert.Single(decoded.Added);
        Assert.Equal(Identifier.Minecraft("story/root"), added.Id);
        Assert.Null(added.Value.Parent);
        Assert.Null(added.Value.Display);
        Assert.Equal(2, added.Value.Requirements.Count);
        Assert.Equal(["mined_dirt", "mined_grass"], added.Value.Requirements[1]);
        Assert.True(added.Value.SendsTelemetryEvent);
        Assert.Equal(Identifier.Minecraft("story/gone"), Assert.Single(decoded.Removed));
        AdvancementProgressEntry progress = Assert.Single(decoded.Progress);
        Assert.Equal(1234567890123L, progress.Criteria[0].ObtainedEpochMillis);
        Assert.Null(progress.Criteria[1].ObtainedEpochMillis);

        // These versions have no wire slot for the flag; decode surfaces it as always-shown.
        Assert.True(decoded.ShowAdvancements);
    }

    [Fact]
    public void UpdateAdvancementsV1_21_2_Bytes_AreV1_21_5_MinusTrailingBool()
    {
        ClientboundUpdateAdvancementsPacket packet = SamplePacket(show: true);
        byte[] pre = CodecRoundTrip.Encode(AdvancementCodecs.UpdateAdvancementsV1_21_2, packet);
        byte[] modern = CodecRoundTrip.Encode(AdvancementCodecs.UpdateAdvancementsV1_21_5, packet);

        Assert.Equal(pre.Length + 1, modern.Length);
        Assert.Equal(pre, modern[..^1]);
        Assert.Equal(1, modern[^1]); // the 1.21.5 trailing show bool
    }
}
