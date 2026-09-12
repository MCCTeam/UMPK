namespace Umpk.Physics.Tests.Fixtures;

/// <summary>The small set of block kinds the physics fixtures need. Each maps to one deterministic state id in <see cref="FixtureBlockData"/> and carries the flags/scalars the engine reads. This is the hand-built shape/flag table tests supply themselves over an in-memory block map.</summary>
public enum BlockKind
{
    /// <summary>Empty, no collision.</summary>
    Air = 0,

    /// <summary>Full solid cube, default friction 0.6.</summary>
    Stone,

    /// <summary>Full solid cube, ice friction 0.98.</summary>
    Ice,

    /// <summary>Blue ice, friction 0.989.</summary>
    BlueIce,

    /// <summary>Slime block, friction 0.8, bounce/step-on.</summary>
    SlimeBlock,

    /// <summary>Soul sand, speed factor 0.4.</summary>
    SoulSand,

    /// <summary>Honey block, speed factor 0.4, jump factor 0.5.</summary>
    HoneyBlock,

    /// <summary>Ladder, climbable, no collision.</summary>
    Ladder,

    /// <summary>Water fluid.</summary>
    Water,

    /// <summary>Lava fluid.</summary>
    Lava,

    /// <summary>Glass full cube (solid, default friction).</summary>
    Glass,

    /// <summary>A slab: solid, half-height collision box (0..0.5).</summary>
    Slab,

    /// <summary>Flowing water at block level 6 (fluid amount 2, surface height 2/9): knee-deep.</summary>
    FlowingWater,

    /// <summary>Flowing lava at block level 6 (fluid amount 2, surface height 2/9): knee-deep.</summary>
    FlowingLava,

    /// <summary>Water at block level 1 (fluid amount 7), one step down from a source.</summary>
    WaterLevel1,

    /// <summary>Water at block level 2 (fluid amount 6).</summary>
    WaterLevel2,

    /// <summary>Water at block level 3 (fluid amount 5).</summary>
    WaterLevel3,

    /// <summary>Water at block level 4 (fluid amount 4).</summary>
    WaterLevel4,

    /// <summary>Water at block level 5 (fluid amount 3, surface height 3/9): under the 0.4 depth scale.</summary>
    WaterLevel5,

    /// <summary>Water at block level 7 (fluid amount 1, surface height 1/9): the thinnest sheet.</summary>
    WaterLevel7,

    /// <summary>Water at block level 8: the FALLING state (amount 8, falling flag set).</summary>
    FallingWater,

    /// <summary>Lava at block level 1 (fluid amount 7).</summary>
    LavaLevel1,

    /// <summary>Lava at block level 2 (fluid amount 6).</summary>
    LavaLevel2,

    /// <summary>Kelp: no collision shape at all, and <c>BlockFlags.Waterlogged</c>, which is exactly what <c>BlockAttributeResolver</c> emits for it once <c>intrinsically_waterlogged</c> carries it. A kelp cell is a full water source cell, and the fixture says so with the same flag the dataset does.</summary>
    Kelp,

    /// <summary>Scaffolding, carrying four full-height 2/16 corner posts plus the 0.875-1.0 top plate and the <c>Climbable</c> flag. The only climbable in the game whose collision shape can hold a body's footprint, and the only block whose collision shape differs for a body inside it and one standing on it.</summary>
    Scaffolding,

    /// <summary>Cobweb: an empty collision shape with no flags. Its movement slowdown is the only observable difference from air for a body inside it.</summary>
    Cobweb,

    /// <summary>An upward bubble column, <c>bubble_column[drag=false]</c>, produced by a soul-sand base. Water - its fluid state is an unconditional source - with no collision shape, plus the lift.</summary>
    BubbleColumnUp,

    /// <summary>A DOWNWARD bubble column, <c>bubble_column[drag=true]</c>, from a magma base. Water with no collision shape and no modeled downdraft.</summary>
    BubbleColumnDown,

    /// <summary>Hay: an ordinary full solid cube to the engine. Its 0.2 fall-damage multiplier lives in landing damage, which this engine does not run because damage is server-owned, so what the fixture pins here is the ABSENCE of a bounce - the thing that separates hay from slime for an executor.</summary>
    HayBlock,

    /// <summary>The pre-flattening <c>minecraft:slime</c> spelling for block id 165, retained so the engine's slime predicate is tested on both identifiers.</summary>
    LegacySlimeBlock,

    /// <summary>Soul soil: speed factor 1.0, but in <c>BlockTags.SOUL_SPEED_BLOCKS</c> alongside soul sand. It is the exact inverse of the honey control: behaviourally identical to stone except for the tag, so an implementation that recognises soul blocks by <c>speedFactor == 0.4</c> instead of by the tag fails on soul soil BY OMISSION - it refuses a boost vanilla grants - which honey cannot catch.</summary>
    SoulSoil,

    /// <summary>Powder snow: the empty collision shape and no flags. The full cube a body wearing leather boots meets is not in the table: collision context synthesizes it, which <see cref="Umpk.Physics.PowderSnowCollision"/> reproduces.</summary>
    PowderSnow,

    /// <summary><c>minecraft:pointed_dripstone</c>, carrying the tip shape <c>column-shape construction</c> = <c>[0.3125, 0, 0.3125 .. 0.6875, 0.6875, 0.6875]</c>), i.e. the box BEFORE <c>state.getOffset(pos)</c> is applied. The offset is the engine's job, not the table's; that split is the whole point of <see cref="Umpk.Physics.BlockShapeOffset"/>.</summary>
    PointedDripstone,

    /// <summary><c>minecraft:bamboo</c>, carrying the collision shape <c>column-shape construction</c> = <c>[0.40625, 0, 0.40625 .. 0.59375, 1, 0.59375]</c>), again un-offset. Bamboo does not override the maximum horizontal offset, so it travels the full default 0.25 where dripstone travels 0.125 - which is why both are in the suite rather than only the one that stalled.</summary>
    Bamboo,
}
