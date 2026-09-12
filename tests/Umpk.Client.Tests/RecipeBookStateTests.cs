using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The recipe-book unlocked state, driven from both wire eras into the single <see cref="RecipeState"/> a consumer enumerates, plus the leave-bed action.
/// <para>1.12 - 1.21.1 sends one <c>minecraft:recipe</c> packet whose init/add/remove states carry the recipes (numeric ids on 1.12 - 1.12.2, identifiers from 1.13) and the book flags; 1.21.2+ splits it into recipe_book_add / recipe_book_remove / recipe_book_settings. Both land in the same fields, so a caller never branches on the version.</para>
/// </summary>
public sealed class RecipeBookStateTests
{
    private static readonly Identifier Torch = Identifier.Minecraft("torch");
    private static readonly Identifier Chest = Identifier.Minecraft("chest");
    private static readonly Identifier Ladder = Identifier.Minecraft("ladder");

    private static readonly RecipeBookSetting Closed = new(false, false);

    private static string[] Names(RecipeState state) =>
        [.. state.UnlockedRecipes.Select(static id => id.ToString()).Order(StringComparer.Ordinal)];

    private static ClientboundRecipePacket Legacy(
        int state,
        IReadOnlyList<RecipeBookSetting> books,
        IReadOnlyList<Identifier> recipes,
        IReadOnlyList<int> ids) =>
        new(state, books, recipes, [], ids, []);

    [Fact]
    public async Task LegacyIdentifierWireLayout_Init_Add_Remove_Converge_On_One_Set()
    {
        var harness = new ApplierHarness(JavaVersions.V1_20);
        RecipeState recipes = harness.State.Recipes;

        await harness.ApplyAsync(Legacy(RecipeBookState.Init, [new RecipeBookSetting(true, false)], [Torch, Chest], []));
        Assert.Equal(["minecraft:chest", "minecraft:torch"], Names(recipes));

        await harness.ApplyAsync(Legacy(RecipeBookState.Add, [new RecipeBookSetting(true, false)], [Ladder], []));
        Assert.Equal(["minecraft:chest", "minecraft:ladder", "minecraft:torch"], Names(recipes));
        Assert.True(recipes.IsUnlocked(Ladder));

        await harness.ApplyAsync(Legacy(RecipeBookState.Remove, [new RecipeBookSetting(true, false)], [Torch], []));
        Assert.Equal(["minecraft:chest", "minecraft:ladder"], Names(recipes));
        Assert.False(recipes.IsUnlocked(Torch));

        // A second init replaces rather than unions: the set is exactly what the server just sent.
        await harness.ApplyAsync(Legacy(RecipeBookState.Init, [new RecipeBookSetting(true, false)], [Torch], []));
        Assert.Equal([Torch], recipes.UnlockedRecipes.ToArray());
    }

    [Fact]
    public async Task LegacyNumericWireLayout_Init_Add_Remove_Use_The_Same_State()
    {
        var harness = new ApplierHarness(JavaVersions.V1_12_2);
        RecipeState recipes = harness.State.Recipes;

        await harness.ApplyAsync(Legacy(RecipeBookState.Init, [new RecipeBookSetting(true, true)], [], [7, 9]));
        Assert.Equal([7, 9], recipes.UnlockedRecipeIds.Order().ToArray());

        await harness.ApplyAsync(Legacy(RecipeBookState.Add, [new RecipeBookSetting(true, true)], [], [300]));
        Assert.Equal([7, 9, 300], recipes.UnlockedRecipeIds.Order().ToArray());
        Assert.True(recipes.IsUnlocked(300));

        await harness.ApplyAsync(Legacy(RecipeBookState.Remove, [new RecipeBookSetting(true, true)], [], [9]));
        Assert.Equal([7, 300], recipes.UnlockedRecipeIds.Order().ToArray());
        Assert.False(recipes.IsUnlocked(9));

        // The identifier set stays empty on this era: the wire never names a recipe by location here.
        Assert.Empty(recipes.UnlockedRecipes);
    }

    [Fact]
    public async Task BookFlags_Come_From_Both_WireLayouts_And_Pad_To_Four_Books()
    {
        var harness = new ApplierHarness(JavaVersions.V1_20);
        RecipeState recipes = harness.State.Recipes;

        Assert.Equal([Closed, Closed, Closed, Closed], recipes.Books.ToArray());

        // 1.16.2 - 1.21.1 carries all four books in RecipeBookType order.
        await harness.ApplyAsync(Legacy(
            RecipeBookState.Add,
            [
                new RecipeBookSetting(true, false),
                new RecipeBookSetting(false, true),
                new RecipeBookSetting(true, true),
                Closed,
            ],
            [],
            []));

        Assert.True(recipes.Books[0].Open);
        Assert.False(recipes.Books[0].Filtering);
        Assert.True(recipes.Books[1].Filtering);
        Assert.True(recipes.Books[2] is { Open: true, Filtering: true });
        Assert.Equal(Closed, recipes.Books[3]);

        // A 1.12 frame carries only the crafting pair; the books that era does not know stay closed.
        await harness.ApplyAsync(Legacy(RecipeBookState.Add, [new RecipeBookSetting(false, true)], [], []));
        Assert.Equal(new RecipeBookSetting(false, true), recipes.Books[0]);
        Assert.Equal(Closed, recipes.Books[1]);
        Assert.Equal(Closed, recipes.Books[3]);

        // 1.21.2+ delivers the same flags through its own packet, into the same field.
        await harness.ApplyAsync(new ClientboundRecipeBookSettingsPacket(
            [Closed, Closed, Closed, new RecipeBookSetting(true, true)]));
        Assert.Equal(Closed, recipes.Books[0]);
        Assert.Equal(new RecipeBookSetting(true, true), recipes.Books[3]);
    }

    [Fact]
    public async Task ModernWireLayout_Remove_Drops_Ids_And_Replace_Clears_The_Set()
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_5);
        RecipeState recipes = harness.State.Recipes;

        // Seed through the legacy shape so the modern remove has something to act on: the point is that both eras address one set, so a removal keyed by number finds what a numeric unlock added.
        await harness.ApplyAsync(Legacy(RecipeBookState.Init, [Closed], [], [4, 5, 6]));

        await harness.ApplyAsync(new ClientboundRecipeBookRemovePacket([5]));
        Assert.Equal([4, 6], recipes.UnlockedRecipeIds.Order().ToArray());

        // recipe_book_add entries are an opaque recipe-display tree in this build, so an addition is counted rather than unlocked; the count is what tells a consumer the set is incomplete.
        Assert.Equal(0, recipes.OpaqueAdditions);
        await harness.ApplyAsync(new ClientboundRecipeBookAddPacket([], Replace: false, UndecodedEntries: 1));
        Assert.Equal(1, recipes.OpaqueAdditions);
        Assert.Equal([4, 6], recipes.UnlockedRecipeIds.Order().ToArray());

        // A replace frame clears the set and the pending count together.
        await harness.ApplyAsync(new ClientboundRecipeBookAddPacket([], Replace: true));
        Assert.Empty(recipes.UnlockedRecipeIds);
        Assert.Equal(0, recipes.OpaqueAdditions);
    }

    [Fact]
    public async Task RecipeBookChanged_Reports_The_Enumerable_Count()
    {
        var harness = new ApplierHarness(JavaVersions.V1_20);
        var seen = new List<RecipeBookChanged>();
        harness.Events.Subscribe<RecipeBookChanged>(seen.Add);

        await harness.ApplyAsync(Legacy(RecipeBookState.Init, [Closed], [Torch, Chest], []));
        await harness.ApplyAsync(Legacy(RecipeBookState.Remove, [Closed], [Torch], []));

        Assert.Equal([2, 1], seen.Select(e => e.UnlockedCount).ToArray());
        Assert.Equal([1, 2], seen.Select(e => e.Revision).ToArray());
    }

    [Fact]
    public async Task RecipeState_Is_Tracked_With_The_Inventory_Feature_Off()
    {
        // RecipeState is always present on ClientState, so its applier must not be inventory-gated.
        var harness = new ApplierHarness(JavaVersions.V1_20, new ClientFeatures { Inventory = false });

        await harness.ApplyAsync(Legacy(RecipeBookState.Init, [Closed], [Torch], []));
        await harness.ApplyAsync(new ClientboundUpdateRecipesPacket([1, 2, 3]));

        Assert.Equal([Torch], harness.State.Recipes.UnlockedRecipes.ToArray());
        Assert.Equal(1, harness.State.Recipes.Revision);
        Assert.Equal([1, 2, 3], harness.State.Recipes.LatestPayload);
    }

    [Fact]
    public async Task LeaveBedAsync_Sends_The_StopSleeping_Action()
    {
        var recorder = new RecordingSink();
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.EntityId = 77;

        var services = new ClientSessionServices
        {
            Version = JavaVersions.V1_21_5,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(JavaVersions.V1_21_5),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        var actions = new MovementActions(recorder, services, () => null);

        await actions.LeaveBedAsync();

        ServerboundPlayerCommandPacket sent = Assert.IsType<ServerboundPlayerCommandPacket>(
            Assert.Single(recorder.Packets));
        Assert.Equal(77, sent.EntityId);
        // STOP_SLEEPING, third constant on protocol 770. The ordinal is NOT stable across the range: 1.21.6 deleted PRESS_SHIFT_KEY and RELEASE_SHIFT_KEY from the action enum, so from protocol 771 STOP_SLEEPING is ordinal 0. PlayerCommandActionCompatibilityTests covers both layouts.
        Assert.Equal(2, sent.Action);
        Assert.Equal(0, sent.Data);

        // Not one of the neighbouring actions this surface already sends.
        await actions.SetSneakingAsync(true);
        await actions.SetSprintingAsync(true);
        Assert.Equal([2, 0, 3], recorder.Packets.OfType<ServerboundPlayerCommandPacket>().Select(p => p.Action).ToArray());
        var input = Assert.IsType<ServerboundPlayerInputPacket>(recorder.Packets[^1]);
        Assert.True(input.Shift);
        Assert.True(input.Sprint);
    }
}
