using Umpk.Game.Registries;

namespace Umpk.Game.Blocks;

/// <summary>Ergonomic bridges from an <see cref="IBlockDataSource"/> to <see cref="BlockState"/> values: state-id to state, block to default state, and legacy <c>(id, meta)</c> to state. Consumers (world storage, physics) use these instead of constructing <see cref="BlockState"/> by hand.</summary>
public static class BlockDataSourceExtensions
{
    /// <summary>Wraps a state id as a <see cref="BlockState"/> over this source.</summary>
    public static BlockState GetState(this IBlockDataSource source, int stateId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new BlockState(source, stateId);
    }

    /// <summary>The default <see cref="BlockState"/> of a block given its registry entry.</summary>
    public static BlockState GetDefaultState(this IBlockDataSource source, RegistryEntry<BlockDefinition> block)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new BlockState(source, block.Value.DefaultStateId);
    }

    /// <summary>The default <see cref="BlockState"/> of a block given its network id.</summary>
    public static BlockState GetDefaultState(this IBlockDataSource source, int blockNetworkId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new BlockState(source, source.GetDefaultStateId(blockNetworkId));
    }

    /// <summary>The default <see cref="BlockState"/> of a block given its key, or the unknown state when the key is absent.</summary>
    public static bool TryGetDefaultState(this IBlockDataSource source, Identifier blockId, out BlockState state)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Blocks.TryGet(blockId, out var entry))
        {
            state = new BlockState(source, entry.Value.DefaultStateId);
            return true;
        }

        state = new BlockState(source, source.UnknownStateId);
        return false;
    }

    /// <summary>Composes a legacy <c>(blockId, meta)</c> pair into a <see cref="BlockState"/>. Only meaningful on legacy sources; on modern sources the caller should guard on <see cref="IBlockDataSource.IsLegacy"/>.</summary>
    public static BlockState GetLegacyState(this IBlockDataSource source, int blockId, int meta)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new BlockState(source, source.EncodeLegacy(blockId, meta));
    }
}
