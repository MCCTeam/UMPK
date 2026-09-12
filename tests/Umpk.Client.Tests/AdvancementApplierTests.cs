using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The advancement applier leg. Before this the update_advancements packet had no applier on ANY protocol, so <see cref="ClientState.Advancements"/> was permanently empty even on the versions where the packet already decoded. These tests drive decoded packets through the real applier chain and assert the state a consumer reads.</summary>
public sealed class AdvancementApplierTests
{
    private static AdvancementNode Node(
        Identifier? parent,
        IReadOnlyList<string> criteria,
        IReadOnlyList<IReadOnlyList<string>> requirements,
        AdvancementDisplayInfo? display = null) =>
        new(parent, display, criteria, requirements, SendsTelemetryEvent: false);

    [Fact]
    public async Task Added_Definitions_Progress_And_Tab_Land_In_State()
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_5);
        int changed = 0;
        harness.Events.Subscribe<AdvancementsChanged>(_ => changed++);

        var packet = new ClientboundUpdateAdvancementsPacket(
            Reset: true,
            Added:
            [
                new AdvancementEntry(
                    Identifier.Minecraft("story/root"),
                    Node(null, ["crafted_stone"], [["crafted_stone"]])),
                new AdvancementEntry(
                    Identifier.Minecraft("story/mine_stone"),
                    Node(Identifier.Minecraft("story/root"), ["get_stone", "never_done"], [["get_stone"], ["never_done"]])),
            ],
            Removed: [],
            Progress:
            [
                new AdvancementProgressEntry(
                    Identifier.Minecraft("story/mine_stone"),
                    [
                        new CriterionProgressEntry("get_stone", 1_700_000_000_123L),
                        new CriterionProgressEntry("never_done", null),
                    ]),
            ],
            ShowAdvancements: true);

        await harness.ApplyAsync(packet);

        AdvancementState state = harness.State.Advancements;
        Assert.Equal(2, state.Advancements.Count);
        Assert.True(state.TryGetAdvancement(Identifier.Minecraft("story/mine_stone"), out Advancement? mined));
        Assert.Equal("minecraft:story/root", mined!.ParentId?.ToString());
        Assert.Equal(["get_stone", "never_done"], [.. mined.Criteria]);
        Assert.Equal(2, mined.Requirements.Count);

        IReadOnlyDictionary<string, DateTimeOffset?> progress = state.GetProgress(Identifier.Minecraft("story/mine_stone"));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123L), progress["get_stone"]);
        Assert.Null(progress["never_done"]);
        Assert.True(state.ShowAdvancements);
        Assert.Equal(1, changed);

        await harness.ApplyAsync(new ClientboundSelectAdvancementsTabPacket(Identifier.Minecraft("story/root")));
        Assert.Equal("minecraft:story/root", state.SelectedTab?.ToString());
        Assert.Equal(2, changed);
    }

    [Fact]
    public async Task Reset_Clears_And_Removed_Ids_Drop_Out()
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_5);
        await harness.ApplyAsync(new ClientboundUpdateAdvancementsPacket(
            Reset: true,
            Added: [new AdvancementEntry(Identifier.Minecraft("a/keep"), Node(null, ["x"], [["x"]])),
                    new AdvancementEntry(Identifier.Minecraft("a/drop"), Node(null, ["y"], [["y"]]))],
            Removed: [],
            Progress: [],
            ShowAdvancements: true));
        Assert.Equal(2, harness.State.Advancements.Advancements.Count);

        // An incremental update (reset = false) keeps the tree and applies only the removal.
        await harness.ApplyAsync(new ClientboundUpdateAdvancementsPacket(
            Reset: false, Added: [], Removed: [Identifier.Minecraft("a/drop")], Progress: [], ShowAdvancements: true));
        Assert.Single(harness.State.Advancements.Advancements);
        Assert.True(harness.State.Advancements.TryGetAdvancement(Identifier.Minecraft("a/keep"), out _));

        // A reset batch replaces the whole tree.
        await harness.ApplyAsync(new ClientboundUpdateAdvancementsPacket(
            Reset: true,
            Added: [new AdvancementEntry(Identifier.Minecraft("b/new"), Node(null, ["z"], [["z"]]))],
            Removed: [],
            Progress: [],
            ShowAdvancements: false));
        Assert.Single(harness.State.Advancements.Advancements);
        Assert.True(harness.State.Advancements.TryGetAdvancement(Identifier.Minecraft("b/new"), out _));
        Assert.False(harness.State.Advancements.ShowAdvancements);
    }

    /// <summary>1.20.2 dropped the criterion-name list from the wire, so on the modern band the node's Criteria is empty and the names have to come from the requirement groups. A consumer must see the same criterion set on both sides of that boundary.</summary>
    [Fact]
    public async Task ModernBand_RecoversCriterionNames_FromRequirements()
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_5);
        await harness.ApplyAsync(new ClientboundUpdateAdvancementsPacket(
            Reset: true,
            Added:
            [
                new AdvancementEntry(
                    Identifier.Minecraft("story/root"),
                    Node(null, criteria: [], requirements: [["has_the_recipe", "unlock_right_away"], ["has_the_recipe"]])),
            ],
            Removed: [],
            Progress: [],
            ShowAdvancements: true));

        Assert.True(harness.State.Advancements.TryGetAdvancement(Identifier.Minecraft("story/root"), out Advancement? a));
        Assert.Equal(["has_the_recipe", "unlock_right_away"], [.. a!.Criteria]);
    }

    /// <summary>End to end on recorded bytes: the 1.17 capture's populated advancement tree decodes through the bound descriptor and reaches state with its display metadata intact.</summary>
    [Fact]
    public async Task Protocol755_RecordedTree_LandsInState()
    {
        Assert.True(JavaVersions.TryGetByProtocol(755, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(755);

        object packet = AdvancementCorpus.DecodeLargestFrame(755, "play-idle", version!);
        await harness.ApplyAsync(packet);

        AdvancementState state = harness.State.Advancements;
        Assert.Equal(18, state.Advancements.Count);
        Assert.True(state.TryGetAdvancement(Identifier.Minecraft("adventure/root"), out Advancement? root));
        Assert.Null(root!.ParentId);
        Assert.NotNull(root.Display);
        Assert.Equal(0, root.Display!.Frame);
        Assert.NotNull(root.Display.Icon);
        Assert.Equal(["killed_by_something", "killed_something"], [.. root.Criteria]);

        Assert.True(state.TryGetAdvancement(Identifier.Minecraft("adventure/spyglass_at_parrot"), out Advancement? child));
        Assert.Equal("minecraft:adventure/root", child!.ParentId?.ToString());
        Assert.True(child.Display!.ShowToast);
        Assert.NotEmpty(state.GetProgress(Identifier.Minecraft("adventure/sleep_in_bed")));
    }
}
