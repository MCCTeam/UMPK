using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>The original HoeItem use result; server-owned terrain, drops and durability remain authoritative.</summary>
internal readonly record struct VanillaHoeUse(bool HasRules, bool HasRootedDirt, bool UsesComponents)
{
    // Original HoeItem and ItemStack/MultiPlayerGameMode sources are audited on all 43 releases from 1.14.4 through 26.2. Protocol 755 adds rooted dirt with an unconditional predicate; 777 replaces HoeItem with data-driven BlockTransformer components.
    public static VanillaHoeUse ForProtocol(int protocol) => new(protocol is >= 498 and <= 776, protocol >= 755, protocol >= 766);

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
        if (face == Direction.Down || block is not ("grass_block" or "dirt" or "coarse_dirt" or "grass_path" or "dirt_path"))
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
