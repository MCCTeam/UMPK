using Umpk.Client.Internal;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Original777 default tool result after activation, cooldown and Adventure. Effective definitions and received block tags remain session-owned facts.</summary>
internal static class Vanilla777ToolUse
{
    public static bool Supports(int protocol, Identifier item, out Identifier transformer)
    {
        transformer = default;
        if (protocol != 777 || !item.IsMinecraft)
            return false;
        int split = item.Path.IndexOf('_');
        if (split < 0 || item.Path.AsSpan(0, split) is not ("wooden" or "stone" or "copper" or "iron" or "golden" or "diamond" or "netherite"))
            return false;
        transformer = item.Path.AsSpan(split + 1) switch
        {
            "hoe" => Identifier.Minecraft("hoe"),
            "shovel" => Identifier.Minecraft("shovel"),
            "axe" => Identifier.Minecraft("axe"),
            _ => default,
        };
        return transformer.Path.Length != 0;
    }

    public static bool SupportsStack(ItemStack stack)
    {
        if (stack.Components.HasNoPatch)
            return true;
        foreach (DataComponentEntry entry in stack.Components.Patch)
            if (!entry.Type.Equals(DataComponents.Damage) && !entry.Type.Equals(DataComponents.CanPlaceOn))
                return false;
        return true;
    }

    public static BlockUsePrediction Predict(Identifier transformer, BlockState clicked, World world, BlockPos position, Direction face, Hand hand, ItemStack offhand, bool offhandKnown, bool secondaryUse, ClientToolTransformers definitions, ClientRegistryTags tags, JukeboxPredictionState? current)
    {
        if (!definitions.TryGetVanillaDefault(transformer, out _))
            return BlockUsePrediction.Unknown;
        // Native performs this same effective offhand intent check for every transformer, before the per-definition face/provider loop.
        if (hand == Hand.Main && !secondaryUse)
        {
            if (!offhandKnown || !offhand.IsEmpty && !offhand.Components.HasNoPatch && offhand.Components.Patch.Any(entry => !entry.Type.Equals(DataComponents.Damage)))
                return BlockUsePrediction.Unknown;
            if (!offhand.IsEmpty && offhand.Item.Id == Identifier.Minecraft("shield"))
                return BlockUsePrediction.Pass;
        }
        if (transformer.Path == "axe")
            return VanillaToolTransformerDefinitions.IsAxeInput(clicked.Block.Id) ? BlockUsePrediction.Success(localSwing: true) : BlockUsePrediction.Pass;
        // Both possible hoe providers return the same Success/swing. Rooted dirt therefore proves this result independently of an earlier unknown tag match; no world-state alternative is predicted here.
        if (transformer.Path == "hoe" && clicked.Block.Id == Identifier.Minecraft("rooted_dirt"))
            return BlockUsePrediction.Success(localSwing: true);
        if (face == Direction.Down)
            return BlockUsePrediction.Pass;
        string targetTag = transformer.Path == "hoe" ? "turns_into_farmland" : "turns_into_dirt_path";
        bool? target = tags.Contains(RegistryIds.Block, Identifier.Minecraft(targetTag), clicked.Block.NetworkId);
        if (transformer.Path == "hoe" && clicked.Block.Id == Identifier.Minecraft("coarse_dirt"))
            target = true;
        if (target is false)
            return BlockUsePrediction.Pass;
        BlockPos above = position.Above();
        BlockState upper;
        if (above.Y < world.Dimension.MinY || above.Y >= world.Dimension.MaxY)
        {
            // Native Level.getBlockState returns VOID_AIR outside the dimension height. Storage state0 AIR is a different tag input.
            if (!world.BlockData.Blocks.TryGet(Identifier.Minecraft("void_air"), out var voidAir))
                return BlockUsePrediction.Unknown;
            upper = new BlockState(world.BlockData, voidAir.Value.DefaultStateId);
        }
        else
        {
            upper = current?.GetBlock(world, above) ?? world.GetBlock(above);
            if (world.GetColumn(above) is null || !upper.IsValid)
                return BlockUsePrediction.Unknown;
        }
        bool? air = tags.Contains(RegistryIds.Block, Identifier.Minecraft("air"), upper.Block.NetworkId);
        if (air is false)
            return BlockUsePrediction.Pass;
        return target is true && air is true ? BlockUsePrediction.Success(localSwing: true) : BlockUsePrediction.Unknown;
    }
}
