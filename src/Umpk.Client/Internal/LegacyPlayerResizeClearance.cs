using Umpk.Client.Navigation;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Client.Internal;

/// <summary>The original remote-player size phase's block-only query against a client world. A null actor contributes no entity collider or border wall.</summary>
internal sealed class LegacyPlayerResizeClearance
{
    private readonly PhysicsCollisionQuery _blocks;
    private readonly QueryProfile _profile;
    private readonly EntityMembershipState? _membership;

    internal LegacyPlayerResizeClearance(World world, IBlockShapeSource shapes, int protocol, EntityMembershipState? membership = null)
    {
        _membership = membership;
        _profile = QueryProfile.ForProtocol(protocol);
        _blocks = new PhysicsCollisionQuery(new WorldPhysicsView(world, shapes, WorldBorderContainmentEra.Legacy, PhysicsProfile.BlockCollisionQueryForProtocol(protocol)));
    }

    internal bool NoCollision(in Aabb box)
    {
        int minX = (int)Math.Floor(box.MinX) - 1, maxX = (int)Math.Ceiling(box.MaxX) + 1;
        int minY = (int)Math.Floor(box.MinY) - 1, maxY = (int)Math.Ceiling(box.MaxY) + 1;
        int minZ = (int)Math.Floor(box.MinZ) - 1, maxZ = (int)Math.Ceiling(box.MaxZ) + 1;
        bool blocked = false;
        if (_profile.DeferredFullCubes)
        {
            for (int z = minZ; z < maxZ; z++)
                for (int y = minY; y < maxY; y++)
                    for (int x = minX; x < maxX; x++)
                    {
                        int sides = (x == minX || x == maxX - 1 ? 1 : 0) + (z == minZ || z == maxZ - 1 ? 1 : 0);
                        if (sides == 2 || (sides != 0 && (y == minY || y == maxY - 1)))
                            continue;
                        blocked |= IntersectsBlock(new BlockPos(x, y, z), box);
                    }
            return !blocked;
        }

        for (int x = minX; x < maxX; x++)
            for (int z = minZ; z < maxZ; z++)
            {
                int sides = (x == minX || x == maxX - 1 ? 1 : 0) + (z == minZ || z == maxZ - 1 ? 1 : 0);
                bool outside = x is < -30_000_000 or >= 30_000_000 || z is < -30_000_000 or >= 30_000_000;
                if (sides == 2 || (_profile.BoundedLoadedColumns && outside))
                    continue;
                for (int y = minY; y < maxY; y++)
                {
                    if (sides != 0 && (y == maxY - 1 || (_profile.SkipBottomPadding && y == minY)))
                        continue;
                    if ((!_profile.Voxel && outside) || IntersectsBlock(new BlockPos(x, y, z), box))
                        return false;
                }
            }
        return true;
    }

    private bool IntersectsBlock(BlockPos cell, in Aabb box)
        => (_membership is null || _membership.HasColumn(cell)) && _blocks.IntersectsBlock(cell, box);

    private readonly record struct QueryProfile(bool BoundedLoadedColumns, bool SkipBottomPadding, bool Voxel, bool DeferredFullCubes)
    {
        internal static QueryProfile ForProtocol(int protocol) => protocol switch
        {
            >= 107 and <= 109 => new(true, true, false, false),
            >= 110 and <= 315 => new(false, true, false, false),
            >= 316 and <= 340 => new(false, false, false, false),
            393 => new(false, false, true, false),
            401 or 404 => new(false, true, true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), "The legacy player resize query requires a measured protocol from 1.9 through 1.13.2."),
        };
    }
}
