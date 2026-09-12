using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>The <c>minecraft:piston_head</c> collision geometry the client's moving-piston model reads.</summary>
/// <remarks>
/// <para><c>MovingPiston</c> computes displacement from this shape and nothing else, so a dataset that answered the wrong boxes, or the default facing's boxes for every facing, would give a plausible-looking push with the wrong displacement.</para>
/// <para>The piston-head shape combines a 16-by-16 plate with a 4-by-4 arm. The short variant retracts the arm by four pixels. The default facing is north, so the template plate is z in [0, 4/16] and the arm z in [4/16, 20/16]. Rotated to EAST the plate is x in [12/16, 1] and the arm x in [-4/16, 12/16].</para>
/// </remarks>
public sealed class PistonHeadShapeTests
{
    /// <summary>Label used by the selected cross-protocol piston cases.</summary>
    private const string SweepFailProtocols = nameof(SweepFailProtocols);

    private const double Sixteenth = 1.0 / 16.0;

    [Theory]
    [InlineData(404)]
    [InlineData(498)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    public void PistonHeadEast_HasVanillaPlateAndArm(int protocol)
    {
        Aabb[] boxes = ShapeOf(protocol, "minecraft:piston_head", ("facing", "east"), ("short", "false"));

        // Asserted on the UNION, not on the box list. The datasets do not all decompose this shape the same way: most bands use two boxes, and at least one uses a five-box greedy decomposition of the identical union. The union, rather than box count, determines motion.
        Assert.Equal(-4 * Sixteenth, MinAlong(boxes, 0), 9);
        Assert.Equal(1.0, MaxAlong(boxes, 0), 9);
        Assert.Equal(0.0, MinAlong(boxes, 1), 9);
        Assert.Equal(1.0, MaxAlong(boxes, 1), 9);
        Assert.Equal(0.0, MinAlong(boxes, 2), 9);
        Assert.Equal(1.0, MaxAlong(boxes, 2), 9);

        // The plate has to be the FULL 16x16 face, or a player standing anywhere but the middle of the block would not be pushed. Summing the Y/Z areas of the boxes clipped to the outer 1/16 of the block tests that without caring how the union was cut up: a valid decomposition is disjoint, so a full face sums to exactly 1.
        Assert.Equal(1.0, FaceAreaInSlab(boxes, 15 * Sixteenth, 1.0), 9);

        // The arm, 4/16 square on Y/Z, is the only thing outside the block.
        Aabb arm = OnlyBoxReaching(boxes, MinAlong(boxes, 0));
        Assert.Equal(6 * Sixteenth, arm.MinY, 9);
        Assert.Equal(10 * Sixteenth, arm.MaxY, 9);
        Assert.Equal(6 * Sixteenth, arm.MinZ, 9);
        Assert.Equal(10 * Sixteenth, arm.MaxZ, 9);
    }

    /// <summary>The push value is <c>area.maxX - player.minX</c> for an EAST push, so the ONE number that decides it is the plate's <c>maxX</c>. Pinned on its own because a dataset that clamped every box into [0,1] would still pass an "is there an arm" check while silently moving the plate.</summary>
    [Theory]
    [InlineData(404)]
    [InlineData(498)]
    [InlineData(773)]
    [InlineData(776)]
    public void PistonHeadPlate_ReachesTheBlockFace_OnEveryHorizontalFacing(int protocol)
    {
        Assert.Equal(1.0, MaxAlong(ShapeOf(protocol, "minecraft:piston_head", ("facing", "east"), ("short", "false")), 0), 9);
        Assert.Equal(0.0, MinAlong(ShapeOf(protocol, "minecraft:piston_head", ("facing", "west"), ("short", "false")), 0), 9);
        Assert.Equal(1.0, MaxAlong(ShapeOf(protocol, "minecraft:piston_head", ("facing", "south"), ("short", "false")), 2), 9);
        Assert.Equal(0.0, MinAlong(ShapeOf(protocol, "minecraft:piston_head", ("facing", "north"), ("short", "false")), 2), 9);
    }

    /// <summary>The four horizontal facings must be four DIFFERENT shapes. Without this, a table that answered the default facing's boxes for every state would satisfy every check above on the default and still be wrong on the other three.</summary>
    [Theory]
    [InlineData(404)]
    [InlineData(773)]
    [InlineData(776)]
    public void PistonHeadFacings_AreFourDistinctShapes(int protocol)
    {
        Aabb[] east = ShapeOf(protocol, "minecraft:piston_head", ("facing", "east"), ("short", "false"));
        Aabb[] west = ShapeOf(protocol, "minecraft:piston_head", ("facing", "west"), ("short", "false"));
        Aabb[] north = ShapeOf(protocol, "minecraft:piston_head", ("facing", "north"), ("short", "false"));
        Aabb[] south = ShapeOf(protocol, "minecraft:piston_head", ("facing", "south"), ("short", "false"));

        Assert.NotEqual(east, west);
        Assert.NotEqual(east, north);
        Assert.NotEqual(east, south);
        Assert.NotEqual(north, south);
    }

    /// <summary><c>SHORT=true</c> shortens the arm by 4/16 and must not touch the plate. A retracting source piston selects it from <c>progress &gt; 0.25F</c> mid-retraction, so the two shapes have to be distinguishable.</summary>
    [Theory]
    [InlineData(404)]
    [InlineData(773)]
    [InlineData(776)]
    public void PistonHeadShort_ShortensTheArmOnly(int protocol)
    {
        Aabb[] full = ShapeOf(protocol, "minecraft:piston_head", ("facing", "east"), ("short", "false"));
        Aabb[] shortened = ShapeOf(protocol, "minecraft:piston_head", ("facing", "east"), ("short", "true"));

        Assert.Equal(1.0, MaxAlong(full, 0), 9);
        Assert.Equal(1.0, MaxAlong(shortened, 0), 9);
        Assert.Equal(-4 * Sixteenth, MinAlong(full, 0), 9);
        Assert.Equal(0.0, MinAlong(shortened, 0), 9);
    }

    /// <summary>The state the moving-piston model asks for must EXIST on every protocol it runs on. A missing block or a missing property value would make the client silently skip the push.</summary>
    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    [InlineData(498)]
    [InlineData(578)]
    [InlineData(736)]
    [InlineData(758)]
    [InlineData(765)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    public void PistonHeadStateIsResolvable_OnEveryFlattenedBand(int protocol)
    {
        foreach (string facing in new[] { "north", "south", "east", "west", "up", "down" })
        {
            Aabb[] boxes = ShapeOf(protocol, "minecraft:piston_head", ("facing", facing), ("short", "false"));
            Assert.NotEmpty(boxes);
        }
    }

    /// <summary>The static table gives <c>minecraft:moving_piston</c> no collision boxes, which is correct because its shape comes from the block entity. This also means there is no static shape to suppress during movement.</summary>
    [Theory]
    [InlineData(404)]
    [InlineData(773)]
    [InlineData(776)]
    public void MovingPiston_HasNoStaticCollisionBoxes(int protocol)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        Assert.True(blocks.TryGetValue(Identifier.Parse("minecraft:moving_piston"), out BlockDefinition? definition));
        for (int state = definition.MinStateId; state <= definition.MaxStateId; state++)
            Assert.Empty(JavaGameData.BlockShapes(protocol).GetCollisionShapes(state).ToArray());

    }

    /// <summary>The collision boxes of the state that has every requested property value, with any property not named left at its first value. State ids use an odometer over name-sorted properties with the last property varying fastest.</summary>
    private static Aabb[] ShapeOf(int protocol, string blockName, params (string Name, string Value)[] properties)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        Assert.True(
            blocks.TryGetValue(Identifier.Parse(blockName), out BlockDefinition? definition),
            $"protocol {protocol} has no block {blockName}");

        int offset = 0;
        foreach ((string name, string value) in properties)
        {
            int index = -1;
            for (int i = 0; i < definition.Properties.Count; i++)
                if (definition.Properties[i].Name == name)
                {
                    index = i;
                    break;
                }

            Assert.True(index >= 0, $"protocol {protocol}: {blockName} has no '{name}' property");

            IReadOnlyList<string> values = definition.Properties[index].Values;
            int chosen = -1;
            for (int i = 0; i < values.Count; i++)
                if (values[i] == value)
                {
                    chosen = i;
                    break;
                }

            Assert.True(chosen >= 0, $"protocol {protocol}: {blockName} '{name}' domain has no '{value}'");

            int stride = 1;
            for (int i = index + 1; i < definition.Properties.Count; i++)
                stride *= definition.Properties[i].Values.Count;

            offset += chosen * stride;
        }

        return JavaGameData.BlockShapes(protocol).GetCollisionShapes(definition.MinStateId + offset).ToArray();
    }

    /// <summary>The Y/Z area the shape covers inside an X slab. A valid AABB decomposition is disjoint, so a full block face sums to exactly 1 whichever way the union was cut.</summary>
    private static double FaceAreaInSlab(Aabb[] boxes, double fromX, double toX)
    {
        double area = 0;
        foreach (Aabb box in boxes)
            if (box.MinX < toX && box.MaxX > fromX)
                area += box.YSize * box.ZSize;

        return area;
    }

    /// <summary>The single box whose <c>MinX</c> is the shape's overall minimum: the piston arm.</summary>
    private static Aabb OnlyBoxReaching(Aabb[] boxes, double minX)
    {
        Aabb found = default;
        int count = 0;
        foreach (Aabb box in boxes)
            if (Math.Abs(box.MinX - minX) < 1e-9)
            {
                found = box;
                count++;
            }

        Assert.Equal(1, count);
        return found;
    }

    private static double MaxAlong(Aabb[] boxes, int axis)
    {
        double max = double.NegativeInfinity;
        foreach (Aabb box in boxes)
            max = Math.Max(max, box.Max(axis));

        return max;
    }

    private static double MinAlong(Aabb[] boxes, int axis)
    {
        double min = double.PositiveInfinity;
        foreach (Aabb box in boxes)
            min = Math.Min(min, box.Min(axis));

        return min;
    }
}
