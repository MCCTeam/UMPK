using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>The original shovel tool result after the separately owned block-activation and permission phases.</summary>
internal readonly record struct VanillaShovelUse(bool HasRules, bool CreatesPaths, bool ExpandedTargets, bool ExtinguishesCampfires, bool LegacyBlockStates, bool UsesComponents)
{
    // Original clients are audited on all 74 releases through 26.2. Protocol 47 retains 1.8.9's inherited no-op; 107 adds grass paths, 573 adds campfire dowse and 755 expands the path map. Protocol 777 replaces this class with data-driven transformations.
    public static VanillaShovelUse ForProtocol(int protocol) => new(protocol is >= 47 and <= 776, protocol >= 107, protocol >= 755, protocol >= 573, protocol < 393, protocol >= 766);

    public bool SupportsStack(ItemStack stack)
    {
        if (stack.Components.HasNoPatch)
            return true;
        // ShovelItem ignores damage and unrelated legacy NBT when selecting its client result. Global permissions/cooldowns run first; other modern patches remain unclassified.
        foreach (DataComponentEntry entry in stack.Components.Patch)
            if (!entry.Type.Equals(DataComponents.Damage) && (UsesComponents || !entry.Type.Equals(DataComponents.LegacyNbt)))
                return false;
        return true;
    }

    public bool TryPredict(Identifier item, BlockState clicked, World world, BlockPos position, Direction face, out BlockUsePrediction prediction)
    {
        prediction = BlockUsePrediction.Unknown;
        if (!HasRules || !item.IsMinecraft || item.Path is not ("wooden_shovel" or "stone_shovel" or "iron_shovel" or "golden_shovel" or "diamond_shovel" or "netherite_shovel" or "copper_shovel"))
            return false;
        prediction = BlockUsePrediction.Pass;
        if (!CreatesPaths || !clicked.Block.Id.IsMinecraft || face == Direction.Down)
            return true;
        string block = clicked.Block.Id.Path;
        if (ExtinguishesCampfires && block is "campfire" or "soul_campfire")
        {
            if (clicked.TryGetProperty("lit", out string lit) && lit is "true" or "false")
                prediction = lit == "true" ? BlockUsePrediction.Success(localSwing: false) : BlockUsePrediction.Pass;
            else
                prediction = BlockUsePrediction.Unknown;
            return true;
        }
        bool tillable = LegacyBlockStates ? block == "grass"
            : block == "grass_block" || ExpandedTargets && block is "dirt" or "coarse_dirt" or "podzol" or "mycelium" or "rooted_dirt";
        if (!tillable)
            return true;
        if (LegacyBlockStates && clicked.LegacyMeta != 0)
        {
            prediction = BlockUsePrediction.Unknown;
            return true;
        }
        BlockPos above = position.Above();
        BlockState upper = world.GetBlock(above);
        if (world.GetColumn(above) is null || !upper.IsValid)
            prediction = BlockUsePrediction.Unknown;
        else if (upper.IsAir)
            prediction = BlockUsePrediction.Success(localSwing: false);
        return true;
    }
}
