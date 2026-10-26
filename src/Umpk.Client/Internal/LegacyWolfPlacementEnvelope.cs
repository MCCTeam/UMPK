using Umpk.Game.Entities;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Encloses the supported native wolf lifetime only to exclude placement non-candidates. This is neither its actual body nor its current position.</summary>
internal sealed class LegacyWolfPlacementEnvelope
{
    internal const int Protocol = 315;
    private Aabb _endpoints;
    private Vec3d _lastEndpoint;
    private bool _known;

    public LegacyWolfPlacementEnvelope(Vec3d initial)
    {
        _known = Admitted(initial);
        // Native AddMob invokes sm.a(DDDFF), which clamps spawn X/Z before setting its box; packet-coordinate bases retain the original values. Retain both points without altering either runtime position or packet base.
        double nativeX = Math.Clamp(initial.X, -30000000D, 30000000D);
        double nativeZ = Math.Clamp(initial.Z, -30000000D, 30000000D);
        _endpoints = new(Math.Min(initial.X, nativeX), initial.Y, Math.Min(initial.Z, nativeZ),
            Math.Max(initial.X, nativeX), initial.Y, Math.Max(initial.Z, nativeZ));
        _lastEndpoint = initial;
    }

    public static bool Supports(Entity entity, int protocol)
        => protocol == Protocol && entity.Type.Id.IsMinecraft && entity.Type.Id.Path == "wolf";

    public void Invalidate() => _known = false;

    public void ObserveEndpoint(Vec3d value, bool independent)
    {
        if (!_known)
            return;
        if (!Admitted(value) || !independent && value != _lastEndpoint)
        {
            Invalidate();
            return;
        }
        _endpoints = new(Math.Min(_endpoints.MinX, value.X), Math.Min(_endpoints.MinY, value.Y), Math.Min(_endpoints.MinZ, value.Z),
            Math.Max(_endpoints.MaxX, value.X), Math.Max(_endpoints.MaxY, value.Y), Math.Max(_endpoints.MaxZ, value.Z));
        _lastEndpoint = value;
    }

    public bool TryGet(Entity entity, out Aabb envelope)
    {
        envelope = default;
        if (!_known)
            return false;
        if (entity.Position != _lastEndpoint || entity.Vehicle is not null || entity.Passengers.Count != 0
            || entity.Metadata.TryGet(12, out MetadataValue age) && age.Kind != MetadataValueKind.Boolean
            || entity.Metadata.TryGet(13, out MetadataValue flags) && flags.Kind != MetadataValueKind.Byte)
        {
            Invalidate();
            return false;
        }
        // Original Wolf offsets stay below .851 on every axis, including native baby/adult cached-box growth. One block also encloses the proved <1/4096 interpolation error for admitted endpoints and native counts zero through three; outward rounding covers these final additions.
        envelope = new(Math.BitDecrement(_endpoints.MinX - 1), Math.BitDecrement(_endpoints.MinY - 1), Math.BitDecrement(_endpoints.MinZ - 1),
            Math.BitIncrement(_endpoints.MaxX + 1), Math.BitIncrement(_endpoints.MaxY + 1), Math.BitIncrement(_endpoints.MaxZ + 1));
        return true;
    }

    private static bool Admitted(Vec3d value)
        => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z)
            && Math.Abs(value.X) <= 2147483648D && Math.Abs(value.Y) <= 2147483648D && Math.Abs(value.Z) <= 2147483648D;

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
