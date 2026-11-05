using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Bounded horizontal stone-wall ladder placement. Wet/dry states represent identical native collision plates, without predicting terrain or fluid state.</summary>
internal readonly record struct VanillaLadderPlacement(bool Supported)
{
    public static VanillaLadderPlacement ForProtocol(int protocol) => new(protocol is >= 735 and <= 777);

    public bool Supports(Identifier item, ItemUseOnInfo use, IBlockDataSource blocks)
        => Supported && item == Identifier.Minecraft("ladder") && !blocks.IsLegacy && use.Kind == ItemUseOnKind.Unknown && use.PlacedBlock == item
            && blocks.Blocks.TryGet(item, out RegistryEntry<BlockDefinition> block) && HasNativeSchema(block.Value);

    public bool SupportsStack(int protocol, ItemStack stack)
        => Supported && !stack.IsEmpty && stack.Components.HasNoPatch && !stack.Item.IsDefault
            && JavaGameData.Registries(protocol).Items.TryGet(stack.Item.NetworkId, out RegistryEntry<ItemDefinition> canonical)
            && canonical.Id.Equals(stack.Item.Id) && ReferenceEquals(canonical.Value, stack.Item.Value);

    public bool TryGetStoneWallCollisionRepresentative(BlockState clicked, Direction face, IBlockDataSource blocks, IBlockShapeSource shapes, out BlockState representative)
    {
        representative = default;
        if (!Supported || face.GetAxis() == Axis.Y || clicked.Block.Id != Identifier.Minecraft("stone")
            || clicked.Block.Value.StateCount != 1 || clicked.Block.Value.Properties.Count != 0 || shapes is not IBlockCollisionGridSource grids
            || grids.GetCollisionGrid(clicked) is not { CanonicalFullBlock: true, MaxHorizontalOffset: 0 }
            || !blocks.Blocks.TryGet(Identifier.Minecraft("ladder"), out RegistryEntry<BlockDefinition> definition) || !HasNativeSchema(definition.Value))
            return false;
        // Original STONE is registered as plain Block and inherits its collision-based support shape. This rule deliberately does not infer support for any other material.
        string facing = face switch { Direction.North => "north", Direction.South => "south", Direction.West => "west", _ => "east" };
        Aabb expected = face switch
        {
            Direction.North => new(0, 0, .8125, 1, 1, 1),
            Direction.South => new(0, 0, 0, 1, 1, .1875),
            Direction.West => new(.8125, 0, 0, 1, 1, 1),
            _ => new(0, 0, 0, .1875, 1, 1),
        };
        int matched = 0;
        for (int state = definition.Value.MinStateId; state <= definition.Value.MaxStateId; state++)
        {
            if (!definition.Value.TryGetPropertyValue(state, "facing", out string value) || value != facing)
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
        return matched == 2 && representative.IsValid;
    }

    private static bool HasNativeSchema(BlockDefinition block)
    {
        if (block.StateCount != 8 || block.Properties.Count != 2)
            return false;
        BlockPropertyDefinition facing = block.Properties[0];
        BlockPropertyDefinition water = block.Properties[1];
        return facing.Name == "facing" && facing.Values.Count == 4 && facing.Values[0] == "north" && facing.Values[1] == "south" && facing.Values[2] == "west" && facing.Values[3] == "east"
            && water.Name == "waterlogged" && water.Values.Count == 2 && water.Values[0] == "true" && water.Values[1] == "false";
    }
}
