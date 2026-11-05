using Umpk.Game.Entities;
using Umpk.Game.World;

namespace Umpk.Client.Internal;

/// <summary>Binds the exact 1.11 Wolf placement-relevance profile and the native non-displacing world context.</summary>
internal static class LegacyWolfPlacementEnvelope
{
    internal const int Protocol = 315;

    public static bool Supports(Entity entity, int protocol)
        => protocol == Protocol && entity.Type.Id.IsMinecraft && entity.Type.Id.Path == "wolf";

    public static LegacyPlacementEnvelope? Create(Entity entity, int protocol)
        => Supports(entity, protocol) ? new(entity.Position, Protocol, MetadataIsKnown) : null;

    private static bool MetadataIsKnown(Entity entity)
        => (!entity.Metadata.TryGet(12, out MetadataValue age) || age.Kind == MetadataValueKind.Boolean)
            && (!entity.Metadata.TryGet(13, out MetadataValue flags) || flags.Kind == MetadataValueKind.Byte);

    // Original als.l returns field u. Across all 236 registered blocks only alm and aml set it; all 47 resulting block families are accounted for. Chest/trapped-chest and spawner client ticks cannot move an entity; every other block-entity state remains unowned even when no NBT was sent.
    private static ReadOnlySpan<byte> UnownedBlockEntityBlocks =>
    [
        0x00, 0x00, 0x80, 0x02, 0x10, 0x00, 0x00, 0xE0,
        0x10, 0x00, 0x10, 0x00, 0x00, 0x00, 0xB0, 0x00,
        0x04, 0x16, 0xE1, 0x44, 0x00, 0x00, 0x07, 0x00,
        0x00, 0x00, 0x0E, 0xF8, 0xFF, 0x07, 0x00, 0x80,
    ];

    public static bool WorldContextIsKnown(World? world)
    {
        if (world is null || !world.BlockData.IsLegacy)
            return false;
        foreach (ChunkColumn column in world.LoadedColumns)
            for (int index = 0; index < column.SectionCount; index++)
                if (column.GetSection(index) is { } section && section.MayContainBlockState(IsUnownedBlockEntityState))
                    return false;
        foreach (var pair in world.BlockEntityNbt)
        {
            var state = world.GetBlock(pair.Key);
            string? id = pair.Value.GetString("id");
            if (!state.IsValid || !(id == "minecraft:chest" && state.Block.NetworkId is 54 or 146
                || id == "minecraft:mob_spawner" && state.Block.NetworkId == 52))
                return false;
        }
        return true;
    }

    private static bool IsUnownedBlockEntityState(int state)
    {
        int block = state >> 4;
        // The complete native registry contains precisely IDs zero through 234 and 255. Metadata does not participate in its block-entity flag.
        return state < 0 || block is < 0 or > 255 || block is > 234 and < 255
            || (UnownedBlockEntityBlocks[block >> 3] & (1 << (block & 7))) != 0;
    }
}
