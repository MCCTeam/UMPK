using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Original default-chain placement geometry; dry states represent the identical native wet/dry collision boxes.</summary>
internal readonly record struct VanillaChainPlacement(bool Supported, bool FixedVertical, bool CopperNames)
{
    public static VanillaChainPlacement ForProtocol(int protocol) => new(protocol is >= 735 and <= 777, protocol <= 736, protocol >= 773);

    public bool Supports(Identifier item, ItemUseOnInfo use, IBlockDataSource blocks)
        => Supported && item.IsMinecraft && !blocks.IsLegacy && use.Kind == ItemUseOnKind.Unknown && use.PlacedBlock == item
            && (CopperNames ? item.Path is "iron_chain" or "copper_chain" or "exposed_copper_chain" or "weathered_copper_chain" or "oxidized_copper_chain"
                or "waxed_copper_chain" or "waxed_exposed_copper_chain" or "waxed_weathered_copper_chain" or "waxed_oxidized_copper_chain" : item.Path == "chain")
            && blocks.Blocks.TryGet(item, out RegistryEntry<BlockDefinition> block) && HasNativeSchema(block.Value);

    public bool TryGetCollisionRepresentative(Identifier item, Direction face, IBlockDataSource blocks, IBlockShapeSource shapes, out BlockState representative)
    {
        representative = default;
        if (!blocks.Blocks.TryGet(item, out RegistryEntry<BlockDefinition> definition) || !HasNativeSchema(definition.Value)
            || shapes is not IBlockCollisionGridSource grids)
            return false;
        Axis axis = FixedVertical ? Axis.Y : face.GetAxis();
        string name = axis switch { Axis.X => "x", Axis.Y => "y", _ => "z" };
        Aabb expected = axis switch
        {
            Axis.X => new(0, .40625, .40625, 1, .59375, .59375),
            Axis.Y => new(.40625, 0, .40625, .59375, 1, .59375),
            _ => new(.40625, .40625, 0, .59375, .59375, 1),
        };
        int matched = 0;
        for (int state = definition.Value.MinStateId; state <= definition.Value.MaxStateId; state++)
        {
            if (!FixedVertical && (!definition.Value.TryGetPropertyValue(state, "axis", out string value) || value != name))
                continue;
            var selected = new BlockState(blocks, state);
            if (!selected.TryGetProperty("waterlogged", out string water) || water is not ("true" or "false")
                || grids.GetCollisionGrid(selected) is not { } grid || grid.CanonicalFullBlock || grid.MaxHorizontalOffset != 0
                || grid.Boxes.Length != 1 || grid.Boxes[0] != expected)
                return false;
            matched++;
            if (water == "false")
                representative = selected;
        }
        // Both original wet/dry states must establish the same rod. Selecting dry does not assert that the destination's fluid is absent.
        return matched == 2 && representative.IsValid;
    }

    private bool HasNativeSchema(BlockDefinition block)
    {
        if (block.StateCount != (FixedVertical ? 2 : 6) || block.Properties.Count != (FixedVertical ? 1 : 2))
            return false;
        if (!FixedVertical)
        {
            BlockPropertyDefinition axis = block.Properties[0];
            if (axis.Name != "axis" || axis.Values.Count != 3 || axis.Values[0] != "x" || axis.Values[1] != "y" || axis.Values[2] != "z")
                return false;
        }
        BlockPropertyDefinition water = block.Properties[FixedVertical ? 0 : 1];
        return water.Name == "waterlogged" && water.Values.Count == 2 && water.Values[0] == "true" && water.Values[1] == "false";
    }
}
