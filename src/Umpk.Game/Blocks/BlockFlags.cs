namespace Umpk.Game.Blocks;

/// <summary>The per-state boolean facts <see cref="BlockState"/> exposes, packed so a generated data source can store one bitset per state id. Values are the flags surfaced for <see cref="BlockState"/>, plus the curated <see cref="Climbable"/> semantic. Physics-scalar inputs (friction, speed factor) are not flags; they are separate accessors on <see cref="IBlockDataSource"/>.</summary>
[Flags]
public enum BlockFlags
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>The state is an air state (<c>minecraft:air</c> and its cave/void variants).</summary>
    Air = 1 << 0,

    /// <summary>The state is a fluid source or flowing fluid (water or lava).</summary>
    Fluid = 1 << 1,

    /// <summary>The state has its <c>waterlogged</c> property set to true.</summary>
    Waterlogged = 1 << 2,

    /// <summary>The state blocks entity motion (has a non-empty collision shape that stops movement).</summary>
    BlocksMotion = 1 << 3,

    /// <summary>The state is a full solid block (occludes and supports on every face).</summary>
    Solid = 1 << 4,

    /// <summary>The state is climbable (ladder/vine/scaffolding), from curated flags.</summary>
    Climbable = 1 << 5,

    /// <summary>The state can be replaced by block placement (air, fluids, tall grass, ...).</summary>
    Replaceable = 1 << 6,
}
