using Umpk.Client.State;

namespace Umpk.Client.Snapshots;

/// <summary>One recipe book's flags (crafting, furnace, blast furnace, or smoker).</summary>
/// <param name="Open">Whether the book is open.</param>
/// <param name="Filtering">Whether the book filters to craftable recipes.</param>
public readonly record struct RecipeBookFlags(bool Open, bool Filtering);

/// <summary>One decoded recipe-book entry, as a caller sees it: the display id it is acted on by, and the name of what it makes.</summary>
/// <param name="DisplayId">The recipe-display id, which is what place-recipe takes.</param>
/// <param name="Result">The item the recipe makes, or default when the result is not a single named item.</param>
/// <param name="ResultCount">How many it makes.</param>
public sealed record RecipeBookRecipe(int DisplayId, Identifier Result, int ResultCount);

/// <summary>An immutable snapshot of the session's recipe book: the unlocked recipes named both ways the wire can name them, the four per-book open/filtering flags, and the count of additions this build keeps opaque.</summary>
/// <param name="Recipes">The unlocked recipes named by resource location: sent that way on 1.13 through 1.21.1, and resolved from the decoded display tree on 1.21.2+. Empty on 1.12-1.12.2, which names recipes by number only.</param>
/// <param name="RecipeIds">The unlocked recipes the wire named by number: crafting-manager ids on 1.12-1.12.2.</param>
/// <param name="Books">The four recipe books' open/filtering flags, in crafting/furnace/blast-furnace/smoker order.</param>
/// <param name="OpaqueAdditions">How many recipe-book additions arrived that could NOT be decoded. Normally zero: the 1.21.2+ display tree is decoded now. A non-zero value means the lists here are INCOMPLETE rather than genuinely empty, so a caller must not read an empty list as "nothing is unlocked" without also checking this field.</param>
/// <param name="Revision">The number of unlock changes applied so far.</param>
/// <param name="Entries">The decoded 1.21.2+ entries: each display id paired with what it makes. Empty on older protocols, which name recipes by resource location and carry no display id at all.</param>
public sealed record RecipeBookSnapshot(
    IReadOnlyList<Identifier> Recipes,
    IReadOnlyList<int> RecipeIds,
    IReadOnlyList<RecipeBookFlags> Books,
    int OpaqueAdditions,
    int Revision,
    IReadOnlyList<RecipeBookRecipe>? Entries = null)
{
    /// <summary>Projects a <see cref="RecipeBookSnapshot"/> from live tracked state. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    public static RecipeBookSnapshot Project(RecipeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var books = new List<RecipeBookFlags>(state.Books.Count);
        foreach (Umpk.Protocol.Java.Packets.RecipeBookSetting book in state.Books)
            books.Add(new RecipeBookFlags(book.Open, book.Filtering));

        // Entries pairs each display id with what it makes. Recipes and RecipeIds stay as they were, so pre-1.21.2 callers are untouched: those protocols send identifiers with no display id at all, and Entries is empty there.
        var entries = new List<RecipeBookRecipe>(state.Displays.Count);
        foreach ((int id, Umpk.Protocol.Java.Packets.RecipeBookEntry entry) in state.Displays)
            entries.Add(new RecipeBookRecipe(id, entry.ResultId, entry.ResultCount));

        entries.Sort(static (a, b) => a.DisplayId.CompareTo(b.DisplayId));

        return new RecipeBookSnapshot(
            [.. state.UnlockedRecipes],
            [.. state.UnlockedRecipeIds],
            books,
            state.OpaqueAdditions,
            state.BookRevision,
            entries);
    }
}
