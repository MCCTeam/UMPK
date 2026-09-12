using Umpk.Game.Items;

namespace Umpk.Game.Tests.Items;

/// <summary>In-memory <see cref="ILegacyItemBridgeSource"/> containing the well-known NBT keys and a subset of legacy numeric enchantment ids.</summary>
internal sealed class FakeItemBridgeSource : ILegacyItemBridgeSource
{
    private readonly Dictionary<string, Identifier> _nbtKeys = new(StringComparer.Ordinal)
    {
        ["display.Name"] = Identifier.Minecraft("custom_name"),
        ["display.Lore"] = Identifier.Minecraft("lore"),
        ["ench"] = Identifier.Minecraft("enchantments"),
        ["Unbreakable"] = Identifier.Minecraft("unbreakable"),
    };

    private readonly Dictionary<int, Identifier> _enchantments = new()
    {
        [0] = Identifier.Minecraft("protection"),
        [16] = Identifier.Minecraft("sharpness"),
        [34] = Identifier.Minecraft("unbreaking"),
        [32] = Identifier.Minecraft("efficiency"),
    };

    public bool TryMapNbtKey(string era, string nbtKey, out Identifier componentId)
    {
        if (era == "V1_8" && _nbtKeys.TryGetValue(nbtKey, out componentId))
            return true;

        componentId = default;
        return false;
    }

    public bool TryMapEnchantmentId(string era, int numericId, out Identifier enchantmentId)
    {
        if (era == "V1_8" && _enchantments.TryGetValue(numericId, out enchantmentId))
            return true;

        enchantmentId = default;
        return false;
    }
}
