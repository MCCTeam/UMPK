namespace Umpk.Physics;

/// <summary>The block iterator's shape-intersection behavior. Shape identity, merge order and query construction are separate from a shape's bounding boxes.</summary>
public enum BlockCollisionQueryEra
{
    /// <summary>Use bounding-box overlap when exact voxel data is unavailable.</summary>
    BoundingBoxes = 0,

    /// <summary>Intersect the query voxel shape first, with floor-based query-grid detection.</summary>
    QueryFirst = 1,

    /// <summary>Use the canonical full-block AABB shortcut; intersect other block grids first.</summary>
    BlockFirst = 2,

    /// <summary>Also discard thin query shapes and use rounded query-grid detection.</summary>
    Modern = 3,

    /// <summary>Use query-first merging with the 1.13 strict plane-coalescing comparison, preserving planes exactly one epsilon apart.</summary>
    QueryFirstStrict = 4,
}
