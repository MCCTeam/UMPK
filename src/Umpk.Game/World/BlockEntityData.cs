using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;

namespace Umpk.Game.World;

/// <summary>A block entity at a position: its type registry entry and the raw <see cref="NbtCompound"/> the server delivered. The compound is stored as-is so member order survives re-encoding. Typed readers are helpers over this generic store.</summary>
public sealed class BlockEntityData
{
    /// <summary>Creates a block entity from its position, type entry and raw NBT payload.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nbt"/> is null.</exception>
    public BlockEntityData(BlockPos position, RegistryEntry<BlockEntityTypeDefinition> type, NbtCompound nbt)
    {
        ArgumentNullException.ThrowIfNull(nbt);
        Position = position;
        Type = type;
        Nbt = nbt;
    }

    /// <summary>The world position of this block entity.</summary>
    public BlockPos Position { get; }

    /// <summary>The block-entity type registry entry.</summary>
    public RegistryEntry<BlockEntityTypeDefinition> Type { get; }

    /// <summary>The raw server NBT, stored with member order preserved.</summary>
    public NbtCompound Nbt { get; }
}
