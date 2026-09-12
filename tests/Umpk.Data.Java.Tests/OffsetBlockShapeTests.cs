using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>The two shape-bearing blocks that carry vanilla's per-position <c>OffsetType.XZ</c> offset. Their TABLE entry must be the block's own declared box, un-offset; the position half belongs to the engine (<c>Umpk.Physics.BlockShapeOffset</c>), because it cannot be expressed in a table at all.</summary>
/// <remarks>
/// <para>Every listed protocol is checked because the shape pool is shared across bands. The stored boxes must remain centered; the engine applies each position-dependent offset at runtime.</para>
/// </remarks>
public sealed class OffsetBlockShapeTests
{
    /// <summary>A column of width <c>w</c> is centred on 8/16, so the horizontal span is <c>(8 - w/2)/16 .. (8 + w/2)/16</c>.</summary>
    private static (double Min, double Max) Column(double widthPixels) =>
        ((8.0 - (widthPixels / 2.0)) / 16.0, (8.0 + (widthPixels / 2.0)) / 16.0);

    /// <summary>the pointed-dripstone behavior.</summary>
    [Theory]
    [InlineData(755, "tip", "up", 6.0, 0.0, 11.0)]
    [InlineData(763, "tip", "up", 6.0, 0.0, 11.0)]
    [InlineData(773, "tip", "up", 6.0, 0.0, 11.0)]
    [InlineData(774, "tip", "up", 6.0, 0.0, 11.0)]
    [InlineData(775, "tip", "up", 6.0, 0.0, 11.0)]
    [InlineData(775, "tip", "down", 6.0, 5.0, 16.0)]
    [InlineData(775, "tip_merge", "up", 6.0, 0.0, 16.0)]
    [InlineData(775, "frustum", "up", 8.0, 0.0, 16.0)]
    [InlineData(775, "middle", "up", 10.0, 0.0, 16.0)]
    [InlineData(775, "base", "up", 12.0, 0.0, 16.0)]
    public void PointedDripstone_IsTheUnOffsetVanillaColumn(
        int protocol, string thickness, string direction, double widthPixels, double minYPixels, double maxYPixels)
    {
        Aabb box = SingleShapeOf(
            protocol, "minecraft:pointed_dripstone",
            ("thickness", thickness), ("vertical_direction", direction));

        (double min, double max) = Column(widthPixels);

        Assert.Equal(min, box.MinX, 9);
        Assert.Equal(max, box.MaxX, 9);
        Assert.Equal(min, box.MinZ, 9);
        Assert.Equal(max, box.MaxZ, 9);
        Assert.Equal(minYPixels / 16.0, box.MinY, 9);
        Assert.Equal(maxYPixels / 16.0, box.MaxY, 9);
    }

    /// <summary>Bamboo uses a three-pixel centered column before its position-dependent offset is applied.</summary>
    [Theory]
    [InlineData(477)]
    [InlineData(578)]
    [InlineData(755)]
    [InlineData(774)]
    [InlineData(775)]
    public void Bamboo_IsTheUnOffsetVanillaColumn(int protocol)
    {
        Aabb box = SingleShapeOf(protocol, "minecraft:bamboo");
        (double min, double max) = Column(3.0);

        Assert.Equal(min, box.MinX, 9);
        Assert.Equal(max, box.MaxX, 9);
        Assert.Equal(min, box.MinZ, 9);
        Assert.Equal(max, box.MaxZ, 9);
        Assert.Equal(0.0, box.MinY, 9);
        Assert.Equal(1.0, box.MaxY, 9);
    }

    /// <summary>Both un-offset boxes must remain centered on the cell in both horizontal axes.</summary>
    [Theory]
    [InlineData("minecraft:pointed_dripstone")]
    [InlineData("minecraft:bamboo")]
    public void OffsetBlockShapes_AreCentredOnTheCell(string blockName)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(775).Blocks;
        Assert.True(blocks.TryGetValue(Identifier.Parse(blockName), out BlockDefinition? definition));

        IBlockShapeSource shapes = JavaGameData.BlockShapes(775);

        for (int state = definition.MinStateId; state <= definition.MaxStateId; state++)
        {
            ReadOnlySpan<Aabb> boxes = shapes.GetCollisionShapes(state);
            foreach (Aabb box in boxes)
            {
                Assert.Equal(0.5, (box.MinX + box.MaxX) / 2.0, 9);
                Assert.Equal(0.5, (box.MinZ + box.MaxZ) / 2.0, 9);
            }
        }
    }

    /// <summary>The invariant the whole design decision rests on: an offset box NEVER leaves its own cell, so no per-cell walkability verdict can change and the planner is correct to keep reading the un-offset table. <c>maxHorizontalOffset</c> is bounded by the widest shape's own inset; this test verifies that bound directly.</summary>
    /// <remarks>The bound is TIGHT for <c>base</c> dripstone (0.125 inset against a 0.125 clamp) and for bamboo (0.40625 against 0.25), so this test would fail immediately if either block's declared box or either block's maximum offset changes.</remarks>
    [Theory]
    [InlineData("minecraft:pointed_dripstone", 0.125)]
    [InlineData("minecraft:bamboo", 0.25)]
    public void AnOffsetBox_NeverLeavesItsOwnCell(string blockName, double maxOffset)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(775).Blocks;
        Assert.True(blocks.TryGetValue(Identifier.Parse(blockName), out BlockDefinition? definition));

        IBlockShapeSource shapes = JavaGameData.BlockShapes(775);

        for (int state = definition.MinStateId; state <= definition.MaxStateId; state++)
            foreach (Aabb box in shapes.GetCollisionShapes(state))
            {
                Assert.True(
                    box.MinX - maxOffset >= -1.0E-9 && box.MaxX + maxOffset <= 1.0 + 1.0E-9,
                    $"{blockName} state {state} leaves its cell in X at the clamp: "
                    + $"[{box.MinX} - {maxOffset}, {box.MaxX} + {maxOffset}]");
                Assert.True(
                    box.MinZ - maxOffset >= -1.0E-9 && box.MaxZ + maxOffset <= 1.0 + 1.0E-9,
                    $"{blockName} state {state} leaves its cell in Z at the clamp: "
                    + $"[{box.MinZ} - {maxOffset}, {box.MaxZ} + {maxOffset}]");
            }

    }

    /// <summary>The premise <c>BlockShapeOffset.For</c>'s fast path rests on: an offset block is never flagged <c>Solid</c>. That guard is what keeps an ordinary full-cube floor off the registry-lookup path on every cell of every collision search on every tick, and it is only safe because <c>BlockAttributeResolver.ClassifyShape</c> sets <c>Solid</c> exactly when a state's collision is ONE box and that box is the full unit cube - which a 6/16 or 3/16 column never is.</summary>
    /// <remarks>This verifies the fast-path premise against every shipped state for the selected protocols.</remarks>
    [Theory]
    [InlineData(775, "minecraft:pointed_dripstone")]
    [InlineData(775, "minecraft:bamboo")]
    [InlineData(774, "minecraft:pointed_dripstone")]
    [InlineData(774, "minecraft:bamboo")]
    [InlineData(755, "minecraft:pointed_dripstone")]
    [InlineData(477, "minecraft:bamboo")]
    public void OffsetBlocks_AreNotFlaggedSolid(int protocol, string blockName)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        Assert.True(blocks.TryGetValue(Identifier.Parse(blockName), out BlockDefinition? definition));

        for (int state = definition.MinStateId; state <= definition.MaxStateId; state++)
            Assert.False(
                (definition.FlagsForState(state) & Umpk.Game.Blocks.BlockFlags.Solid) != 0,
                $"protocol {protocol}: {blockName} state {state} is flagged Solid, which would make "
                + "BlockShapeOffset.For's fast path skip it and silently drop the offset");

    }

    private static Aabb SingleShapeOf(int protocol, string blockName, params (string Name, string Value)[] properties)
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
            int chosen = values.ToList().IndexOf(value);
            Assert.True(chosen >= 0, $"protocol {protocol}: {blockName} '{name}' domain has no '{value}'");

            int stride = 1;
            for (int i = index + 1; i < definition.Properties.Count; i++)
                stride *= definition.Properties[i].Values.Count;

            offset += chosen * stride;
        }

        ReadOnlySpan<Aabb> boxes = JavaGameData.BlockShapes(protocol)
            .GetCollisionShapes(definition.MinStateId + offset);

        Assert.True(boxes.Length == 1, $"protocol {protocol}: {blockName} has {boxes.Length} boxes, expected 1");
        return boxes[0];
    }
}
