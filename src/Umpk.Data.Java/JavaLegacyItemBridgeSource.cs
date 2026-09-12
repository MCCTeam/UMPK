using Umpk.Game.Items;

namespace Umpk.Data.Java;

/// <summary>The runtime <see cref="ILegacyItemBridgeSource"/> for the <c>V1_8</c> era.</summary>
/// <remarks>
/// <para>The assembly has no dataset file I/O, so this small table is kept directly in runtime code. Its entries must stay synchronized with the validated dataset table.</para>
/// <para>One era only, deliberately: vanilla's numeric enchantment id space did not change between 1.8 and 1.12.2 (the whole pre-1.13 legacy-stack band), and 1.13 replaced numeric NBT enchantment ids with namespaced strings outright, so no table is needed from there on - the string shape is compared directly and never consults <see cref="TryMapEnchantmentId"/>. So <c>V1_8</c> is the only era key this ever needs.</para>
/// </remarks>
internal sealed class JavaLegacyItemBridgeSource : ILegacyItemBridgeSource
{
    /// <summary>The only era this source's data covers (see remarks). Re-exported publicly as <see cref="JavaGameData.LegacyItemBridgeEra"/> since this class itself is internal.</summary>
    internal const string V1_8 = "V1_8";

    // Numeric enchantment ids for the V1_8 era.
    private static readonly Dictionary<int, Identifier> EnchantmentIdsV1_8 = new()
    {
        [0] = Identifier.Minecraft("protection"),
        [1] = Identifier.Minecraft("fire_protection"),
        [2] = Identifier.Minecraft("feather_falling"),
        [3] = Identifier.Minecraft("blast_protection"),
        [4] = Identifier.Minecraft("projectile_protection"),
        [5] = Identifier.Minecraft("respiration"),
        [6] = Identifier.Minecraft("aqua_affinity"),
        [7] = Identifier.Minecraft("thorns"),
        [8] = Identifier.Minecraft("depth_strider"),
        [16] = Identifier.Minecraft("sharpness"),
        [17] = Identifier.Minecraft("smite"),
        [18] = Identifier.Minecraft("bane_of_arthropods"),
        [19] = Identifier.Minecraft("knockback"),
        [20] = Identifier.Minecraft("fire_aspect"),
        [21] = Identifier.Minecraft("looting"),
        [32] = Identifier.Minecraft("efficiency"),
        [33] = Identifier.Minecraft("silk_touch"),
        [34] = Identifier.Minecraft("unbreaking"),
        [35] = Identifier.Minecraft("fortune"),
        [48] = Identifier.Minecraft("power"),
        [49] = Identifier.Minecraft("punch"),
        [50] = Identifier.Minecraft("flame"),
        [51] = Identifier.Minecraft("infinity"),
        [61] = Identifier.Minecraft("luck_of_the_sea"),
        [62] = Identifier.Minecraft("lure"),
    };

    // Mirrors the same file's V1_8 "nbt_to_component" table for display and durability metadata.
    private static readonly Dictionary<string, Identifier> NbtToComponentV1_8 = new()
    {
        ["display.Name"] = Identifier.Minecraft("custom_name"),
        ["display.Lore"] = Identifier.Minecraft("lore"),
        ["ench"] = Identifier.Minecraft("enchantments"),
        ["Unbreakable"] = Identifier.Minecraft("unbreakable"),
    };

    /// <inheritdoc/>
    public bool TryMapNbtKey(string era, string nbtKey, out Identifier componentId)
    {
        ArgumentNullException.ThrowIfNull(era);
        ArgumentNullException.ThrowIfNull(nbtKey);
        if (era == V1_8 && NbtToComponentV1_8.TryGetValue(nbtKey, out Identifier mapped))
        {
            componentId = mapped;
            return true;
        }

        componentId = default;
        return false;
    }

    /// <inheritdoc/>
    public bool TryMapEnchantmentId(string era, int numericId, out Identifier enchantmentId)
    {
        ArgumentNullException.ThrowIfNull(era);
        if (era == V1_8 && EnchantmentIdsV1_8.TryGetValue(numericId, out Identifier mapped))
        {
            enchantmentId = mapped;
            return true;
        }

        enchantmentId = default;
        return false;
    }
}
