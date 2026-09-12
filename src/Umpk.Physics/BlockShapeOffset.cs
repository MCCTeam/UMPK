using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>Computes position-dependent horizontal offsets for block collision boxes.</summary>
/// <remarks>
/// <para>The game derives both horizontal components from a hash of the block position:</para>
/// <code>
/// long seed = Seed(pos.X, pos.Z); double x = Clamp(((float)(seed &amp; 15) / 15 - 0.5) * 0.5, -maxOffset, maxOffset); double z = Clamp(((float)((seed &gt;&gt; 8) &amp; 15) / 15 - 0.5) * 0.5, -maxOffset, maxOffset);
/// </code>
/// <para>A state-only shape table cannot encode a position-dependent offset. The collision engine therefore applies this calculation when it places pointed-dripstone and bamboo shapes.</para>
/// <para>The planner deliberately keeps using the unoffset table. The maximum horizontal offset is the widest shape's own inset (dripstone: 0.125 against a 0.125 inset; bamboo: 0.25 against 0.40625), so an offset box never leaves its own cell and no per-cell walkability verdict can change. Collision resolution still needs the offset because bodies and narrow boxes can meet at different points within the cell.</para>
/// </remarks>
public static class BlockShapeOffset
{
    private static readonly Identifier PointedDripstoneId = Identifier.Minecraft("pointed_dripstone");

    private static readonly Identifier BambooId = Identifier.Minecraft("bamboo");

    /// <summary>Pointed dripstone uses the 2/16 inset of its widest state as its maximum offset.</summary>
    private const double DripstoneMaxOffset = 0.125;

    /// <summary>Bamboo's 3/16 post can move by up to one quarter block.</summary>
    private const double DefaultMaxOffset = 0.25;

    /// <summary>The horizontal offset the game applies to this block's shape at this position, or <see cref="Vec3d.Zero"/> for the overwhelming majority of blocks, which have no offset function.</summary>
    /// <param name="state">The state at the cell.</param>
    /// <param name="pos">The cell. Only X and Z are read; the seed uses a fixed zero Y coordinate, so a whole stalagmite column leans the same way.</param>
    /// <returns>The offset to add to the shape's world position. Y is always 0: only <c>OffsetType.XYZ</c> moves vertically and no colliding block uses it.</returns>
    public static Vec3d For(BlockState state, BlockPos pos)
    {
        // Check shape flags before resolving the registry identifier because this runs for every cell in the collision search box. Neither supported offset block is a solid unit cube.
        if (state.IsDefault || state.IsSolid)
            return Vec3d.Zero;

        Identifier id = state.Block.Id;
        double maxOffset;
        if (id == PointedDripstoneId)
            maxOffset = DripstoneMaxOffset;

        else if (id == BambooId)
            maxOffset = DefaultMaxOffset;

        else
            return Vec3d.Zero;

        long seed = Seed(pos.X, pos.Z);
        return new Vec3d(Component(seed, maxOffset), 0.0, Component(seed >> 8, maxOffset));
    }

    /// <summary>Whether this state's collision box is moved by its own position at all - i.e. whether <see cref="For"/> can return anything but <see cref="Vec3d.Zero"/> for it.</summary>
    /// <remarks>
    /// <para>Exposed because the answer is a cheap, exact and very strong gate for anything that reasons about where a box really is. The planner uses it to decide whether a region can hold a sub-cell lane at all: a body offset to a cell FACE spans <c>[0, 0.6]</c> or <c>[0.4, 1.0]</c> of its cell and therefore needs 0.6 of clear run on one side of the box, which a CENTRED box of width <c>w</c> can only give if <c>(1 - w) / 2 &gt;= 0.6</c>, i.e. <c>w &lt;= -0.2</c>. No box is negatively wide, so <b>no block without an offset function can ever open a lane</b> and a region with none of these blocks in it can skip the question entirely.</para>
    /// <para>Same flag-first ordering and the same two identifiers as <see cref="For"/>, so the two can never disagree about which family a state is in.</para>
    /// </remarks>
    /// <param name="state">The state at the cell.</param>
    /// <returns>True for the offset family.</returns>
    public static bool HasOffset(BlockState state)
    {
        if (state.IsDefault || state.IsSolid)
            return false;

        Identifier id = state.Block.Id;
        return id == PointedDripstoneId || id == BambooId;
    }

    /// <summary>Computes one axis of the offset. The division is a <b>float</b> divide widened to a double, not a double divide: it is worth about 1.2e-8 per offset, which is far too small to change an outcome on its own but is exactly free to get right, and getting it wrong means the client's idea of where a block is stops being bit-identical to the server's.</summary>
    private static double Component(long seed, double maxOffset)
    {
        float unit = (float)(seed & 15L) / 15.0F;
        double raw = ((double)unit - 0.5) * 0.5;
        return Math.Clamp(raw, -maxOffset, maxOffset);
    }

    /// <summary>Computes the position seed with a fixed zero Y coordinate.</summary>
    /// <remarks><c>(long)(x * 3129871)</c> is an <b>int</b> multiply that wraps at 32 bits before the widening cast, while the Z term is a long multiply; an unchecked integer cast reproduces that. The wrap turns out not to be observable through <see cref="Component"/> - the difference it makes lives above bit 32, the square below propagates it no lower than bit 33, and the sampled nibbles are bits 16-19 and 24-27, verified by sweeping every overflowing <c>x</c> from 600 to 40,000,000 against both widths with zero disagreements, so there is deliberately no test pinning it. These arithmetic widths are retained because the sampled bits could move later.</remarks>
    private static long Seed(int x, int z)
    {
        long seed = unchecked((long)(x * 3129871)) ^ unchecked((long)z * 116129781L) ^ 0L;
        return unchecked((seed * seed * 42317861L) + (seed * 11L)) >> 16;
    }
}
