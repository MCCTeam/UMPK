using Umpk.Geometry;

namespace Umpk.Game.Registries;

/// <summary>Block-local targeting geometry selected for the current position and held item. Interaction geometry can override the face while retaining the outline hit point.</summary>
public readonly record struct BlockTargetingShape(ReadOnlyMemory<Aabb> Outline, Vec3d OutlineOffset, ReadOnlyMemory<Aabb> Interaction, Vec3d InteractionOffset)
{
    /// <summary>The measured ray clipping and origin-inside behavior.</summary>
    public BlockRayClipModel ClipModel { get; init; } = BlockRayClipModel.VoxelOppositeInside;
}

/// <summary>Source-backed block ray clipping behaviors.</summary>
public enum BlockRayClipModel
{
    /// <summary>Legacy boxes test all six planes, including an exit face when the origin is inside.</summary>
    LegacyBox = 0,
    /// <summary>Voxel shapes report the direction of travel when a point 0.001 along the segment is inside.</summary>
    VoxelForwardInside = 1,
    /// <summary>Voxel shapes report the opposite direction when a point 0.001 along the segment is inside.</summary>
    VoxelOppositeInside = 2,
    /// <summary>Legacy stair lists select the nearest hit within each box, then the hit farthest from the segment end. A hit exactly at the end is excluded.</summary>
    LegacyBoxList = 3,
}

/// <summary>Contextual outline geometry for block targeting. A false result means the source has no measurement for the world state.</summary>
public interface IBlockTargetingSource : IBlockShapeSource
{
    /// <summary>Resolves outline and interaction geometry using the world and the main-hand item used by vanilla's collision context.</summary>
    bool TryGetTargetingShape(Umpk.Game.World.World world, BlockPos position, Identifier heldItem, out BlockTargetingShape shape);
}
