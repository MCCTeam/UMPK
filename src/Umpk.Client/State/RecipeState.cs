using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.State;

/// <summary>
/// The session's recipe-book state: which recipes the player has unlocked, the per-book open/filtering flags, and the raw recipe-registry payload.
/// <para>Both wire eras write into this one model. On 1.12-1.21.1 the server sends a single <c>minecraft:recipe</c> packet whose init/add/remove states drive the unlocked set; the recipe identity is a numeric crafting-manager id on 1.12-1.12.2 (<see cref="UnlockedRecipeIds"/>) and a resource location from 1.13 on (<see cref="UnlockedRecipes"/>). On 1.21.2+ the packet was split into recipe_book_add / recipe_book_remove / recipe_book_settings, which carry numeric recipe-display ids (<see cref="UnlockedRecipeIds"/>).</para>
/// <para>One caveat, deliberately visible rather than hidden: the 1.21.2+ recipe_book_add entry list is a nested recipe-display tree that this build keeps opaque, so additions on that era are counted in <see cref="OpaqueAdditions"/> instead of landing in the unlocked sets. A non-zero <see cref="OpaqueAdditions"/> means the unlocked sets are incomplete rather than genuinely empty.</para>
/// The update-recipes payload is likewise opaque (nested recipe-property trees), so this records the latest raw payload and a monotonically increasing revision that consumers can watch. Mutated on the session loop.
/// </summary>
public sealed class RecipeState
{
    private static readonly RecipeBookSetting Closed = new(false, false);

    private readonly Dictionary<int, RecipeBookEntry> _displays = [];

    private readonly HashSet<Identifier> _unlocked = [];
    private readonly HashSet<int> _unlockedIds = [];
    private readonly RecipeBookSetting[] _books = [Closed, Closed, Closed, Closed];
    private byte[] _latest = [];

    /// <summary>The most recent update-recipes payload (opaque bytes); empty until the first update.</summary>
    public IReadOnlyList<byte> LatestPayload => _latest;

    /// <summary>The number of update-recipes packets applied so far.</summary>
    public int Revision { get; private set; }

    /// <summary>The unlocked recipes that the wire named by resource location (1.13+).</summary>
    public IReadOnlyCollection<Identifier> UnlockedRecipes => _unlocked;

    /// <summary>The unlocked recipes that the wire named by number: crafting-manager ids on 1.12-1.12.2 and recipe-display ids on 1.21.2+.</summary>
    public IReadOnlyCollection<int> UnlockedRecipeIds => _unlockedIds;

    /// <summary>The decoded 1.21.2+ entries, by recipe-display id. Empty on older protocols, which carry identifiers rather than display trees and populate <see cref="UnlockedRecipes"/> instead.</summary>
    public IReadOnlyDictionary<int, RecipeBookEntry> Displays => _displays;

    /// <summary>The four recipe books' (open, filtering) flags in vanilla <c>RecipeBookType</c> order: crafting, furnace, blast furnace, smoker. Eras that carry fewer books leave the rest closed.</summary>
    public IReadOnlyList<RecipeBookSetting> Books => _books;

    /// <summary>How many 1.21.2+ recipe_book_add entries were received but not decoded into <see cref="UnlockedRecipeIds"/>, because that era's entry payload is kept opaque. Zero on every era whose unlock packet is fully decoded.</summary>
    public int OpaqueAdditions { get; private set; }

    /// <summary>The number of unlock changes applied so far (any era, any state).</summary>
    public int BookRevision { get; private set; }

    /// <summary>Whether a recipe named by resource location is unlocked.</summary>
    /// <param name="recipe">The recipe identifier.</param>
    public bool IsUnlocked(Identifier recipe) => _unlocked.Contains(recipe);

    /// <summary>Whether a recipe named by number is unlocked.</summary>
    /// <param name="recipeId">The numeric recipe id.</param>
    public bool IsUnlocked(int recipeId) => _unlockedIds.Contains(recipeId);

    internal void Update(byte[] payload)
    {
        _latest = payload;
        Revision++;
    }

    /// <summary>Applies one pre-1.21.2 <c>minecraft:recipe</c> frame. Init replaces the unlocked set, add unions into it, remove subtracts from it; the book flags always replace.</summary>
    internal void ApplyLegacyUnlock(ClientboundRecipePacket packet)
    {
        ReplaceBooks(packet.Books);
        switch (packet.State)
        {
            case RecipeBookState.Init:
                _unlocked.Clear();
                _unlockedIds.Clear();
                OpaqueAdditions = 0;
                Add(packet);
                break;

            case RecipeBookState.Add:
                Add(packet);
                break;

            case RecipeBookState.Remove:
                foreach (Identifier recipe in packet.Recipes)
                    _unlocked.Remove(recipe);

                foreach (int id in packet.LegacyRecipeIds)
                    _unlockedIds.Remove(id);

                break;

            default:
                // An unknown state ordinal must not corrupt the set; the book flags above still apply.
                break;
        }

        BookRevision++;
    }

    /// <summary>Applies a 1.21.2+ <c>recipe_book_add</c>: records each decoded entry by its display id, and counts anything the decoder could not read.</summary>
    /// <param name="entries">The decoded entries.</param>
    /// <param name="undecoded">How many additions could not be decoded; see <see cref="OpaqueAdditions"/>.</param>
    /// <param name="replace">Whether these entries replace the book rather than adding to it.</param>
    internal void ApplyBookAdd(IReadOnlyList<RecipeBookEntry> entries, int undecoded, bool replace)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (replace)
        {
            _unlocked.Clear();
            _unlockedIds.Clear();
            _displays.Clear();
            OpaqueAdditions = 0;
        }

        foreach (RecipeBookEntry entry in entries)
        {
            _displays[entry.DisplayId] = entry;

            // A recipe is named after its result, and the result identifier is only on the wire for the item_stack form. The bare item form sends a numeric id, which the caller resolves through the item registry, so an unnamed entry here is not a missing entry.
            if (entry.ResultId.Path.Length > 0)
                _unlocked.Add(entry.ResultId);

        }

        if (undecoded > 0)
            OpaqueAdditions += undecoded;

        BookRevision++;
    }

    /// <summary>Applies a 1.21.2+ recipe_book_remove: drops the listed recipe-display ids.</summary>
    /// <param name="recipeIds">The recipe-display ids to drop.</param>
    internal void ApplyRemove(IReadOnlyList<int> recipeIds)
    {
        foreach (int id in recipeIds)
            _unlockedIds.Remove(id);

        BookRevision++;
    }

    /// <summary>Replaces the book flags (1.21.2+ recipe_book_settings).</summary>
    /// <param name="books">The per-book (open, filtering) pairs.</param>
    internal void ApplySettings(IReadOnlyList<RecipeBookSetting> books)
    {
        ReplaceBooks(books);
        BookRevision++;
    }

    /// <summary>Drops every unlocked recipe, the book flags and the raw payload. The revision counters are left alone so a consumer watching them still sees forward motion rather than a rewind. The recipe container belongs to the play listener and is discarded whenever that listener is replaced.</summary>
    internal void Clear()
    {
        _unlocked.Clear();
        _unlockedIds.Clear();
        ReplaceBooks([]);
        _latest = [];
        OpaqueAdditions = 0;
    }

    private void Add(ClientboundRecipePacket packet)
    {
        foreach (Identifier recipe in packet.Recipes)
            _unlocked.Add(recipe);

        foreach (int id in packet.LegacyRecipeIds)
            _unlockedIds.Add(id);

    }

    private void ReplaceBooks(IReadOnlyList<RecipeBookSetting> books)
    {
        for (int i = 0; i < _books.Length; i++)
            _books[i] = i < books.Count ? books[i] : Closed;

    }
}
