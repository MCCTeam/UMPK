using Umpk.Game.Blocks;
using Umpk.Game.Items;

namespace Umpk.Client.Actions;

/// <summary>The native axe result after the separately owned activation, permission and cooldown phases.</summary>
internal readonly record struct VanillaAxeUse(bool HasRules, bool StripsBlocks, bool ChangesCopper, AxeBlockingIntent BlockingIntent, bool UsesComponents)
{
    // Original clients: inherited pass through 1.12.2, stripping from 1.13, copper from 1.17, shield intent from 1.21 and effective offhand BLOCKS_ATTACKS from 1.21.5. Protocol 777 replaces this class with data-driven transformations.
    public static VanillaAxeUse ForProtocol(int protocol) => new(protocol is >= 47 and <= 776, protocol >= 393, protocol >= 755, protocol >= 770 ? AxeBlockingIntent.Components : protocol >= 767 ? AxeBlockingIntent.Shield : AxeBlockingIntent.None, protocol >= 766);

    public bool SupportsStack(ItemStack stack)
    {
        if (stack.Components.HasNoPatch)
            return true;
        foreach (DataComponentEntry entry in stack.Components.Patch)
            if (!entry.Type.Equals(DataComponents.Damage) && (UsesComponents || !entry.Type.Equals(DataComponents.LegacyNbt)))
                return false;
        return true;
    }

    public bool TryPredict(Identifier item, BlockState clicked, Hand hand, ItemStack offhand, bool secondaryUse, out BlockUsePrediction prediction, bool offhandKnown = true)
    {
        prediction = BlockUsePrediction.Unknown;
        if (!HasRules || !item.IsMinecraft || item.Path is not ("wooden_axe" or "stone_axe" or "iron_axe" or "golden_axe" or "diamond_axe" or "netherite_axe" or "copper_axe"))
            return false;
        prediction = BlockUsePrediction.Pass;
        if (!StripsBlocks || !clicked.Block.Id.IsMinecraft)
            return true;
        string block = clicked.Block.Id.Path;
        bool transforms = block is "oak_log" or "oak_wood" or "spruce_log" or "spruce_wood" or "birch_log" or "birch_wood" or "jungle_log" or "jungle_wood" or "acacia_log" or "acacia_wood" or "dark_oak_log" or "dark_oak_wood"
            or "crimson_stem" or "crimson_hyphae" or "warped_stem" or "warped_hyphae" or "mangrove_log" or "mangrove_wood" or "bamboo_block" or "cherry_log" or "cherry_wood" or "pale_oak_log" or "pale_oak_wood"
            || ChangesCopper && IsCopperChange(block);
        if (!transforms)
            return true;
        if (BlockingIntent != AxeBlockingIntent.None && hand == Hand.Main && !secondaryUse && !offhandKnown)
        {
            prediction = BlockUsePrediction.Unknown;
            return true;
        }
        if (BlockingIntent != AxeBlockingIntent.None && hand == Hand.Main && !secondaryUse && !offhand.IsEmpty)
        {
            // Native reads the effective offhand stack. An unsupported patch is not evidence that BLOCKS_ATTACKS is absent.
            if (BlockingIntent == AxeBlockingIntent.Components && !offhand.Components.HasNoPatch && offhand.Components.Patch.Any(entry => !entry.Type.Equals(DataComponents.Damage)))
                prediction = BlockUsePrediction.Unknown;
            else if (offhand.Item.Id != Identifier.Minecraft("shield"))
                prediction = BlockUsePrediction.Success(localSwing: false);
        }
        else
            prediction = BlockUsePrediction.Success(localSwing: false);
        return true;
    }

    private static bool IsCopperChange(ReadOnlySpan<char> block)
    {
        bool waxed = block.StartsWith("waxed_", StringComparison.Ordinal);
        if (waxed)
            block = block[6..];
        int prefix = block.StartsWith("exposed_", StringComparison.Ordinal) ? 8 : block.StartsWith("weathered_", StringComparison.Ordinal) ? 10 : block.StartsWith("oxidized_", StringComparison.Ordinal) ? 9 : 0;
        if (!waxed && prefix == 0)
            return false;
        block = block[prefix..];
        return block is "copper" && prefix != 0 || block is "copper_block" && prefix == 0
            || block is "cut_copper" or "cut_copper_slab" or "cut_copper_stairs" or "chiseled_copper" or "copper_door" or "copper_trapdoor" or "copper_grate" or "copper_bulb"
                or "copper_bars" or "copper_lantern" or "copper_chain" or "copper_chest" or "copper_golem_statue" or "lightning_rod";
    }
}

internal enum AxeBlockingIntent : byte
{
    None,
    Shield,
    Components,
}
