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

    public static BlockUsePrediction Predict(Identifier transformer, BlockState clicked, World world, BlockPos position, Direction face, Hand hand, ItemStack offhand, bool offhandKnown, bool secondaryUse, ClientToolTransformers definitions, ClientRegistryTags tags, JukeboxPredictionState? current) => Predict(transformer, clicked, world, position, face, hand, offhand, offhandKnown, secondaryUse, definitions, tags, current, out _);

    public static BlockUsePrediction Predict(Identifier transformer, BlockState clicked, World world, BlockPos position, Direction face, Hand hand, ItemStack offhand, bool offhandKnown, bool secondaryUse, ClientToolTransformers definitions, ClientRegistryTags tags, JukeboxPredictionState? current, out ToolBlockMutation? mutation)
    {
        BlockUsePrediction result = PredictResult(transformer, clicked, world, position, face, hand, offhand, offhandKnown, secondaryUse, definitions, tags, current);
        mutation = result.Result == BlockUseResult.Success ? SelectMutation(transformer, clicked, world, position, face, tags, current) : null;
        return result;
    }

    private static BlockUsePrediction PredictResult(Identifier transformer, BlockState clicked, World world, BlockPos position, Direction face, Hand hand, ItemStack offhand, bool offhandKnown, bool secondaryUse, ClientToolTransformers definitions, ClientRegistryTags tags, JukeboxPredictionState? current)
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

    private static ToolBlockMutation? SelectMutation(Identifier transformer, BlockState clicked, World world, BlockPos position, Direction face, ClientRegistryTags tags, JukeboxPredictionState? current)
    {
        int unknown = world.BlockData.UnknownStateId;
        Identifier target;
        if (transformer.Path == "axe")
        {
            if (!VanillaToolTransformerDefinitions.TryGetAxeTarget(clicked.Block.Id, out target))
                return null;
        }
        else if (transformer.Path == "shovel")
            target = Identifier.Minecraft("dirt_path");
        else
        {
            bool? farmland = face == Direction.Down ? false : tags.Contains(RegistryIds.Block, Identifier.Minecraft("turns_into_farmland"), clicked.Block.NetworkId);
            if (farmland is not false)
            {
                BlockPos above = position.Above();
                BlockState upper = ReadNativeBlock(world, above, current);
                bool? air = upper.IsValid ? tags.Contains(RegistryIds.Block, Identifier.Minecraft("air"), upper.Block.NetworkId) : null;
                if (air is false)
                    farmland = false;
                else if (air is null)
                    farmland = null;
            }
            if (farmland is null)
                return null;
            target = Identifier.Minecraft(farmland is true ? "farmland" : "dirt");
        }
        if (!world.BlockData.Blocks.TryGet(target, out var entry))
            return null;
        int state = entry.Value.DefaultStateId;
        if (transformer.Path == "axe" && !TryCopyState(clicked, target, world, out state))
            return null;
        BlockState changed = new(world.BlockData, state);
        BlockPos? peerPosition = null;
        int? peerState = null;
        if (CopperDoors.Contains(target) && clicked.TryGetProperty("half", out string half))
        {
            BlockPos other = half == "lower" ? position.Above() : position.Below();
            BlockState peer = ReadNativeBlock(world, other, current);
            if (peer.IsValid && CopperDoors.Contains(peer.Block.Id)
                && peer.TryGetProperty("half", out string otherHalf) && otherHalf != half)
            {
                peerPosition = other;
                peerState = TryCopyState(changed, target, world, out int copied, "half", otherHalf) ? copied : unknown;
            }
        }
        else if (CopperChests.Contains(target)
            && clicked.TryGetProperty("type", out string type) && type is "left" or "right" && clicked.TryGetProperty("facing", out string facing))
        {
            Direction direction = facing switch
            {
                "north" => type == "left" ? Direction.East : Direction.West,
                "south" => type == "left" ? Direction.West : Direction.East,
                "west" => type == "left" ? Direction.North : Direction.South,
                _ => type == "left" ? Direction.South : Direction.North,
            };
            BlockPos other = position.Offset(direction);
            BlockState peer = ReadNativeBlock(world, other, current);
            if (peer.IsValid && CopperChests.Contains(peer.Block.Id)
                && peer.TryGetProperty("type", out string otherType) && otherType == (type == "left" ? "right" : "left")
                && peer.TryGetProperty("facing", out string otherFacing) && otherFacing == facing)
            {
                peerPosition = other;
                bool? targetTagged = tags.Contains(RegistryIds.Block, Identifier.Minecraft("copper_chests"), changed.Block.NetworkId);
                bool? peerTagged = tags.Contains(RegistryIds.Block, Identifier.Minecraft("copper_chests"), peer.Block.NetworkId);
                if (targetTagged is true && peerTagged is true && TryCopyState(peer, target, world, out int copied))
                    peerState = copied;
                else
                {
                    state = unknown;
                    peerState = unknown;
                }
            }
        }
        // This bounded overlay commits only source-bound no-op surrounding shapes, including the surrounding cells of a coupled mutation. Other target/neighbor participants remain an explicitly unimplemented local-prediction context.
        if (!HasKnownNoOpNeighbors(position, peerPosition) || peerPosition is { } second && !HasKnownNoOpNeighbors(second, position))
            return null;
        if (target.Path.EndsWith("_stairs", StringComparison.Ordinal) || target.Path.EndsWith("_bars", StringComparison.Ordinal)
            || state == unknown || peerState == unknown)
            return null;
        return new(position, state, peerPosition, peerState);

        bool HasKnownNoOpNeighbors(BlockPos cell, BlockPos? coupled)
        {
            foreach (Direction direction in ShapeOrder)
            {
                BlockPos neighborPosition = cell.Offset(direction);
                if (neighborPosition == coupled)
                    continue;
                BlockState neighbor = ReadNativeBlock(world, neighborPosition, current);
                if (!neighbor.IsValid || !NoOpNeighborBlocks.Contains(neighbor.Block.Id))
                    return false;
            }
            return true;
        }
    }

    private static BlockState ReadNativeBlock(World world, BlockPos position, JukeboxPredictionState? current)
    {
        if (position.Y < world.Dimension.MinY || position.Y >= world.Dimension.MaxY)
            return world.BlockData.Blocks.TryGet(Identifier.Minecraft("void_air"), out var empty)
                ? new BlockState(world.BlockData, empty.Value.DefaultStateId) : new BlockState(world.BlockData, world.BlockData.UnknownStateId);
        return current?.GetBlock(world, position) ?? world.GetBlock(position);
    }

    private static bool TryCopyState(BlockState source, Identifier target, World world, out int state, string? overrideName = null, string? overrideValue = null)
    {
        state = world.BlockData.UnknownStateId;
        if (!source.IsValid || !world.BlockData.Blocks.TryGet(target, out var entry))
            return false;
        BlockDefinition definition = entry.Value;
        foreach (BlockPropertyDefinition property in definition.Properties)
            if (source.PropertyNames.Contains(property.Name) && !source.TryGetProperty(property.Name, out _))
                return false;
        for (int candidate = definition.MinStateId; candidate <= definition.MaxStateId; candidate++)
        {
            bool matches = true;
            foreach (BlockPropertyDefinition property in definition.Properties)
            {
                string? expected = property.Name == overrideName ? overrideValue
                    : source.TryGetProperty(property.Name, out string shared) ? shared
                    : definition.TryGetPropertyValue(definition.DefaultStateId, property.Name, out string initial) ? initial : null;
                if (expected is null || !definition.TryGetPropertyValue(candidate, property.Name, out string value) || value != expected)
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            {
                state = candidate;
                return true;
            }
        }
        return false;
    }

    // Exact finite native classes: these four registered states inherit the unchanged BlockBehaviour.updateShape. Solidity and item/block naming do not establish this fact.
    private static readonly Identifier[] NoOpNeighborBlocks = [Identifier.Minecraft("air"), Identifier.Minecraft("cave_air"), Identifier.Minecraft("void_air"), Identifier.Minecraft("stone")];
    // The source-bound 26.3 COPPER_DOOR/COPPER_CHEST collections register exactly these eight native family members. Other door/chest classes are not classified by suffix here.
    private static readonly Identifier[] CopperDoors = [Identifier.Minecraft("copper_door"), Identifier.Minecraft("exposed_copper_door"), Identifier.Minecraft("weathered_copper_door"), Identifier.Minecraft("oxidized_copper_door"), Identifier.Minecraft("waxed_copper_door"), Identifier.Minecraft("waxed_exposed_copper_door"), Identifier.Minecraft("waxed_weathered_copper_door"), Identifier.Minecraft("waxed_oxidized_copper_door")];
    private static readonly Identifier[] CopperChests = [Identifier.Minecraft("copper_chest"), Identifier.Minecraft("exposed_copper_chest"), Identifier.Minecraft("weathered_copper_chest"), Identifier.Minecraft("oxidized_copper_chest"), Identifier.Minecraft("waxed_copper_chest"), Identifier.Minecraft("waxed_exposed_copper_chest"), Identifier.Minecraft("waxed_weathered_copper_chest"), Identifier.Minecraft("waxed_oxidized_copper_chest")];

    private static readonly Direction[] ShapeOrder = [Direction.West, Direction.East, Direction.North, Direction.South, Direction.Down, Direction.Up];
}

internal readonly record struct ToolBlockMutation(BlockPos Position, int State, BlockPos? NeighborPosition = null, int? NeighborState = null);
