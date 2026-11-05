using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

internal readonly record struct StonePlacementMutation(BlockPos Position, int State, ItemStack RemainingStack);

/// <summary>Default stone placement into literal air with a finite, source-bound no-op neighbor set.</summary>
internal readonly record struct VanillaStoneLocalPlacement(bool Supported, bool ControllerCreativeRestoresCount)
{
    internal static VanillaStoneLocalPlacement ForProtocol(int protocol) => new(protocol is >= 766 and <= 777, protocol is >= 766 and <= 769);

    internal bool TryCreate(int protocol, ClientState state, BlockPos clickedPosition, Direction face, ItemStack item, out StonePlacementMutation mutation)
    {
        mutation = default;
        if (!Supported || item.IsEmpty || !item.Components.HasNoPatch || item.Item.IsDefault || item.Item.Id != Identifier.Minecraft("stone")
            || !JavaGameData.Registries(protocol).Items.TryGet(item.Item.NetworkId, out RegistryEntry<ItemDefinition> canonicalItem)
            || canonicalItem.Id != item.Item.Id || !ReferenceEquals(canonicalItem.Value, item.Item.Value)
            || state.Self.AbilitiesObservationLifetime != state.PlayerLifetime
            || state.Inventory.HasOpenContainer || state.Inventory.HasPendingPrediction)
            return false;
        var world = state.World;
        BlockPos destination = clickedPosition.Offset(face);
        if (!KnownCell(clickedPosition) || !KnownCell(destination)
            || !Canonical(state.JukeboxPrediction.GetBlock(world, clickedPosition), "stone")
            || !Canonical(state.JukeboxPrediction.GetBlock(world, destination), "air"))
            return false;
        // Literal AIR and plain STONE inherit the original no-op shape callbacks. Solidity or an arbitrary full box does not establish that property for another block class.
        ReadOnlySpan<Direction> neighbors = [Direction.Down, Direction.Up, Direction.North, Direction.South, Direction.West, Direction.East];
        foreach (Direction direction in neighbors)
        {
            BlockPos neighbor = destination.Offset(direction);
            if (!KnownCell(neighbor))
                return false;
            BlockState current = state.JukeboxPrediction.GetBlock(world, neighbor);
            if (!Canonical(current, "air") && !Canonical(current, "stone"))
                return false;
        }
        RegistryEntry<BlockDefinition> stone = JavaGameData.Registries(protocol).Blocks[Identifier.Minecraft("stone")];
        if (JavaGameData.BlockShapes(protocol) is not IBlockCollisionGridSource grids
            || grids.GetCollisionGrid(new BlockState(world.BlockData, stone.Value.DefaultStateId)) is not { CanonicalFullBlock: true, MaxHorizontalOffset: 0 })
            return false;
        bool preserveCount = state.Self.InstantBuild || ControllerCreativeRestoresCount && state.Self.GameMode == GameMode.Creative;
        mutation = new(destination, stone.Value.DefaultStateId, preserveCount ? item : item.Shrink(1));
        return true;

        // This bounded slice uses the native ordinary world envelope; 777's wider valid-chunk range is not inferred from a synthesized state.
        bool KnownCell(BlockPos position) => position.X is >= -30000000 and < 30000000 && position.Z is >= -30000000 and < 30000000
            && position.Y >= world.Dimension.MinY && position.Y < world.Dimension.MaxY
            && world.GetColumn(position) is not null && state.EntityMembership.HasColumn(position);

        bool Canonical(BlockState current, string name)
        {
            Identifier id = Identifier.Minecraft(name);
            return current.IsValid && current.Block.Id == id && JavaGameData.Registries(protocol).Blocks.TryGet(id, out RegistryEntry<BlockDefinition> canonical)
                && ReferenceEquals(current.Block.Value, canonical.Value) && canonical.Value.Properties.Count == 0 && canonical.Value.StateCount == 1
                && current.StateId == canonical.Value.DefaultStateId;
        }
    }
}
