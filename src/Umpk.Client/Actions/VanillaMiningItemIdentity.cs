using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;

namespace Umpk.Client.Actions;

/// <summary>The held-stack comparison used by the native destroy-target cache, independently of stack count.</summary>
internal static class VanillaMiningItemIdentity
{
    public static bool Matches(ItemStack current, ItemStack previous, int protocol)
    {
        // The flattening moved damage into the compared tag. Component-era clients compare the complete effective component map.
        if (protocol >= 393)
            return current.IsSameItemSameComponents(previous);
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
