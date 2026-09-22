using Umpk.Protocol.Java.Packets;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>
/// The 1.21.2+ recipe-display tree: what <c>minecraft:recipe_book_add</c> carries instead of the identifier lists the pre-1.21.2 <c>minecraft:recipe</c> packet sent.
/// <para>Read and write are exact inverses over the whole tree, so a decoded frame re-encodes byte for byte. Every field consumed by a read branch is retained and emitted by its matching write branch.</para>
/// </summary>
internal static class RecipeDisplayCodecs
{
    /// <summary>The <c>minecraft:slot_display</c> registry as it stands from 1.21.2 to 1.21.11. The type id on the wire is the registration index, so this array is the wire mapping.</summary>
    private static readonly SlotDisplayKind[] KindsV1_21_2 =
    [
        SlotDisplayKind.Empty,
        SlotDisplayKind.AnyFuel,
        SlotDisplayKind.Item,
        SlotDisplayKind.ItemStack,
        SlotDisplayKind.Tag,
        SlotDisplayKind.SmithingTrim,
        SlotDisplayKind.WithRemainder,
        SlotDisplayKind.Composite,
    ];

    /// <summary>The same registry from 26.1 onward. Three variants were INSERTED rather than appended (<c>with_any_potion</c> and <c>only_with_component</c> at 2 and 3, <c>dyed</c> at 7), so every id from 2 upward shifts and the older table cannot be reused. Version 26.2 keeps this order.</summary>
    private static readonly SlotDisplayKind[] KindsV26_1 =
    [
        SlotDisplayKind.Empty,
        SlotDisplayKind.AnyFuel,
        SlotDisplayKind.WithAnyPotion,
        SlotDisplayKind.OnlyWithComponent,
        SlotDisplayKind.Item,
        SlotDisplayKind.ItemStack,
        SlotDisplayKind.Tag,
        SlotDisplayKind.Dyed,
        SlotDisplayKind.SmithingTrim,
        SlotDisplayKind.WithRemainder,
        SlotDisplayKind.Composite,
    ];

    /// <summary>1.21.2 - 1.21.4 (768, 769): the eight-variant table, and <c>smithing_trim</c>'s pattern is a nested <c>SlotDisplay</c>.</summary>
    internal static SlotDisplayTable SlotsV1_21_2 { get; } = new(KindsV1_21_2, trimPatternIsHolder: false);

    /// <summary>1.21.5 - 1.21.11 (770 - 774): the same eight variants in the same order, but <c>smithing_trim</c>'s pattern became a registry holder. Two eras that agree on the kind ORDER can still disagree on a PAYLOAD, which is why the table carries both facts instead of being a bare array.</summary>
    internal static SlotDisplayTable SlotsV1_21_5 { get; } = new(KindsV1_21_2, trimPatternIsHolder: true);

    /// <summary>26.1 - 26.2 (775, 776): the eleven-variant table, holder-form trim pattern.</summary>
    internal static SlotDisplayTable SlotsV26_1 { get; } = new(KindsV26_1, trimPatternIsHolder: true, tagIsHolderSet: false);

    /// <summary>26.3 (777): the eleven-variant table, holder-form trim pattern, holder-set tag payload.</summary>
    internal static SlotDisplayTable SlotsV26_3 { get; } = new(KindsV26_1, trimPatternIsHolder: true, tagIsHolderSet: true);

    /// <summary>One variant of <c>SlotDisplay</c>, named rather than numbered so the era tables can differ.</summary>
    internal enum SlotDisplayKind
    {
        /// <summary>Nothing in the slot.</summary>
        Empty,

        /// <summary>Any fuel item.</summary>
        AnyFuel,

        /// <summary>A single item, by registry id.</summary>
        Item,

        /// <summary>A full item stack, components and all.</summary>
        ItemStack,

        /// <summary>Everything in an item tag.</summary>
        Tag,

        /// <summary>A smithing-trim preview.</summary>
        SmithingTrim,

        /// <summary>An input paired with what it leaves behind.</summary>
        WithRemainder,

        /// <summary>Several displays shown together.</summary>
        Composite,

        /// <summary>26.1+: the display with any potion applied.</summary>
        WithAnyPotion,

        /// <summary>26.1+: the display only when a component is present.</summary>
        OnlyWithComponent,

        /// <summary>26.1+: a dye paired with what it dyes.</summary>
        Dyed,
    }

    /// <summary>One era's <c>slot_display</c> wire rules: the registration order (which IS the id mapping) plus the shape of <c>smithing_trim</c>'s third field and of the <c>tag</c> payload. The three move independently across the ten protocols this packet exists on, and getting any wrong misreads every byte that follows, so they travel together and a codec is bound with one of these rather than with a loose array.</summary>
    internal sealed class SlotDisplayTable
    {
        private readonly SlotDisplayKind[] _kinds;

        internal SlotDisplayTable(SlotDisplayKind[] kinds, bool trimPatternIsHolder, bool tagIsHolderSet = false)
        {
            _kinds = kinds;
            TrimPatternIsHolder = trimPatternIsHolder;
            TagIsHolderSet = tagIsHolderSet;
            Form = $"{string.Join('+', kinds)},trimholder={(trimPatternIsHolder ? 1 : 0)}";
            if (tagIsHolderSet)
            {
                Form += ",tagholderset=1";
            }
        }

        /// <summary>This era's contribution to the wire shape of any codec that reads through it.</summary>
        internal string Form { get; }

        /// <summary>True from 1.21.5, when <c>smithing_trim</c>'s pattern uses a registry holder.</summary>
        internal bool TrimPatternIsHolder { get; }

        /// <summary>True from 26.3, when the <c>tag</c> payload is an item holder set (VarInt count+1, where 0 names a tag next and anything else counts inline ids) instead of a bare identifier string.</summary>
        internal bool TagIsHolderSet { get; }

        /// <summary>How many variants this era registers.</summary>
        internal int Count => _kinds.Length;

        /// <summary>The variant a wire type id names.</summary>
        internal SlotDisplayKind KindOf(int typeId) => _kinds[typeId];

        /// <summary>The wire type id of a variant, or -1 when this era does not register it.</summary>
        internal int IdOf(SlotDisplayKind kind) => Array.IndexOf(_kinds, kind);
    }

    /// <summary>Reads one recipe-book-add body: the entry list, then the replace flag.</summary>
    /// <param name="r">The reader, positioned at the start of the packet body.</param>
    /// <param name="context">The codec context, for the era's item-stack reader.</param>
    /// <param name="table">The era's <c>slot_display</c> wire rules.</param>
    /// <param name="readStack">The era's item-stack reader (component tables differ per version).</param>
    public static ClientboundRecipeBookAddPacket ReadAdd(
        ref PacketReader r,
        PacketCodecContext context,
        SlotDisplayTable table,
        StackReader readStack)
    {
        int count = r.ReadVarInt();
        var entries = new List<RecipeBookEntry>(count);
        for (int i = 0; i < count; i++)
            entries.Add(ReadEntry(ref r, context, table, readStack));

        bool replace = r.ReadBool();
        return new ClientboundRecipeBookAddPacket(entries, replace, UndecodedEntries: 0);
    }

    /// <summary>Writes one <c>ClientboundRecipeBookAddPacket</c> body: the exact inverse of <see cref="ReadAdd"/>.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="packet">The packet to write.</param>
    /// <param name="context">The codec context, for the era's item-stack writer.</param>
    /// <param name="table">The era's <c>slot_display</c> wire rules.</param>
    /// <param name="writeStack">The era's item-stack writer.</param>
    /// <exception cref="ProtocolViolationException">The packet cannot be written faithfully: either the decode that produced it was contained partway (so the bytes those entries stood for are gone), or it holds a variant this era's registry does not have. Emitting a short or mistyped frame instead would silently corrupt the wire representation.</exception>
    public static void WriteAdd(
        ref PacketWriter w,
        ClientboundRecipeBookAddPacket packet,
        PacketCodecContext context,
        SlotDisplayTable table,
        StackWriter writeStack)
    {
        if (packet.UndecodedEntries != 0)
            throw new ProtocolViolationException(
                $"recipe_book_add reports {packet.UndecodedEntries} undecoded entries and cannot be re-encoded: " +
                "the tree those bytes stood for was never decoded.");

        w.WriteVarInt(packet.Entries.Count);
        foreach (RecipeBookEntry entry in packet.Entries)
            WriteEntry(ref w, entry, context, table, writeStack);

        w.WriteBool(packet.Replace);
    }

    // Entry
    //
    // Each entry carries a display-id VarInt, a recipe display, an optional group VarInt, a category VarInt, optional crafting requirements, and one byte of notification/highlight flags.

    private static RecipeBookEntry ReadEntry(
        ref PacketReader r, PacketCodecContext context, SlotDisplayTable table, StackReader readStack)
    {
        int displayId = r.ReadVarInt();
        RecipeDisplay display = ReadDisplay(ref r, context, table, readStack);

        // OptionalVarInt group: a leading VarInt of 0 means absent, otherwise the value is n - 1.
        int rawGroup = r.ReadVarInt();
        int? group = rawGroup == 0 ? null : rawGroup - 1;

        int category = r.ReadVarInt();

        // Optional ingredient list: each ingredient is an item holder set, which the recipe book only needs for the "can I craft this" highlight. Absent is not the same as empty on the wire, so the two are kept apart (null versus an empty list).
        List<RecipeIngredient>? requirements = null;
        if (r.ReadBool())
        {
            int ingredients = r.ReadVarInt();
            requirements = new List<RecipeIngredient>(ingredients);
            for (int i = 0; i < ingredients; i++)
                requirements.Add(ReadHolderSet(ref r));

        }

        byte flags = r.ReadByte();

        SlotResult result = ResolveResult(ResultOf(display), context);
        return new RecipeBookEntry(
            displayId,
            KindOf(display),
            result.ItemId,
            result.Id,
            result.Count,
            group,
            category,
            (flags & 1) != 0,
            (flags & 2) != 0,
            display,
            requirements);
    }

    private static void WriteEntry(
        ref PacketWriter w,
        RecipeBookEntry entry,
        PacketCodecContext context,
        SlotDisplayTable table,
        StackWriter writeStack)
    {
        w.WriteVarInt(entry.DisplayId);
        WriteDisplay(ref w, entry.Display, context, table, writeStack);
        w.WriteVarInt(entry.Group is int group ? group + 1 : 0);
        w.WriteVarInt(entry.Category);

        if (entry.CraftingRequirements is null)
            w.WriteBool(false);

        else
        {
            w.WriteBool(true);
            w.WriteVarInt(entry.CraftingRequirements.Count);
            foreach (RecipeIngredient ingredient in entry.CraftingRequirements)
                WriteHolderSet(ref w, ingredient);

        }

        w.WriteByte((byte)((entry.Notification ? 1 : 0) | (entry.Highlight ? 2 : 0)));
    }

    // Recipe display
    //
    // Recipe display registration order is unchanged from 1.21.2 through 26.2, so unlike slot_display this order is a constant rather than an era table.

    private const int DisplayCraftingShapeless = 0;

    private const int DisplayCraftingShaped = 1;

    private const int DisplayFurnace = 2;

    private const int DisplayStonecutter = 3;

    private const int DisplaySmithing = 4;

    private static RecipeDisplay ReadDisplay(
        ref PacketReader r, PacketCodecContext context, SlotDisplayTable table, StackReader readStack)
    {
        int typeId = r.ReadVarInt();
        switch (typeId)
        {
            case DisplayCraftingShapeless:
                {
                    // Ingredients, result, and station.
                    SlotDisplay[] ingredients = ReadSlotList(ref r, context, table, readStack);
                    SlotDisplay result = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay station = ReadSlot(ref r, context, table, readStack);
                    return new RecipeDisplay.CraftingShapeless(ingredients, result, station);
                }

            case DisplayCraftingShaped:
                {
                    // Width, height, ingredients, result, and station.
                    int width = r.ReadVarInt();
                    int height = r.ReadVarInt();
                    SlotDisplay[] ingredients = ReadSlotList(ref r, context, table, readStack);
                    SlotDisplay result = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay station = ReadSlot(ref r, context, table, readStack);
                    return new RecipeDisplay.CraftingShaped(width, height, ingredients, result, station);
                }

            case DisplayFurnace:
                {
                    // Ingredient, fuel, result, station, VarInt duration, and float experience.
                    SlotDisplay ingredient = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay fuel = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay result = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay station = ReadSlot(ref r, context, table, readStack);
                    int duration = r.ReadVarInt();
                    float experience = r.ReadFloat();
                    return new RecipeDisplay.Furnace(ingredient, fuel, result, station, duration, experience);
                }

            case DisplayStonecutter:
                {
                    // Input, result, and station.
                    SlotDisplay input = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay result = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay station = ReadSlot(ref r, context, table, readStack);
                    return new RecipeDisplay.Stonecutter(input, result, station);
                }

            case DisplaySmithing:
                {
                    // Template, base, addition, result, and station.
                    SlotDisplay template = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay baseItem = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay addition = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay result = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay station = ReadSlot(ref r, context, table, readStack);
                    return new RecipeDisplay.Smithing(template, baseItem, addition, result, station);
                }

            default:
                // An unknown display type has an unknown length, so there is nothing to skip TO. Throwing hands the whole packet to the containment path rather than guessing at an offset.
                throw new ProtocolViolationException(
                    $"Unknown recipe_display type {typeId} in recipe_book_add.");
        }
    }

    private static void WriteDisplay(
        ref PacketWriter w,
        RecipeDisplay display,
        PacketCodecContext context,
        SlotDisplayTable table,
        StackWriter writeStack)
    {
        switch (display)
        {
            case RecipeDisplay.CraftingShapeless shapeless:
                w.WriteVarInt(DisplayCraftingShapeless);
                WriteSlotList(ref w, shapeless.Ingredients, context, table, writeStack);
                WriteSlot(ref w, shapeless.Result, context, table, writeStack);
                WriteSlot(ref w, shapeless.CraftingStation, context, table, writeStack);
                break;

            case RecipeDisplay.CraftingShaped shaped:
                w.WriteVarInt(DisplayCraftingShaped);
                w.WriteVarInt(shaped.Width);
                w.WriteVarInt(shaped.Height);
                WriteSlotList(ref w, shaped.Ingredients, context, table, writeStack);
                WriteSlot(ref w, shaped.Result, context, table, writeStack);
                WriteSlot(ref w, shaped.CraftingStation, context, table, writeStack);
                break;

            case RecipeDisplay.Furnace furnace:
                w.WriteVarInt(DisplayFurnace);
                WriteSlot(ref w, furnace.Ingredient, context, table, writeStack);
                WriteSlot(ref w, furnace.Fuel, context, table, writeStack);
                WriteSlot(ref w, furnace.Result, context, table, writeStack);
                WriteSlot(ref w, furnace.CraftingStation, context, table, writeStack);
                w.WriteVarInt(furnace.Duration);
                w.WriteFloat(furnace.Experience);
                break;

            case RecipeDisplay.Stonecutter stonecutter:
                w.WriteVarInt(DisplayStonecutter);
                WriteSlot(ref w, stonecutter.Input, context, table, writeStack);
                WriteSlot(ref w, stonecutter.Result, context, table, writeStack);
                WriteSlot(ref w, stonecutter.CraftingStation, context, table, writeStack);
                break;

            case RecipeDisplay.Smithing smithing:
                w.WriteVarInt(DisplaySmithing);
                WriteSlot(ref w, smithing.Template, context, table, writeStack);
                WriteSlot(ref w, smithing.Base, context, table, writeStack);
                WriteSlot(ref w, smithing.Addition, context, table, writeStack);
                WriteSlot(ref w, smithing.Result, context, table, writeStack);
                WriteSlot(ref w, smithing.CraftingStation, context, table, writeStack);
                break;

            default:
                throw new ProtocolViolationException(
                    $"Unhandled recipe_display variant {display.GetType().Name} in recipe_book_add.");
        }
    }

    private static SlotDisplay[] ReadSlotList(
        ref PacketReader r, PacketCodecContext context, SlotDisplayTable table, StackReader readStack)
    {
        int count = r.ReadVarInt();
        var slots = new SlotDisplay[count];
        for (int i = 0; i < count; i++)
            slots[i] = ReadSlot(ref r, context, table, readStack);

        return slots;
    }

    private static void WriteSlotList(
        ref PacketWriter w,
        IReadOnlyList<SlotDisplay> slots,
        PacketCodecContext context,
        SlotDisplayTable table,
        StackWriter writeStack)
    {
        w.WriteVarInt(slots.Count);
        foreach (SlotDisplay slot in slots)
            WriteSlot(ref w, slot, context, table, writeStack);

    }

    // Slot display

    /// <summary>Reads a slot display. Protocol 768 has eight variants; protocol 775 adds <c>with_any_potion</c>, <c>only_with_component</c>, and <c>dyed</c>.</summary>
    private static SlotDisplay ReadSlot(
        ref PacketReader r, PacketCodecContext context, SlotDisplayTable table, StackReader readStack)
    {
        int typeId = r.ReadVarInt();
        if (typeId < 0 || typeId >= table.Count)
            throw new ProtocolViolationException(
                $"Unknown slot_display type {typeId} in recipe_book_add (era table has {table.Count}).");

        switch (table.KindOf(typeId))
        {
            case SlotDisplayKind.Empty:
                // This variant has no payload.
                return SlotDisplay.Empty.Instance;

            case SlotDisplayKind.AnyFuel:
                return SlotDisplay.AnyFuel.Instance;

            case SlotDisplayKind.Item:
                // A bare network id with no identifier on the wire. It is resolved against the connection's item registry in ResolveResult, which is what stops half a recipe book listing as "#647" instead of "Oak Boat".
                return new SlotDisplay.Item(r.ReadVarInt());

            case SlotDisplayKind.ItemStack:
                return new SlotDisplay.Stack(readStack(ref r, context));

            case SlotDisplayKind.Tag:
                // Through 26.2 the tag is one resource-location string. From 26.3 it is an item holder set: VarInt 0 names a tag next, anything else counts inline network ids.
                if (table.TagIsHolderSet)
                    return ReadTagHolderSet(ref r);

                return new SlotDisplay.Tag(r.ReadString());

            case SlotDisplayKind.SmithingTrim:
                {
                    SlotDisplay baseItem = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay material = ReadSlot(ref r, context, table, readStack);
                    TrimPatternDisplay pattern = table.TrimPatternIsHolder
                        ? ReadTrimPatternHolder(ref r)
                        : new TrimPatternDisplay.Nested(ReadSlot(ref r, context, table, readStack));
                    return new SlotDisplay.SmithingTrim(baseItem, material, pattern);
                }

            case SlotDisplayKind.WithRemainder:
                {
                    SlotDisplay input = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay remainder = ReadSlot(ref r, context, table, readStack);
                    return new SlotDisplay.WithRemainder(input, remainder);
                }

            case SlotDisplayKind.Composite:
                return new SlotDisplay.Composite(ReadSlotList(ref r, context, table, readStack));

            case SlotDisplayKind.WithAnyPotion:
                return new SlotDisplay.WithAnyPotion(ReadSlot(ref r, context, table, readStack));

            case SlotDisplayKind.OnlyWithComponent:
                {
                    SlotDisplay source = ReadSlot(ref r, context, table, readStack);
                    return new SlotDisplay.OnlyWithComponent(source, r.ReadVarInt());
                }

            case SlotDisplayKind.Dyed:
                {
                    SlotDisplay dye = ReadSlot(ref r, context, table, readStack);
                    SlotDisplay target = ReadSlot(ref r, context, table, readStack);
                    return new SlotDisplay.Dyed(dye, target);
                }

            default:
                throw new ProtocolViolationException($"Unhandled slot_display kind {table.KindOf(typeId)}.");
        }
    }

    /// <summary>Reads a 26.3+ tag holder set: a VarInt of <c>count + 1</c>, where 0 means "a tag, named next" and anything else means that many inline item network ids.</summary>
    private static SlotDisplay ReadTagHolderSet(ref PacketReader r)
    {
        int size = r.ReadVarInt();
        if (size == 0)
            return new SlotDisplay.Tag(r.ReadString());

        var ids = new int[size - 1];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = r.ReadVarInt();

        return new SlotDisplay.TagItems(ids);
    }

    private static void WriteSlot(
        ref PacketWriter w,
        SlotDisplay slot,
        PacketCodecContext context,
        SlotDisplayTable table,
        StackWriter writeStack)
    {
        WriteSlotType(ref w, KindOf(slot), table);

        switch (slot)
        {
            case SlotDisplay.Empty:
            case SlotDisplay.AnyFuel:
                break;

            case SlotDisplay.Item item:
                w.WriteVarInt(item.ItemId);
                break;

            case SlotDisplay.Stack stack:
                writeStack(ref w, stack.Value, context);
                break;

            case SlotDisplay.Tag tag:
                if (table.TagIsHolderSet)
                {
                    w.WriteVarInt(0);
                    w.WriteString(tag.Name);
                }
                else
                    w.WriteString(tag.Name);
                break;

            case SlotDisplay.TagItems tagItems:
                if (!table.TagIsHolderSet)
                    throw new ProtocolViolationException(
                        "Inline tag holder sets have no wire form on this protocol's string-form tag payload.");

                w.WriteVarInt(tagItems.ItemIds.Count + 1);
                foreach (int id in tagItems.ItemIds)
                    w.WriteVarInt(id);
                break;

            case SlotDisplay.SmithingTrim trim:
                WriteSlot(ref w, trim.Base, context, table, writeStack);
                WriteSlot(ref w, trim.Material, context, table, writeStack);
                WriteTrimPattern(ref w, trim.Pattern, context, table, writeStack);
                break;

            case SlotDisplay.WithRemainder remainder:
                WriteSlot(ref w, remainder.Input, context, table, writeStack);
                WriteSlot(ref w, remainder.Remainder, context, table, writeStack);
                break;

            case SlotDisplay.Composite composite:
                WriteSlotList(ref w, composite.Contents, context, table, writeStack);
                break;

            case SlotDisplay.WithAnyPotion potion:
                WriteSlot(ref w, potion.Display, context, table, writeStack);
                break;

            case SlotDisplay.OnlyWithComponent gated:
                WriteSlot(ref w, gated.Source, context, table, writeStack);
                w.WriteVarInt(gated.ComponentId);
                break;

            case SlotDisplay.Dyed dyed:
                WriteSlot(ref w, dyed.Dye, context, table, writeStack);
                WriteSlot(ref w, dyed.Target, context, table, writeStack);
                break;

            default:
                throw new ProtocolViolationException(
                    $"Unhandled slot_display variant {slot.GetType().Name} in recipe_book_add.");
        }
    }

    /// <summary>Writes the era's wire id for a variant, refusing a variant this era does not register.</summary>
    /// <remarks>This is the guard that makes cross-era misuse loud rather than silent. 26.1 INSERTED three variants at ids 2, 3 and 7, so a 26.1-only <c>dyed</c> has no correct id at all on 1.21.2; writing some nearby id would put a plausible but wrong type on the wire and corrupt every byte after it.</remarks>
    private static void WriteSlotType(ref PacketWriter w, SlotDisplayKind kind, SlotDisplayTable table)
    {
        int typeId = table.IdOf(kind);
        if (typeId < 0)
            throw new ProtocolViolationException(
                $"slot_display kind {kind} does not exist in this protocol's registry ({table.Count} variants).");

        w.WriteVarInt(typeId);
    }

    /// <summary>Reads a trim-pattern holder: a VarInt of <c>id + 1</c>, or <c>0</c> followed by the direct form.</summary>
    private static TrimPatternDisplay ReadTrimPatternHolder(ref PacketReader r)
    {
        int raw = r.ReadVarInt();
        if (raw != 0)
            return new TrimPatternDisplay.Registry(raw - 1);

        // The inline form carries a Component whose wire shape depends on the connection's component era and NBT format, neither of which is available here. Rejecting the form lets the caller retain the entries already decoded and report the remainder as undecoded.
        //
        // Reachable only if a server sends a trim pattern that is NOT a registry entry inside a recipe display. Registered trim patterns use the registry branch, so this branch is unreachable.
        throw new ProtocolViolationException(
            "recipe_book_add carries an inline trim pattern, which needs the connection's component era to read.");
    }

    private static void WriteTrimPattern(
        ref PacketWriter w,
        TrimPatternDisplay pattern,
        PacketCodecContext context,
        SlotDisplayTable table,
        StackWriter writeStack)
    {
        switch (pattern)
        {
            case TrimPatternDisplay.Registry registry when table.TrimPatternIsHolder:
                w.WriteVarInt(registry.PatternId + 1);
                break;

            case TrimPatternDisplay.Nested nested when !table.TrimPatternIsHolder:
                WriteSlot(ref w, nested.Pattern, context, table, writeStack);
                break;

            default:
                throw new ProtocolViolationException(
                    $"smithing_trim pattern form {pattern.GetType().Name} does not match this protocol, whose " +
                    $"pattern field is {(table.TrimPatternIsHolder ? "a Holder<TrimPattern>" : "a nested SlotDisplay")}.");
        }
    }

    /// <summary>Reads an ingredient holder set: a VarInt of <c>count + 1</c>, where 0 means "a tag, named next" and anything else means that many inline ids.</summary>
    private static RecipeIngredient ReadHolderSet(ref PacketReader r)
    {
        int size = r.ReadVarInt();
        if (size == 0)
            return new RecipeIngredient.Tag(r.ReadString());

        var ids = new int[size - 1];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = r.ReadVarInt();

        return new RecipeIngredient.Items(ids);
    }

    private static void WriteHolderSet(ref PacketWriter w, RecipeIngredient ingredient)
    {
        switch (ingredient)
        {
            case RecipeIngredient.Tag tag:
                w.WriteVarInt(0);
                w.WriteString(tag.Name);
                break;

            case RecipeIngredient.Items items:
                w.WriteVarInt(items.ItemIds.Count + 1);
                foreach (int id in items.ItemIds)
                    w.WriteVarInt(id);

                break;

            default:
                throw new ProtocolViolationException(
                    $"Unhandled ingredient form {ingredient.GetType().Name} in recipe_book_add.");
        }
    }

    // Decode-time summaries
    //
    // These populate RecipeBookEntry's listing fields. They read the tree and never the wire, so they cannot affect byte fidelity; the encoder ignores every one of them.

    private static RecipeDisplayKind KindOf(RecipeDisplay display) => display switch
    {
        RecipeDisplay.CraftingShapeless => RecipeDisplayKind.CraftingShapeless,
        RecipeDisplay.CraftingShaped => RecipeDisplayKind.CraftingShaped,
        RecipeDisplay.Furnace => RecipeDisplayKind.Furnace,
        RecipeDisplay.Stonecutter => RecipeDisplayKind.Stonecutter,
        RecipeDisplay.Smithing => RecipeDisplayKind.Smithing,
        _ => throw new ProtocolViolationException($"Unhandled recipe_display variant {display.GetType().Name}."),
    };

    private static SlotDisplayKind KindOf(SlotDisplay slot) => slot switch
    {
        SlotDisplay.Empty => SlotDisplayKind.Empty,
        SlotDisplay.AnyFuel => SlotDisplayKind.AnyFuel,
        SlotDisplay.Item => SlotDisplayKind.Item,
        SlotDisplay.Stack => SlotDisplayKind.ItemStack,
        SlotDisplay.Tag => SlotDisplayKind.Tag,
        SlotDisplay.TagItems => SlotDisplayKind.Tag,
        SlotDisplay.SmithingTrim => SlotDisplayKind.SmithingTrim,
        SlotDisplay.WithRemainder => SlotDisplayKind.WithRemainder,
        SlotDisplay.Composite => SlotDisplayKind.Composite,
        SlotDisplay.WithAnyPotion => SlotDisplayKind.WithAnyPotion,
        SlotDisplay.OnlyWithComponent => SlotDisplayKind.OnlyWithComponent,
        SlotDisplay.Dyed => SlotDisplayKind.Dyed,
        _ => throw new ProtocolViolationException($"Unhandled slot_display variant {slot.GetType().Name}."),
    };

    /// <summary>The RESULT slot of a display: the only one a recipe book needs, because it is what names the recipe.</summary>
    private static SlotDisplay ResultOf(RecipeDisplay display) => display switch
    {
        RecipeDisplay.CraftingShapeless shapeless => shapeless.Result,
        RecipeDisplay.CraftingShaped shaped => shaped.Result,
        RecipeDisplay.Furnace furnace => furnace.Result,
        RecipeDisplay.Stonecutter stonecutter => stonecutter.Result,
        RecipeDisplay.Smithing smithing => smithing.Result,
        _ => throw new ProtocolViolationException($"Unhandled recipe_display variant {display.GetType().Name}."),
    };

    /// <summary>Resolves a slot display to the single item it stands for, when it stands for exactly one.</summary>
    /// <remarks>These are listing rules, not a general evaluation: a <c>with_remainder</c> is named after its INPUT (the remainder is what is left behind), a <c>dyed</c> after its TARGET, and a <c>composite</c> after the first alternative that resolves at all, because its members are alternatives and none of them is "the" result. Empty, any_fuel, tag and smithing_trim stand for zero or many items, never one.</remarks>
    private static SlotResult ResolveResult(SlotDisplay slot, PacketCodecContext context)
    {
        switch (slot)
        {
            case SlotDisplay.Item item:
                {
                    Identifier id = context.Registries.Items.TryGet(item.ItemId, out var definition)
                        ? definition.Id
                        : default;
                    return new SlotResult(item.ItemId, id, 1);
                }

            case SlotDisplay.Stack stack:
                return stack.Value.IsEmpty
                    ? SlotResult.None
                    : new SlotResult(stack.Value.Item.NetworkId, stack.Value.Item.Id, stack.Value.Count);

            case SlotDisplay.WithRemainder remainder:
                return ResolveResult(remainder.Input, context);

            case SlotDisplay.WithAnyPotion potion:
                return ResolveResult(potion.Display, context);

            case SlotDisplay.OnlyWithComponent gated:
                return ResolveResult(gated.Source, context);

            case SlotDisplay.Dyed dyed:
                return ResolveResult(dyed.Target, context);

            case SlotDisplay.Composite composite:
                {
                    SlotResult first = SlotResult.None;
                    foreach (SlotDisplay one in composite.Contents)
                    {
                        if (first.ItemId >= 0)
                            break;

                        first = ResolveResult(one, context);
                    }

                    return first;
                }

            default:
                return SlotResult.None;
        }
    }

    /// <summary>What a slot display resolved to: an item id and a count, or nothing.</summary>
    private readonly record struct SlotResult(int ItemId, Identifier Id, int Count)
    {
        public static SlotResult None => new(-1, default, 0);
    }
}
