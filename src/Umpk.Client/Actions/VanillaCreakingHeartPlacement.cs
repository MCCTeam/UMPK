using Umpk.Client.Internal;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Bounds-only admission for the native heart getter; exact placed state and activation remain separate.</summary>
internal static class VanillaCreakingHeartPlacement
{
    public static bool IsFamily(Identifier item) => item == Identifier.Minecraft("creaking_heart");

    public static bool Supports(int protocol, Identifier item, ItemUseOnInfo use, IBlockDataSource blocks, IBlockShapeSource shapes)
    {
        if (protocol is < 768 or > 777 || !IsFamily(item) || use.Kind != ItemUseOnKind.Unknown || use.PlacedBlock != item
            || !blocks.Blocks.TryGet(item, out RegistryEntry<BlockDefinition> entry) || shapes is not IBlockCollisionGridSource grids)
            return false;
        BlockDefinition block = entry.Value;
        if (block.StateCount != (protocol == 768 ? 9 : protocol == 769 ? 12 : 18))
            return false;
        bool schema = protocol switch
        {
            768 => block.Properties.Count == 2 && block.Properties[0].Name == "axis" && block.Properties[1].Name == "creaking",
            769 => block.Properties.Count == 3 && block.Properties[0].Name == "active" && block.Properties[1].Name == "axis" && block.Properties[2].Name == "natural",
            _ => block.Properties.Count == 3 && block.Properties[0].Name == "axis" && block.Properties[1].Name == "creaking_heart_state" && block.Properties[2].Name == "natural",
        };
        if (!schema)
            return false;
        BlockPropertyDefinition heartAxis = block.Properties[protocol == 769 ? 1 : 0];
        // Protocol 768's unresolved creaking domain leaves all packed domains unavailable. The source-bound family/schema and every native grid suffice for this result-only path.
        if (protocol != 768 && (heartAxis.Values.Count != 3 || heartAxis.Values[0] != "x" || heartAxis.Values[1] != "y" || heartAxis.Values[2] != "z"))
            return false;
        for (int state = block.MinStateId; state <= block.MaxStateId; state++)
            if (grids.GetCollisionGrid(new BlockState(blocks, state)) is not { CanonicalFullBlock: true })
                return false;
        return true;
    }

    public static bool HasSafeGetterContext(World world, EntityMembershipState membership, JukeboxPredictionState prediction, ClientRegistryTags tags, BlockPos destination, Direction face)
    {
        if (world.GetColumn(destination) is null || !membership.HasColumn(destination))
            return false;
        (Direction positive, Direction negative, string axis) = face switch
        {
            Direction.Up or Direction.Down => (Direction.Up, Direction.Down, "y"),
            Direction.North or Direction.South => (Direction.South, Direction.North, "z"),
            Direction.East or Direction.West => (Direction.East, Direction.West, "x"),
            _ => throw new ArgumentOutOfRangeException(nameof(face)),
        };
        if (!Read(positive, out bool positiveCanContinue))
            return false;
        return !positiveCanContinue || Read(negative, out _);

        bool Read(Direction direction, out bool canContinue)
        {
            canContinue = false;
            BlockPos cell = destination.Offset(direction);
            if (cell.Y < world.Dimension.MinY || cell.Y >= world.Dimension.MaxY || world.GetColumn(cell) is null || !membership.HasColumn(cell))
                return false;
            BlockState neighbor = prediction.GetBlock(world, cell);
            if (!neighbor.IsValid)
                return false;
            bool? member = tags.Contains(RegistryIds.Block, Identifier.Minecraft("pale_oak_logs"), neighbor.Block.NetworkId);
            if (member is false)
                return true;
            // These four canonical classes inherit the exact native RotatedPillarBlock.AXIS singleton. Name/domain equality alone cannot prove StateHolder identity lookup.
            if (!neighbor.Block.Id.IsMinecraft || neighbor.Block.Id.Path is not ("pale_oak_log" or "stripped_pale_oak_log" or "pale_oak_wood" or "stripped_pale_oak_wood"))
                return false;
            BlockPropertyDefinition? property = null;
            foreach (BlockPropertyDefinition candidate in neighbor.Block.Value.Properties)
                if (candidate.Name == "axis")
                    property = candidate;
            // Preserve the canonical domain as a separate guard; the source-bound singleton identity above remains required.
            if (property is null || property.Values.Count != 3 || property.Values[0] != "x" || property.Values[1] != "y" || property.Values[2] != "z"
                || !neighbor.TryGetProperty("axis", out string value) || value is not ("x" or "y" or "z"))
                return false;
            canContinue = value == axis;
            return true;
        }
    }
}
