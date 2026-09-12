using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Game.Items;

/// <summary>One item representation across all versions: an item registry handle, a count, and a unified <see cref="DataComponentMap"/>. A pre-1.20.5 stack's NBT is NOT pre-bridged into components by the wire codecs: it arrives verbatim in <see cref="DataComponents.LegacyNbt"/>, and the era-neutral accessors here (<see cref="Book"/>, <see cref="TryGetEnchantmentLevel"/>) interpret it on demand, so a consumer does not branch on the NBT-versus-components split. The type is immutable; edits produce a new stack through <see cref="WithCount"/> / <see cref="WithComponents"/>.</summary>
public sealed class ItemStack : IEquatable<ItemStack>, Umpk.Game.Entities.IMetadataSlot
{
    /// <summary>The canonical empty stack (air, count 0, no components).</summary>
    public static ItemStack Empty { get; } = new();

    private ItemStack()
    {
        Item = default;
        Count = 0;
        Components = DataComponentMap.Empty;
    }

    /// <summary>Creates a stack.</summary>
    /// <param name="item">The item registry handle.</param>
    /// <param name="count">The stack count.</param>
    /// <param name="components">The component map, or null for an empty map.</param>
    /// <exception cref="ArgumentException"><paramref name="item"/> is an unbound handle.</exception>
    public ItemStack(RegistryEntry<ItemDefinition> item, int count, DataComponentMap? components = null)
    {
        if (item.IsDefault)
            throw new ArgumentException("An item stack requires a bound item registry handle.", nameof(item));

        Item = item;
        Count = count;
        Components = components ?? DataComponentMap.Empty;
    }

    /// <summary>The item registry handle. An unbound handle indicates the empty stack.</summary>
    public RegistryEntry<ItemDefinition> Item { get; }

    /// <summary>The stack count.</summary>
    public int Count { get; }

    /// <summary>The unified metadata surface.</summary>
    public DataComponentMap Components { get; }

    /// <summary>True when this stack is air or has a non-positive count.</summary>
    public bool IsEmpty => Item.IsDefault || Count <= 0;

    /// <summary>The maximum stack size, honoring a <c>max_stack_size</c> component override.</summary>
    public int MaxStackSize
    {
        get
        {
            if (Components.TryGet(DataComponents.MaxStackSize, out MaxStackSizeComponent? overriden))
                return overriden.Value;

            return Item.IsDefault ? 1 : Item.Value.MaxStackSize;
        }
    }

    /// <summary>The custom display name, or null when unset.</summary>
    public Component? CustomName =>
        Components.TryGet(DataComponents.CustomName, out CustomNameComponent? name) ? name.Name : null;

    /// <summary>The tooltip lore lines; empty when unset.</summary>
    public IReadOnlyList<Component> Lore =>
        Components.TryGet(DataComponents.Lore, out LoreComponent? lore) ? lore.Lines : [];

    /// <summary>The accumulated durability damage; 0 when unset.</summary>
    public int Damage =>
        Components.TryGet(DataComponents.Damage, out DamageComponent? d) ? d.Value : 0;

    /// <summary>The maximum durability; 0 when the item is not damageable.</summary>
    public int MaxDamage =>
        Components.TryGet(DataComponents.MaxDamage, out MaxDamageComponent? m) ? m.Value : 0;

    /// <summary>The applied enchantments; empty when unset. NOT era-neutral: this reads only the 1.20.5+ structured component. A pre-1.20.5 stack's enchantments (if any) arrive on the wire as NBT inside <see cref="DataComponents.LegacyNbt"/> instead, which this never inspects, so this is always empty for a legacy-format stack even when the player is genuinely wearing an enchanted item. Use <see cref="TryGetEnchantmentLevel"/> to ask for one known enchantment's level era-neutrally.</summary>
    public IReadOnlyList<EnchantmentInstance> Enchantments =>
        Components.TryGet(DataComponents.Enchantments, out EnchantmentsComponent? e) ? e.Enchantments : [];

    /// <summary>The level of one KNOWN enchantment on this stack, era-neutrally; false when it is absent. Checks the 1.20.5+ structured component first (the same value <see cref="Enchantments"/> reads) and, when that is absent, interprets the residual pre-1.20.5 <see cref="DataComponents.LegacyNbt"/> compound on demand - the same structured-component-first-then-legacy-NBT fallback <see cref="BookContent.From"/> already uses for book content, so a stack that carries both reports the authoritative structured value.</summary>
    /// <remarks>
    /// <para>Both legacy NBT shapes are covered: the pre-1.13 list under <c>ench</c> whose entries carry a NUMERIC <c>id</c> (resolved through <paramref name="legacyIds"/>'s per-era table), and the 1.13-1.20.4 list under <c>Enchantments</c> whose entries carry a namespaced STRING <c>id</c> (compared directly). Vanilla reads exactly one key per era: <c>ench</c> through 1.12.2 and <c>Enchantments</c> from 1.13. Therefore, reading both keys in both eras here is deliberate leniency, not vanilla behavior: a stack whose NBT era was misjudged would otherwise lose its level silently, and a key vanilla would not have written cannot appear on a vanilla wire anyway.</para>
    /// <para>Deliberately asks "what level is THIS enchantment" instead of returning enchantment handles the way <see cref="Enchantments"/> and <see cref="LegacyItemBridge"/> do. Producing a handle requires resolving the wire identity against a <see cref="Umpk.Game.Registries.Registry{T}"/> of <see cref="Umpk.Game.Registries.EnchantmentDefinition"/>, and that registry is empty in every live session today (nothing populates it: no emitter emits an enchantment table, and the config-phase registry sync installs dimension types and chat types only), which silently drops every enchantment on every protocol - the exact trap this method exists to avoid. A caller that already knows the <see cref="Identifier"/> it wants needs no registry at all, so this path works on a real session. The structured-component branch is still registry-bound (a component-era stack carries only a numeric holder id on the wire, so nothing else can name it), and is therefore only as live as the session's enchantment registry.</para>
    /// </remarks>
    /// <param name="enchantment">The enchantment to look for, e.g. <c>minecraft:depth_strider</c>.</param>
    /// <param name="legacyEra">The legacy-bridge era key (see <see cref="ILegacyItemBridgeSource"/>) selecting the numeric-id table. Read only for the pre-1.13 numeric shape; the string shape never consults it.</param>
    /// <param name="legacyIds">The per-era numeric enchantment-id table source.</param>
    /// <param name="level">The level found, or 0.</param>
    /// <exception cref="ArgumentNullException"><paramref name="legacyEra"/> or <paramref name="legacyIds"/> is null.</exception>
    public bool TryGetEnchantmentLevel(
        Identifier enchantment, string legacyEra, ILegacyItemBridgeSource legacyIds, out int level)
    {
        ArgumentNullException.ThrowIfNull(legacyEra);
        ArgumentNullException.ThrowIfNull(legacyIds);

        if (Components.TryGet(DataComponents.Enchantments, out EnchantmentsComponent? modern))
        {
            foreach (EnchantmentInstance instance in modern.Enchantments)
                if (!instance.Enchantment.IsDefault && instance.Enchantment.Id == enchantment)
                {
                    level = instance.Level;
                    return true;
                }

            level = 0;
            return false;
        }

        if (Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy) &&
            (TryReadLegacyLevel(legacy.Nbt.GetList("ench"), enchantment, legacyEra, legacyIds, out level) ||
             TryReadLegacyLevel(legacy.Nbt.GetList("Enchantments"), enchantment, legacyEra, legacyIds, out level)))
            return true;

        level = 0;
        return false;
    }

    /// <summary>Scans one legacy enchantment NBT list for a wanted enchantment. Entries store <c>lvl</c> as a short and <c>id</c> as either a namespaced string (1.13+) or a numeric id needing the era table.</summary>
    private static bool TryReadLegacyLevel(
        NbtList? list,
        Identifier enchantment,
        string legacyEra,
        ILegacyItemBridgeSource legacyIds,
        out int level)
    {
        if (list is not null)
            foreach (NbtTag element in list)
            {
                if (element is not NbtCompound entry)
                    continue;

                bool matches =
                    (entry.TryGet("id", out NbtString? byName) &&
                     Identifier.TryParse(byName.Value, out Identifier parsed) && parsed == enchantment) ||
                    (entry.TryGet("id", out NbtNumeric? byId) &&
                     legacyIds.TryMapEnchantmentId(legacyEra, byId.AsInt, out Identifier mapped) &&
                     mapped == enchantment);

                if (matches)
                {
                    level = entry.GetShort("lvl");
                    return true;
                }
            }

        level = 0;
        return false;
    }

    /// <summary>The decoded book content, or null when this stack carries no book data. Era-neutral: it reads the 1.20.5+ <c>written_book_content</c>/<c>writable_book_content</c> components and the pre-1.20.5 legacy NBT through the same view, so a consumer reading pages, title or author never branches on era and never touches NBT.</summary>
    public BookContent? Book => BookContent.From(Components);

    /// <summary>Returns a copy with a new count. A non-positive count yields <see cref="Empty"/>.</summary>
    public ItemStack WithCount(int count) =>
        IsEmpty || count <= 0 ? Empty : new ItemStack(Item, count, Components);

    /// <summary>Returns a copy grown by <paramref name="amount"/> (clamped at zero).</summary>
    public ItemStack Grow(int amount) => WithCount(Count + amount);

    /// <summary>Returns a copy shrunk by <paramref name="amount"/> (clamped at <see cref="Empty"/>).</summary>
    public ItemStack Shrink(int amount) => WithCount(Count - amount);

    /// <summary>Returns a copy with a new component map.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="components"/> is null.</exception>
    public ItemStack WithComponents(DataComponentMap components)
    {
        ArgumentNullException.ThrowIfNull(components);
        return IsEmpty ? Empty : new ItemStack(Item, Count, components);
    }

    /// <summary>Returns a copy with one component set (functional update through the map).</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ItemStack With<T>(DataComponentType<T> type, T value)
        where T : class => WithComponents(Components.With(type, value));

    /// <summary>Whether two stacks are the same item with equal components, ignoring count. The click simulator relies on this for merging.</summary>
    public bool IsSameItemSameComponents(ItemStack other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (IsEmpty || other.IsEmpty)
            return IsEmpty && other.IsEmpty;

        return Item.Equals(other.Item) && Components.EffectiveEquals(other.Components);
    }

    /// <summary>Whether two stacks are the same item, ignoring count and components.</summary>
    public bool IsSameItem(ItemStack other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (IsEmpty || other.IsEmpty)
            return IsEmpty && other.IsEmpty;

        return Item.Equals(other.Item);
    }

    /// <summary>Full value equality: same item, count, and effective components. Empty equals empty.</summary>
    public bool Equals(ItemStack? other)
    {
        if (other is null)
            return false;

        if (IsEmpty || other.IsEmpty)
            return IsEmpty && other.IsEmpty;

        return Count == other.Count && Item.Equals(other.Item) && Components.EffectiveEquals(other.Components);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ItemStack);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (IsEmpty)
            return 0;

        // Component values are excluded from the hash (their effective enumeration is not cheap and the count+item already disperse well); Equals stays authoritative.
        return HashCode.Combine(Item, Count);
    }

    /// <inheritdoc/>
    public override string ToString() => IsEmpty ? "ItemStack.Empty" : $"{Count}x {Item.Id}";
}
