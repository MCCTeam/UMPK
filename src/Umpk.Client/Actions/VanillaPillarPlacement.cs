using Umpk.Game.Blocks;
using Umpk.Game.Registries;

namespace Umpk.Client.Actions;

/// <summary>Default native log/pillar items whose orientation never changes their full-cube placement bounds or inherited survival rule.</summary>
internal static class VanillaPillarPlacement
{
    public static bool Supports(int protocol, Identifier item, ItemUseOnInfo use, IBlockDataSource blocks, IBlockShapeSource shapes)
    {
        if (protocol is < 47 or > 777 || !item.IsMinecraft || use.Kind != ItemUseOnKind.Unknown || use.PlacedBlock != item
            || !blocks.Blocks.TryGet(item, out RegistryEntry<BlockDefinition> entry))
            return false;
        // The legacy default stacks preserve metadata zero; ItemBlock's existing pre-send query still runs before the actual face-oriented placement.
        if (blocks.IsLegacy)
            return protocol <= 340 && item.Path is "log" or "log2" or "hay_block" or "purpur_pillar" or "bone_block";
        BlockDefinition block = entry.Value;
        // Every matching registered BlockItem on the supported fifty datasets is source-bound to the native full-cube pillar family. The item mapping, complete axis schema and original collision grids are all required; a suffix or property name alone does not admit an item.
        if (protocol < 393 || block.StateCount != 3 || block.Properties.Count != 1 || shapes is not IBlockCollisionGridSource grids)
            return false;
        BlockPropertyDefinition axis = block.Properties[0];
        if (axis.Name != "axis" || axis.Values.Count != 3 || axis.Values[0] != "x" || axis.Values[1] != "y" || axis.Values[2] != "z")
            return false;
        for (int state = block.MinStateId; state <= block.MaxStateId; state++)
            if (grids.GetCollisionGrid(new BlockState(blocks, state)) is not { CanonicalFullBlock: true })
                return false;
        return true;
    }
}
