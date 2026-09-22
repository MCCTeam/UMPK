using Umpk.Game.Items;

namespace Umpk.Protocol.Java.Packets;

/// <summary>One node of the slot-display tree: what a recipe book draws in a single slot.</summary>
/// <remarks>
/// <para>The hierarchy is closed (the constructor is <c>private protected</c>), so the set of variants is exactly the set supported by the codec and a <c>switch</c> over it is total. The type id on the wire is the SLOT_DISPLAY registration index, which differs by era, so the mapping from variant to id lives in the era table the codec is bound with, never on these records.</para>
/// <para>The eight original variants start at protocol 768. Protocol 775 adds three more.</para>
/// </remarks>
public abstract record SlotDisplay
{
    private protected SlotDisplay()
    {
    }

    /// <summary>Nothing in the slot. This unit variant has no payload.</summary>
    public sealed record Empty : SlotDisplay
    {
        /// <summary>The single instance; the variant carries no state.</summary>
        public static Empty Instance { get; } = new();
    }

    /// <summary>Any fuel item. This unit variant has no payload.</summary>
    public sealed record AnyFuel : SlotDisplay
    {
        /// <summary>The single instance; the variant carries no state.</summary>
        public static AnyFuel Instance { get; } = new();
    }

    /// <summary>One item by registry id: a bare VarInt network id with no identifier.</summary>
    /// <param name="ItemId">The item's network id in the connection's item registry.</param>
    public sealed record Item(int ItemId) : SlotDisplay;

    /// <summary>A full item stack, components and all.</summary>
    /// <remarks>Its payload changed form at 26.1. Up to 1.21.11 the stack writes its count first. From 26.1 it writes the holder id first, then count. The two mirror each other, so a frame read with the wrong one re-encodes byte-identically while reporting the id as the count and the count as the id. The stack reader/writer the codec is bound with is what decides, per protocol.</remarks>
    /// <param name="Value">The stack.</param>
    public sealed record Stack(ItemStack Value) : SlotDisplay;

    /// <summary>Everything in an item tag by name. Through 26.2 this is the whole payload (one resource-location string); from 26.3 the payload is a holder set and this is its named form (see <see cref="TagItems"/> for the inline-ids form).</summary>
    /// <param name="Name">The tag's resource location, exactly as it appears on the wire.</param>
    public sealed record Tag(string Name) : SlotDisplay;

    /// <summary>26.3+: everything in an inline item holder set (the tag payload's direct-ids form).</summary>
    /// <param name="ItemIds">The item network ids, in wire order.</param>
    public sealed record TagItems(IReadOnlyList<int> ItemIds) : SlotDisplay;

    /// <summary>A smithing-trim preview.</summary>
    /// <param name="Base">The base item display.</param>
    /// <param name="Material">The trim material display.</param>
    /// <param name="Pattern">The trim pattern, whose WIRE TYPE changed at 1.21.5: see <see cref="TrimPatternDisplay"/>.</param>
    public sealed record SmithingTrim(SlotDisplay Base, SlotDisplay Material, TrimPatternDisplay Pattern) : SlotDisplay;

    /// <summary>An input paired with what it leaves behind.</summary>
    /// <param name="Input">The consumed input.</param>
    /// <param name="Remainder">What stays in the slot.</param>
    public sealed record WithRemainder(SlotDisplay Input, SlotDisplay Remainder) : SlotDisplay;

    /// <summary>Several displays shown in rotation.</summary>
    /// <param name="Contents">The alternatives, in wire order.</param>
    public sealed record Composite(IReadOnlyList<SlotDisplay> Contents) : SlotDisplay;

    /// <summary>26.1+: the wrapped display with any potion applied.</summary>
    /// <param name="Display">The wrapped display.</param>
    public sealed record WithAnyPotion(SlotDisplay Display) : SlotDisplay;

    /// <summary>26.1+: a display shown only when a data component is present. The second field is a component-id VarInt.</summary>
    /// <param name="Source">The wrapped display.</param>
    /// <param name="ComponentId">The gating component's network id.</param>
    public sealed record OnlyWithComponent(SlotDisplay Source, int ComponentId) : SlotDisplay;

    /// <summary>26.1+: a dye and the thing it dyes. The wire writes the dye first, then the target.</summary>
    /// <param name="Dye">The dye display.</param>
    /// <param name="Target">The dyed item display.</param>
    public sealed record Dyed(SlotDisplay Dye, SlotDisplay Target) : SlotDisplay;
}

/// <summary>The third field of <see cref="SlotDisplay.SmithingTrim"/>, whose wire type is era dependent.</summary>
/// <remarks>Versions 1.21.2 and 1.21.4 write the pattern as a nested slot display. Version 1.21.5 changes the field to a trim-pattern holder, and 1.21.6, 1.21.9, 1.21.11, 26.1, and 26.2 all keep that. Reading the holder form on 1.21.2 (or the nested form on 1.21.5+) misreads every byte after it, so the era table decides which shape applies and the codec refuses the other one.</remarks>
public abstract record TrimPatternDisplay
{
    private protected TrimPatternDisplay()
    {
    }

    /// <summary>1.21.2 - 1.21.4: the pattern is itself a <see cref="SlotDisplay"/>.</summary>
    /// <param name="Pattern">The nested display.</param>
    public sealed record Nested(SlotDisplay Pattern) : TrimPatternDisplay;

    /// <summary>1.21.5+: the pattern uses the registry-holder form, on the wire as <c>VarInt(id + 1)</c>. The inline (direct) form, signalled by a leading zero, carries a <c>Component</c> whose dialect depends on the connection's component era, which is not reachable from inside a recipe display. Registered trim patterns therefore use the reachable registry form.</summary>
    /// <param name="PatternId">The trim pattern's network id in the connection's trim-pattern registry.</param>
    public sealed record Registry(int PatternId) : TrimPatternDisplay;
}

/// <summary>One <c>RecipeDisplay</c>: how a recipe book lays a recipe out. The wire type id is the RECIPE_DISPLAY registration index, which is <c>crafting_shapeless, crafting_shaped, furnace, stonecutter, smithing</c> and is unchanged from 1.21.2 to 26.2, so unlike the slot table it is a constant.</summary>
public abstract record RecipeDisplay
{
    private protected RecipeDisplay()
    {
    }

    /// <summary>A shapeless recipe: ingredient list, result, and crafting station.</summary>
    /// <param name="Ingredients">The ingredient displays, in wire order.</param>
    /// <param name="Result">What the recipe makes.</param>
    /// <param name="CraftingStation">The station that crafts it.</param>
    public sealed record CraftingShapeless(
        IReadOnlyList<SlotDisplay> Ingredients,
        SlotDisplay Result,
        SlotDisplay CraftingStation) : RecipeDisplay;

    /// <summary>A shaped recipe: width, height, ingredient list, result, and crafting station. The server requires <c>ingredients.Count == width * height</c>; that is a server-side invariant, not a wire rule, so this record does not enforce it and the codec writes whatever it is given.</summary>
    /// <param name="Width">The grid width.</param>
    /// <param name="Height">The grid height.</param>
    /// <param name="Ingredients">The ingredient displays, in row-major wire order.</param>
    /// <param name="Result">What the recipe makes.</param>
    /// <param name="CraftingStation">The station that crafts it.</param>
    public sealed record CraftingShaped(
        int Width,
        int Height,
        IReadOnlyList<SlotDisplay> Ingredients,
        SlotDisplay Result,
        SlotDisplay CraftingStation) : RecipeDisplay;

    /// <summary>A furnace recipe: ingredient, fuel, result, station, VarInt duration, and float experience.</summary>
    /// <param name="Ingredient">What goes in.</param>
    /// <param name="Fuel">What burns.</param>
    /// <param name="Result">What comes out.</param>
    /// <param name="CraftingStation">The furnace variant.</param>
    /// <param name="Duration">Cook time in ticks.</param>
    /// <param name="Experience">Experience awarded.</param>
    public sealed record Furnace(
        SlotDisplay Ingredient,
        SlotDisplay Fuel,
        SlotDisplay Result,
        SlotDisplay CraftingStation,
        int Duration,
        float Experience) : RecipeDisplay;

    /// <summary>A stonecutter recipe: input, result, and station.</summary>
    /// <param name="Input">What goes in.</param>
    /// <param name="Result">What comes out.</param>
    /// <param name="CraftingStation">The stonecutter.</param>
    public sealed record Stonecutter(
        SlotDisplay Input,
        SlotDisplay Result,
        SlotDisplay CraftingStation) : RecipeDisplay;

    /// <summary>A smithing recipe: template, base, addition, result, and station.</summary>
    /// <param name="Template">The smithing template.</param>
    /// <param name="Base">The base item.</param>
    /// <param name="Addition">The addition.</param>
    /// <param name="Result">What comes out.</param>
    /// <param name="CraftingStation">The smithing table.</param>
    public sealed record Smithing(
        SlotDisplay Template,
        SlotDisplay Base,
        SlotDisplay Addition,
        SlotDisplay Result,
        SlotDisplay CraftingStation) : RecipeDisplay;
}

/// <summary>One entry of a recipe display's <c>craftingRequirements</c>. A leading VarInt of 0 means "a tag, named next"; anything else is <c>count + 1</c> inline network ids.</summary>
public abstract record RecipeIngredient
{
    private protected RecipeIngredient()
    {
    }

    /// <summary>The named-set form: a single item tag.</summary>
    /// <param name="Name">The tag's resource location, exactly as it appears on the wire.</param>
    public sealed record Tag(string Name) : RecipeIngredient;

    /// <summary>The inline form: an explicit list of item network ids.</summary>
    /// <param name="ItemIds">The item network ids, in wire order.</param>
    public sealed record Items(IReadOnlyList<int> ItemIds) : RecipeIngredient;
}
