using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;

namespace Umpk.Client.Actions;

internal enum MiningEnchantmentFormat
{
    NumericShortNbt,
    NamedIntNbt,
    ClampedNamedIntNbt,
    Components,
}

/// <summary>The native enchantment lookup used by player mining, including its NBT id and level shape and the era-specific duplicate rule.</summary>
internal static class VanillaMiningEnchantments
{
    public static bool TryGetLevel(ItemStack item, Identifier enchantment, MiningEnchantmentFormat format, out int level)
    {
        level = 0;
        if (item.IsEmpty)
            return false;
        if (format == MiningEnchantmentFormat.Components)
        {
            if (item.Components.TryGet(DataComponents.Enchantments, out EnchantmentsComponent? component))
                for (int index = component.Enchantments.Count - 1; index >= 0; index--)
                {
                    EnchantmentInstance instance = component.Enchantments[index];
                    if (!instance.Enchantment.IsDefault && instance.Enchantment.Id == enchantment)
                    {
                        level = instance.Level;
                        return true;
                    }
                }
            return false;
        }
        if (!item.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy))
            return false;

        bool numeric = format == MiningEnchantmentFormat.NumericShortNbt;
        NbtList? entries = legacy.Nbt.GetList(numeric ? "ench" : "Enchantments");
        if (entries is null)
            return false;
        foreach (NbtTag tag in entries)
        {
            if (tag is not NbtCompound entry)
                continue;
            bool matches = numeric
                ? entry.TryGet("id", out NbtNumeric? numericId)
                    && JavaGameData.LegacyItemBridgeSource.TryMapEnchantmentId(JavaGameData.LegacyItemBridgeEra, numericId.AsShort, out Identifier mapped)
                    && mapped == enchantment
                : entry.TryGet("id", out NbtString? namedId)
                    && Identifier.TryParse(namedId.Value, out Identifier parsed)
                    && parsed == enchantment;
            if (!matches)
                continue;

            // First matching entry wins, including zero. Named levels became int in 1.13 and gained the 0..255 clamp in 1.15.
            level = numeric ? entry.GetShort("lvl") : entry.GetInt("lvl");
            if (format == MiningEnchantmentFormat.ClampedNamedIntNbt)
                level = Math.Clamp(level, 0, 255);
            return true;
        }
        return false;
    }
}
