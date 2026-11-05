using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Client.Actions;

/// <summary>The held-stack comparison used by the native destroy-target cache, independently of stack count.</summary>
internal static class VanillaMiningItemIdentity
{
    private const int ItemComponentsProtocol = 766;
    private const int ListCustomModelDataProtocol = 769;
    private const int LastMeasuredCustomModelDataProtocol = 777;
    private const int ModernPredicateProtocol = 770;
    private const int LastMeasuredPredicateProtocol = 777;
    private const int LastMeasuredLoreProtocol = 777;
    private const int LastMeasuredEnchantmentProtocol = 777;

    public static bool Matches(ItemStack current, ItemStack previous, int protocol)
        => TryMatches(current, previous, protocol) is true;

    public static bool? TryMatches(ItemStack current, ItemStack previous, int protocol)
    {
        // A locally supplied out-of-era component ID has no native registry key, even when both stacks share that invalid value.
        if (HasNativeTooltipIdentity(protocol)
            && (!TooltipKeysAreDeclared(current.Components.Get(DataComponents.TooltipDisplay), protocol)
                || !TooltipKeysAreDeclared(previous.Components.Get(DataComponents.TooltipDisplay), protocol)))
            return null;
        if (ReferenceEquals(current, previous))
            return true;
        // The flattening moved damage into the compared tag. Component-era clients compare the complete effective component map.
        if (protocol >= 393)
        {
            if (HasNativeTooltipIdentity(protocol) && !current.IsEmpty && !previous.IsEmpty && current.Item.Equals(previous.Item) && !ReferenceEquals(current.Components, previous.Components))
            {
                bool? tooltip = TooltipMapsMatch(current, previous, protocol);
                if (tooltip is not true)
                    return tooltip;
            }
            if (HasNativeLoreIdentity(protocol) && !current.IsEmpty && !previous.IsEmpty && current.Item.Equals(previous.Item) && !ReferenceEquals(current.Components, previous.Components))
            {
                bool? lore = LoreMapsMatch(current.Components, previous.Components);
                if (lore is not true)
                    return lore;
            }
            if (protocol is >= ItemComponentsProtocol and < ListCustomModelDataProtocol && !current.IsEmpty && !previous.IsEmpty && current.Item.Equals(previous.Item) && !ReferenceEquals(current.Components, previous.Components))
            {
                bool? customModel = LegacyCustomModelMapsMatch(current.Components, previous.Components);
                if (customModel is not true)
                    return customModel;
            }
            if (current.IsSameItemSameComponents(previous))
                return true;
            if (protocol is < ItemComponentsProtocol or > LastMeasuredEnchantmentProtocol || current.IsEmpty || previous.IsEmpty || !current.Item.Equals(previous.Item))
                return false;
            return ComponentMapsMatch(current.Components, previous.Components, protocol);
        }
        if (current.IsEmpty || previous.IsEmpty)
            return current.IsEmpty && previous.IsEmpty;
        if (!current.Item.Equals(previous.Item))
            return false;

        // 1.8.9 bda.a(BlockPos), through 1.12.2 bsa.b(BlockPos): same item, equal tags, then either a damageable CURRENT stack or equal metadata. ItemStack.isDamageable excludes Unbreakable stacks.
        NbtCompound? currentTag = current.Components.Get(DataComponents.LegacyNbt)?.Nbt;
        NbtCompound? previousTag = previous.Components.Get(DataComponents.LegacyNbt)?.Nbt;
        if (currentTag is null ? previousTag is not null : !currentTag.Equals(previousTag))
            return false;
        return IsLegacyDamageableItem(current.Item.Id) && !(currentTag?.GetBool("Unbreakable") ?? false)
            || Math.Max(0, current.Damage) == Math.Max(0, previous.Damage);
    }

    private static bool? ComponentMapsMatch(DataComponentMap current, DataComponentMap previous, int protocol)
    {
        bool nativeLore = HasNativeLoreIdentity(protocol);
        bool nativeTooltip = HasNativeTooltipIdentity(protocol);
        int currentCount = current.Count - (nativeLore && current.Has(DataComponents.Lore) ? 1 : 0) - (nativeTooltip && current.Has(DataComponents.TooltipDisplay) ? 1 : 0);
        int previousCount = previous.Count - (nativeLore && previous.Has(DataComponents.Lore) ? 1 : 0) - (nativeTooltip && previous.Has(DataComponents.TooltipDisplay) ? 1 : 0);
        if (currentCount != previousCount)
            return false;
        foreach (DataComponentEntry entry in current.Effective)
        {
            if (nativeLore && entry.Type.Equals(DataComponents.Lore) || nativeTooltip && entry.Type.Equals(DataComponents.TooltipDisplay))
                continue;
            bool? match = false;
            foreach (DataComponentEntry other in previous.Effective)
            {
                if (!entry.Type.Equals(other.Type))
                    continue;
                // Native AdventureModePredicate, HolderSet.Direct and StatePropertiesPredicate compare ordered values. Their public wire models retain IReadOnlyList without structural record equality.
                match = ComponentValuesMatch(entry.Value, other.Value, protocol);
                break;
            }
            if (match is not true)
                return match;
        }
        return true;
    }

    private static bool HasNativeTooltipIdentity(int protocol) => protocol is >= 770 and <= 777;

    private static bool TooltipKeysAreDeclared(TooltipDisplayComponent? display, int protocol)
    {
        if (display is null)
            return true;
        int count = protocol switch
        {
            >= 770 and <= 773 => ComponentIds.V1_21_5.Length,
            774 => ComponentIds.V1_21_11.Length,
            775 => ComponentIds.V26_1.Length,
            776 => ComponentIds.V26_2.Length,
            777 => ComponentIds.V26_3.Length,
            _ => 0,
        };
        foreach (int id in display.HiddenComponents)
            if (id < 0 || id >= count)
                return false;
        return true;
    }

    private static bool? TooltipMapsMatch(ItemStack current, ItemStack previous, int protocol)
    {
        TooltipDisplayComponent? display = current.Components.Get(DataComponents.TooltipDisplay);
        TooltipDisplayComponent? other = previous.Components.Get(DataComponents.TooltipDisplay);
        bool removed = display is null && HasTooltipRemoval(current.Components);
        bool otherRemoved = other is null && HasTooltipRemoval(previous.Components);
        if (removed || otherRemoved)
        {
            if (removed && otherRemoved)
                return true;
            if (removed ? other is not null : display is not null)
                return false;
            return HasCanonicalTooltipDefault(current.Item, protocol) ? false : null;
        }
        // Equal omissions share this item's same prototype. Comparing an omission with a value needs the source-proven native default.
        if (display is null && other is null)
            return true;
        if ((display is null || other is null) && !HasCanonicalTooltipDefault(current.Item, protocol))
            return null;
        return TooltipValuesMatch(display, other);
    }

    private static bool HasCanonicalTooltipDefault(RegistryEntry<ItemDefinition> item, int protocol)
        => !item.IsDefault && JavaGameData.Registries(protocol).Items.TryGet(item.NetworkId, out RegistryEntry<ItemDefinition> native)
            && native.Id.Equals(item.Id) && ReferenceEquals(native.Value, item.Value);

    private static bool TooltipValuesMatch(TooltipDisplayComponent? current, TooltipDisplayComponent? previous)
    {
        if ((current?.HideTooltip ?? false) != (previous?.HideTooltip ?? false))
            return false;
        IReadOnlyList<int> hidden = current?.HiddenComponents ?? [];
        IReadOnlyList<int> other = previous?.HiddenComponents ?? [];
        if (hidden.Count == 0 || other.Count == 0)
            return hidden.Count == other.Count;
        // Original ReferenceLinkedOpenHashSet equality ignores order and duplicate input. Re-read mutable modeled values; no cached identity proof is retained.
        return ReferenceEquals(hidden, other) || new HashSet<int>(hidden).SetEquals(other);
    }

    private static bool HasTooltipRemoval(DataComponentMap components)
    {
        if (components.HasNoPatch)
            return false;
        foreach (DataComponentEntry entry in components.Patch)
            if (entry.IsRemoval && entry.Type.Equals(DataComponents.TooltipDisplay))
                return true;
        return false;
    }

    private static bool HasNativeLoreIdentity(int protocol)
        => protocol is >= ItemComponentsProtocol and <= LastMeasuredLoreProtocol;

    private static bool? LoreMapsMatch(DataComponentMap current, DataComponentMap previous)
    {
        LoreComponent? lore = current.Get(DataComponents.Lore);
        LoreComponent? other = previous.Get(DataComponents.Lore);
        if (lore is not null && other is not null)
            return LoreValuesMatch(lore, other);
        bool removed = lore is null && HasLoreRemoval(current);
        bool otherRemoved = other is null && HasLoreRemoval(previous);
        if (removed || otherRemoved)
            return removed == otherRemoved;
        // Every vanilla item carries Lore.EMPTY by default, including when the bounded local prototype omits it. Explicit wire removal is distinct.
        return (lore?.Lines.Count ?? 0) == 0 && (other?.Lines.Count ?? 0) == 0;
    }

    private static bool? LoreValuesMatch(LoreComponent current, LoreComponent previous)
    {
        if (ReferenceEquals(current, previous))
            return true;
        if (!ItemComponentCodecs.IsLoreIdentityComplete(current) || !ItemComponentCodecs.IsLoreIdentityComplete(previous))
            return null;
        return current.Lines.SequenceEqual(previous.Lines);
    }

    private static bool HasLoreRemoval(DataComponentMap components)
    {
        if (components.HasNoPatch)
            return false;
        foreach (DataComponentEntry entry in components.Patch)
            if (entry.IsRemoval && entry.Type.Equals(DataComponents.Lore))
                return true;
        return false;
    }

    private static bool? LegacyCustomModelMapsMatch(DataComponentMap current, DataComponentMap previous)
    {
        CustomModelDataComponent? model = current.Get(DataComponents.CustomModelData);
        CustomModelDataComponent? other = previous.Get(DataComponents.CustomModelData);
        if (model is null || other is null)
            return model is null && other is null;
        return LegacyCustomModelValuesMatch(model, other);
    }

    private static bool? LegacyCustomModelValuesMatch(CustomModelDataComponent current, CustomModelDataComponent previous)
    {
        if (ReferenceEquals(current, previous))
            return true;
        // The legacy stream carries an integer; the shared float projection alone cannot identify large received values.
        if (!ItemComponentCodecs.TryGetLegacyCustomModelDataInteger(current, out int value)
            || !ItemComponentCodecs.TryGetLegacyCustomModelDataInteger(previous, out int other))
            return null;
        return value == other;
    }

    private static bool? ComponentValuesMatch(object? current, object? previous, int protocol)
    {
        if (HasNativeTooltipIdentity(protocol) && current is TooltipDisplayComponent display && previous is TooltipDisplayComponent otherDisplay)
            return !TooltipKeysAreDeclared(display, protocol) || !TooltipKeysAreDeclared(otherDisplay, protocol)
                ? null
                : TooltipValuesMatch(display, otherDisplay);
        if (HasNativeLoreIdentity(protocol) && current is LoreComponent lore && previous is LoreComponent otherLore)
            return LoreValuesMatch(lore, otherLore);
        if (protocol is >= ItemComponentsProtocol and <= LastMeasuredCustomModelDataProtocol && current is CustomModelDataComponent customModel && previous is CustomModelDataComponent otherModel)
            return protocol < ListCustomModelDataProtocol
                ? LegacyCustomModelValuesMatch(customModel, otherModel)
                : CustomModelValuesMatch(customModel, otherModel);
        if (current is EnchantmentsComponent enchantments && previous is EnchantmentsComponent otherEnchantments)
            return EnchantmentValuesMatch(enchantments.Enchantments, otherEnchantments.Enchantments, enchantments.ShowInTooltip, otherEnchantments.ShowInTooltip, protocol);
        if (current is StoredEnchantmentsComponent stored && previous is StoredEnchantmentsComponent otherStored)
            return EnchantmentValuesMatch(stored.Enchantments, otherStored.Enchantments, stored.ShowInTooltip, otherStored.ShowInTooltip, protocol);
        if (current is AdventureModePredicateComponent predicate && previous is AdventureModePredicateComponent otherPredicate)
            return protocol <= LastMeasuredPredicateProtocol ? PredicateValuesMatch(predicate, otherPredicate, protocol) : false;
        return Equals(current, previous);
    }

    private static bool CustomModelValuesMatch(CustomModelDataComponent current, CustomModelDataComponent previous)
    {
        if (current.Floats.Count != previous.Floats.Count)
            return false;
        for (int index = 0; index < current.Floats.Count; index++)
        {
            // Java Float.equals distinguishes signed zero and canonicalizes NaNs in the ordered native lists.
            int bits = float.IsNaN(current.Floats[index]) ? 0x7FC00000 : BitConverter.SingleToInt32Bits(current.Floats[index]);
            int otherBits = float.IsNaN(previous.Floats[index]) ? 0x7FC00000 : BitConverter.SingleToInt32Bits(previous.Floats[index]);
            if (bits != otherBits)
                return false;
        }
        return current.Flags.SequenceEqual(previous.Flags)
            && current.Strings.SequenceEqual(previous.Strings)
            && current.Colors.SequenceEqual(previous.Colors);
    }

    private static bool EnchantmentValuesMatch(IReadOnlyList<EnchantmentInstance> current, IReadOnlyList<EnchantmentInstance> previous, bool currentTooltip, bool previousTooltip, int protocol)
    {
        if (protocol < ModernPredicateProtocol && currentTooltip != previousTooltip)
            return false;
        if (current.Count == 0 || previous.Count == 0)
            return current.Count == previous.Count;

        // Both native component forms use ItemEnchantments: map equality ignores insertion order, keeps explicit zero values and compares the final value of each duplicate key.
        var currentLevels = new Dictionary<RegistryEntry<EnchantmentDefinition>, int>(current.Count);
        foreach (EnchantmentInstance entry in current)
        {
            if (entry.Enchantment.IsDefault)
                return false;
            currentLevels[entry.Enchantment] = entry.Level;
        }
        var previousLevels = new Dictionary<RegistryEntry<EnchantmentDefinition>, int>(previous.Count);
        foreach (EnchantmentInstance entry in previous)
        {
            if (entry.Enchantment.IsDefault)
                return false;
            previousLevels[entry.Enchantment] = entry.Level;
        }
        if (currentLevels.Count != previousLevels.Count)
            return false;
        foreach ((RegistryEntry<EnchantmentDefinition> key, int level) in currentLevels)
            if (!previousLevels.TryGetValue(key, out int otherLevel) || level != otherLevel)
                return false;
        return true;
    }

    private static bool? PredicateValuesMatch(AdventureModePredicateComponent current, AdventureModePredicateComponent previous, int protocol)
    {
        if (protocol < ModernPredicateProtocol && current.ShowInTooltip != previous.ShowInTooltip || current.Predicates.Count != previous.Predicates.Count)
            return false;
        for (int index = 0; index < current.Predicates.Count; index++)
        {
            BlockPredicateEntry entry = current.Predicates[index];
            BlockPredicateEntry other = previous.Predicates[index];
            if (entry.Blocks is { } blocks)
            {
                if (other.Blocks is not { } otherBlocks || blocks.Tag != otherBlocks.Tag || !blocks.Ids.SequenceEqual(otherBlocks.Ids))
                    return false;
            }
            else if (other.Blocks is not null)
                return false;
            if (entry.Properties is { } properties)
            {
                if (other.Properties is not { } otherProperties || !properties.SequenceEqual(otherProperties))
                    return false;
            }
            else if (other.Properties is not null)
                return false;
            if (entry.Nbt is null ? other.Nbt is not null : !entry.Nbt.Equals(other.Nbt))
                return false;
            if (protocol >= ModernPredicateProtocol && protocol <= LastMeasuredPredicateProtocol)
            {
                bool? match = MatcherValuesMatch(entry.ComponentMatchers, other.ComponentMatchers, protocol);
                if (match is not true)
                    return match;
            }
        }
        return true;
    }

    private static bool? MatcherValuesMatch(BlockPredicateComponentMatchers? current, BlockPredicateComponentMatchers? previous, int protocol)
    {
        if (current is null || previous is null || current.Exact.Count != previous.Exact.Count || current.Partial.Count != previous.Partial.Count)
            return false;
        // Native exact predicates retain ordered typed values; DATA map hashes cannot establish this equality.
        for (int index = 0; index < current.Exact.Count; index++)
        {
            DataComponentEntry entry = current.Exact[index];
            DataComponentEntry other = previous.Exact[index];
            if (entry.IsRemoval || other.IsRemoval || !entry.Type.Equals(other.Type))
                return false;
            bool? match = ComponentValuesMatch(entry.Value, other.Value, protocol);
            if (match is not true)
                return match;
        }
        // Only Damage is owned by the measured selected-predicate codec, so the native partial map has at most one owned entry.
        if (current.Partial.Count > 1)
            return false;
        if (current.Partial.Count == 0)
            return true;
        BlockPredicatePartialMatcher partial = current.Partial[0];
        BlockPredicatePartialMatcher otherPartial = previous.Partial[0];
        return !partial.IsAnyValue && !otherPartial.IsAnyValue
            && partial.Type == Identifier.Minecraft("damage") && partial.Type == otherPartial.Type
            && partial.Value is NbtCompound damage && otherPartial.Value is NbtCompound otherDamage
            && DamageBoundsMatch(damage, otherDamage, "durability")
            && DamageBoundsMatch(damage, otherDamage, "damage");
    }

    private static bool DamageBoundsMatch(NbtCompound current, NbtCompound previous, string name)
        => TryReadDamageBounds(current, name, out int? min, out int? max)
            && TryReadDamageBounds(previous, name, out int? otherMin, out int? otherMax)
            && min == otherMin && max == otherMax;

    private static bool TryReadDamageBounds(NbtCompound compound, string name, out int? min, out int? max)
    {
        min = null;
        max = null;
        if (!compound.TryGet(name, out NbtTag? value))
            return true;
        if (value is NbtInt point)
        {
            min = point.Value;
            max = point.Value;
            return true;
        }
        if (value is not NbtCompound bounds)
            return false;
        if (bounds.TryGet("min", out NbtTag? lower))
        {
            if (lower is not NbtInt minimum)
                return false;
            min = minimum.Value;
        }
        if (bounds.TryGet("max", out NbtTag? upper))
        {
            if (upper is not NbtInt maximum)
                return false;
            max = maximum.Value;
        }
        return true;
    }

    // Original item-registry/max-damage calls cover every item in all eleven pre-flattening datasets. There are 50 damageable item types in 1.8 and 52 after shield/elytra arrive; horse armor, buckets and block subtypes are not damageable.
    private static bool IsLegacyDamageableItem(Identifier item)
        => item.Namespace == "minecraft" && item.Path is
            "wooden_sword" or "wooden_shovel" or "wooden_pickaxe" or "wooden_axe" or "wooden_hoe"
            or "stone_sword" or "stone_shovel" or "stone_pickaxe" or "stone_axe" or "stone_hoe"
            or "iron_sword" or "iron_shovel" or "iron_pickaxe" or "iron_axe" or "iron_hoe"
            or "diamond_sword" or "diamond_shovel" or "diamond_pickaxe" or "diamond_axe" or "diamond_hoe"
            or "golden_sword" or "golden_shovel" or "golden_pickaxe" or "golden_axe" or "golden_hoe"
            or "leather_helmet" or "leather_chestplate" or "leather_leggings" or "leather_boots"
            or "chainmail_helmet" or "chainmail_chestplate" or "chainmail_leggings" or "chainmail_boots"
            or "iron_helmet" or "iron_chestplate" or "iron_leggings" or "iron_boots"
            or "diamond_helmet" or "diamond_chestplate" or "diamond_leggings" or "diamond_boots"
            or "golden_helmet" or "golden_chestplate" or "golden_leggings" or "golden_boots"
            or "bow" or "carrot_on_a_stick" or "fishing_rod" or "flint_and_steel" or "shears" or "shield" or "elytra";
}
