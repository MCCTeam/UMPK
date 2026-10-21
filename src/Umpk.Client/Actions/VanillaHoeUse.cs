using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>The original HoeItem use result; server-owned terrain, drops and durability remain authoritative.</summary>
internal readonly record struct VanillaHoeUse(bool HasRules, bool HasRootedDirt, bool UsesComponents, bool LegacyBlockStates)
{
    // Original client HoeItem sources are audited on all 74 releases through 26.2. Protocol 47 retains the 1.8.9 profile; legacy dirt variants are separate from flattened block names. Protocol 755 adds unconditional rooted dirt; 777 replaces HoeItem with data-driven BlockTransformer components.
    public static VanillaHoeUse ForProtocol(int protocol) => new(protocol is >= 47 and <= 776, protocol >= 755, protocol >= 766, protocol < 393);

    public bool SupportsStack(ItemStack stack)
    {
        if (stack.Components.HasNoPatch)
            return true;
        // ItemStack.useOn delegates to HoeItem after the separately owned permission/cooldown checks. HoeItem ignores legacy NBT and durability when choosing its client result; damage is applied only by the server. Other modern component patches remain unclassified.
        foreach (DataComponentEntry entry in stack.Components.Patch)
            if (!entry.Type.Equals(DataComponents.Damage) && (UsesComponents || !entry.Type.Equals(DataComponents.LegacyNbt)))
                return false;
        return true;
    }

    public bool TryPredict(Identifier item, BlockState clicked, World world, BlockPos position, Direction face, out BlockUsePrediction prediction)
    {
        prediction = BlockUsePrediction.Unknown;
        if (!HasRules || !item.IsMinecraft || item.Path is not ("wooden_hoe" or "stone_hoe" or "iron_hoe" or "golden_hoe" or "diamond_hoe" or "netherite_hoe" or "copper_hoe"))
            return false;
        prediction = BlockUsePrediction.Pass;
        if (!clicked.Block.Id.IsMinecraft)
            return true;
        string block = clicked.Block.Id.Path;
        if (HasRootedDirt && block == "rooted_dirt")
        {
            prediction = BlockUsePrediction.Success(localSwing: false);
            return true;
        }
        bool tillable = LegacyBlockStates
            ? block is "grass" or "dirt" or "grass_path"
            : block is "grass_block" or "dirt" or "coarse_dirt" or "grass_path" or "dirt_path";
        if (!tillable)
            return true;
        if (LegacyBlockStates && clicked.LegacyMeta != 0)
        {
            // Original dirt variants 0/1 till successfully; variant 2 (podzol) passes. Unused packed metadata is not evidence of either native state, and the legacy grass name must never classify the flattened grass plant.
            if (block == "dirt" && clicked.LegacyMeta == 2)
                return true;
            if (block != "dirt" || clicked.LegacyMeta != 1)
            {
                prediction = BlockUsePrediction.Unknown;
                return true;
            }
        }
        if (face == Direction.Down)
            return true;
        BlockPos above = position.Above();
        BlockState upper = world.GetBlock(above);
        if (world.GetColumn(above) is null || !upper.IsValid)
            prediction = BlockUsePrediction.Unknown;
        else if (upper.IsAir)
            prediction = BlockUsePrediction.Success(localSwing: false);
        return true;
    }
}
