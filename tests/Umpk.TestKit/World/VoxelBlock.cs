namespace Umpk.TestKit.World;

/// <summary>The small block vocabulary the TestKit voxel world exposes. Each maps to one deterministic state id and a collision shape. It is intentionally minimal and grows only with consumer-test needs.</summary>
public enum VoxelBlock
{
    /// <summary>Empty, no collision.</summary>
    Air = 0,

    /// <summary>Full solid cube, default friction 0.6.</summary>
    Stone = 1,

    /// <summary>Full solid cube, ice friction 0.98.</summary>
    Ice = 2,

    /// <summary>Bottom slab: solid, half-height collision box (0..0.5).</summary>
    Slab = 3,

    /// <summary>Ladder: climbable, no collision.</summary>
    Ladder = 4,

    /// <summary>Water fluid, no collision.</summary>
    Water = 5,
}
