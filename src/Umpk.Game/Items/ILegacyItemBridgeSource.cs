namespace Umpk.Game.Items;

/// <summary>The per-era data seam for the legacy NBT-to-component bridge. An era key selects well-known NBT keys mapped to component ids and the numeric enchantment-id map for that era.</summary>
public interface ILegacyItemBridgeSource
{
    /// <summary>Resolves the component id a legacy NBT key maps to for the given era (e.g. <c>display.Name</c> to <c>minecraft:custom_name</c>). Returns false when the key is not well-known for the era.</summary>
    bool TryMapNbtKey(string era, string nbtKey, out Identifier componentId);

    /// <summary>Resolves a legacy numeric enchantment id to its namespaced id for the given era. Returns false when the id is unknown for the era.</summary>
    bool TryMapEnchantmentId(string era, int numericId, out Identifier enchantmentId);
}
