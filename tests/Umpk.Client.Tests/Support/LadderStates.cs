using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Picks the ladder block-state a ladder test means to place. There are two different right answers and the difference is not cosmetic, so both are spelled out here rather than re-derived per test file.</summary>
/// <remarks>
/// <para>Whichever rule is used, the state must be the DRY one. A waterlogged ladder is water as far as physics is concerned (<c>PlayerPhysics.IsWater</c> is <c>(IsFluid &amp;&amp; !IsLava) || IsWaterlogged</c>), so a fixture built from a waterlogged rung runs <c>TravelInWater</c> instead of the climb branch: the resting Y velocity becomes <c>-gravity/16 = -0.005</c>, <c>HandleJumping</c> takes the <c>JumpInLiquid</c> arm and adds a flat 0.04 every tick, and water drag replaces the 0.1176 per tick a real climb rises at. Ladder fixtures must select a dry state rather than the first matching collision shape, which on protocol 772 is waterlogged state 4752.</para>
/// </remarks>
internal static class LadderStates
{
    /// <summary>A dry ladder whose single collision slab sits against the block's -Z face, for a test that builds its own wall at z-1. Selected by SHAPE, because the shape is the only thing the collision detector reads, so a fixture chosen this way is self-consistent whatever the state is named.</summary>
    internal static int ShapedAgainstTheNorthWall(
        Registry<BlockDefinition> blocks, IBlockDataSource data, IBlockShapeSource shapes)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(shapes);
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("ladder"), out BlockDefinition? definition));
        for (int id = definition.MinStateId; id <= definition.MaxStateId; id++)
        {
            var state = new BlockState(data, id);
            if (state.IsWaterlogged)
                continue;

            ReadOnlySpan<Aabb> boxes = shapes.GetCollisionShapes(state);
            if (boxes.Length == 1 && boxes[0].MinZ == 0.0 && boxes[0].MaxZ < 0.2)
                return id;

        }

        Assert.Fail("no DRY ladder state has a collision box on its -Z face");
        return 0;
    }

    /// <summary>The state id a vanilla server assigns to <c>minecraft:ladder[facing=south,waterlogged=false]</c>, which is what <c>setblock</c> places against a wall at z-1. Used where a test must be faithful to the state that actually arrives on the wire, shape table and all.</summary>
    /// <remarks>Pre-flattening there are no property names, and the state id is <c>(id &lt;&lt; 4) | meta</c> with the ladder's metadata encodes facing as 2 north, 3 south, 4 west, and 5 east, which is what the fixture writes as <c>setblock ... minecraft:ladder 3</c>.</remarks>
    internal static int FacingSouthDry(Registry<BlockDefinition> blocks, IBlockDataSource data)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(data);
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("ladder"), out BlockDefinition? definition));
        for (int id = definition.MinStateId; id <= definition.MaxStateId; id++)
        {
            if (!data.TryGetPropertyValue(id, "facing", out string facing)
                || !string.Equals(facing, "south", StringComparison.Ordinal))
                continue;

            // A pre-1.20.5 ladder has no waterlogged property at all; treat its absence as dry.
            if (data.TryGetPropertyValue(id, "waterlogged", out string waterlogged)
                && !string.Equals(waterlogged, "false", StringComparison.Ordinal))
                continue;

            return id;
        }

        // Pre-flattening: no properties are carried, so use the era's meta directly.
        Assert.Equal(definition.MinStateId, definition.MinStateId & ~0xF);
        return definition.MinStateId | 3;
    }
}
