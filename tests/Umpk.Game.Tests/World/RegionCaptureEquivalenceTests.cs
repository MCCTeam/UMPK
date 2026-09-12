using Umpk.Game.World;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Game.Tests.World;

/// <summary><see cref="Umpk.Game.World.World.CopyRegion"/> agrees with the live world it was copied from, cell for cell, over a fixture that carries every section encoding at once.</summary>
/// <remarks>
/// <para>This is the oracle for changing HOW a region is copied. The capture is the pathfinder's whole world: if one cell differs, the planner walks into a block that is not there, and it does so silently, because nothing downstream compares the copy against the original. So the assertion is the strong one - every cell in the box, not a sample - and the fixture is built to make the interesting cases reachable rather than hoping a random world contains them:</para>
/// <list type="bullet">
/// <item>single-value sections (a whole section of one id, which is what air and bedrock really are),</item>
/// <item>indirect sections at the 4-bit floor and at a wider palette,</item>
/// <item>a direct-width section, whose id set is too large for any palette,</item>
/// <item>absent sections inside a loaded column, and absent columns inside the box,</item>
/// <item>a box that straddles chunk borders on both axes, spans negative coordinates, and runs past
/// the top and bottom of the world.</item>
/// </list>
/// <para>The reference side is <see cref="Umpk.Game.World.World.GetBlockStateId"/>, the live read path, which is stated independently of whatever the capture does internally.</para>
/// </remarks>
public class RegionCaptureEquivalenceTests
{
    /// <summary>A world whose sections between y = -64 and y = 128 cover every encoding.</summary>
    private static Umpk.Game.World.World BuildMixedWorld()
    {
        Umpk.Game.World.World world = WorldTestData.NewWorld();
        var rng = new Random(20260826);

        for (int cx = -2; cx <= 1; cx++)
        {
            for (int cz = -2; cz <= 1; cz++)
            {
                // One column inside the box is never loaded at all.
                if (cx == 1 && cz == 1)
                    continue;

                ChunkColumn column = world.LoadColumn(new ChunkPos(cx, cz));

                for (int sectionIndex = 0; sectionIndex < column.SectionCount; sectionIndex++)
                {
                    int sectionMinY = column.MinY + (sectionIndex * ChunkSection.Size);
                    if (sectionMinY is < -64 or > 128)
                        continue;

                    switch (Math.Abs(sectionMinY / ChunkSection.Size + cx + cz) % 5)
                    {
                        case 0:
                            // Absent: a hole in a loaded column.
                            break;

                        case 1:
                            column.SetSection(sectionIndex, ChunkSection.Filled(WorldTestData.StoneState, 1));
                            break;

                        case 2:
                            FillSection(column, sectionIndex, rng, distinctIds: 3, biomeIds: 1);
                            break;

                        case 3:
                            FillSection(column, sectionIndex, rng, distinctIds: 200, biomeIds: 3);
                            break;

                        default:
                            // Past the indirect ceiling: the container promotes to the direct width.
                            FillSection(column, sectionIndex, rng, distinctIds: 900, biomeIds: 2);
                            break;
                    }
                }
            }
        }

        return world;
    }

    private static void FillSection(ChunkColumn column, int sectionIndex, Random rng, int distinctIds, int biomeIds)
    {
        ChunkSection section = column.GetOrCreateSection(sectionIndex);
        for (int i = 0; i < ChunkSection.BlockCells; i++)
        {
            int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
            section.SetBlockStateId(x, y, z, rng.Next(distinctIds));
        }

        for (int i = 0; i < ChunkSection.BiomeCells; i++)
        {
            int x = i & 3, z = (i >> 2) & 3, y = i >> 4;
            section.SetBiomeId(x, y, z, rng.Next(biomeIds));
        }
    }

    private static int ReferenceBiomeId(Umpk.Game.World.World world, BlockPos pos)
    {
        ChunkSection? section = world.GetColumn(pos)?.GetSectionForY(pos.Y);
        return section?.GetBiomeId((pos.X & 15) >> 2, (pos.Y & 15) >> 2, (pos.Z & 15) >> 2) ?? 0;
    }

    [Fact]
    public void CopyRegion_ReadsTheSameBlockAsTheWorld_ForEveryCellInTheBox()
    {
        Umpk.Game.World.World world = BuildMixedWorld();
        var min = new BlockPos(-20, -70, -20);
        var max = new BlockPos(19, 135, 19);

        RegionSnapshot snapshot = world.CopyRegion(min, max);

        long compared = 0;
        for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
                for (int x = min.X; x <= max.X; x++)
                {
                    var pos = new BlockPos(x, y, z);
                    int expected = world.GetBlockStateId(pos);
                    int actual = snapshot.GetBlockStateId(pos);
                    if (expected != actual)
                        Assert.Fail($"capture disagrees at {x},{y},{z}: world {expected}, snapshot {actual}");

                    compared++;
                }

        Assert.Equal(snapshot.CellCount, compared);
        Assert.Equal(40L * 206 * 40, snapshot.CellCount);
    }

    [Fact]
    public void CopyRegion_ReadsTheSameBiomeAsTheWorld_WhenBiomesAreRequested()
    {
        Umpk.Game.World.World world = BuildMixedWorld();
        var min = new BlockPos(-20, -70, -20);
        var max = new BlockPos(19, 135, 19);

        RegionSnapshot snapshot = world.CopyRegion(min, max, includeBiomes: true);

        Assert.True(snapshot.HasBiomes);
        for (int y = min.Y; y <= max.Y; y += 4)
            for (int z = min.Z; z <= max.Z; z += 4)
                for (int x = min.X; x <= max.X; x += 4)
                {
                    var pos = new BlockPos(x, y, z);
                    int expected = ReferenceBiomeId(world, pos);
                    int actual = snapshot.GetBiomeId(pos);
                    if (expected != actual)
                        Assert.Fail($"biome capture disagrees at {x},{y},{z}: world {expected}, snapshot {actual}");

                }

    }

    /// <summary>A capture without biomes reports none and reads 0, whatever the world holds.</summary>
    [Fact]
    public void CopyRegion_WithoutBiomes_ReportsNoneAndReadsZero()
    {
        Umpk.Game.World.World world = BuildMixedWorld();
        RegionSnapshot snapshot = world.CopyRegion(new BlockPos(-4, 0, -4), new BlockPos(4, 32, 4));

        Assert.False(snapshot.HasBiomes);
        Assert.Equal(0, snapshot.GetBiomeId(new BlockPos(0, 16, 0)));
    }

    /// <summary>Outside the box is air, on every axis and in both directions.</summary>
    [Fact]
    public void CopyRegion_ReadsAirOutsideTheBox()
    {
        Umpk.Game.World.World world = BuildMixedWorld();
        var min = new BlockPos(-4, 0, -4);
        var max = new BlockPos(4, 32, 4);
        RegionSnapshot snapshot = world.CopyRegion(min, max);

        BlockPos[] outside =
        [
            new(min.X - 1, 16, 0), new(max.X + 1, 16, 0),
            new(0, min.Y - 1, 0), new(0, max.Y + 1, 0),
            new(0, 16, min.Z - 1), new(0, 16, max.Z + 1),
        ];

        foreach (BlockPos pos in outside)
        {
            Assert.False(snapshot.Contains(pos));
            Assert.Equal(0, snapshot.GetBlockStateId(pos));
            Assert.True(snapshot.GetBlock(pos).IsAir);
        }
    }

    /// <summary>The capture is a copy, not a window: a write anywhere in the box after the capture - including one that promotes a section's encoding - is invisible to the snapshot.</summary>
    [Fact]
    public void CopyRegion_IsUnaffectedByLaterWrites_IncludingEncodingPromotions()
    {
        Umpk.Game.World.World world = WorldTestData.NewWorld();
        ChunkColumn column = world.LoadColumn(new ChunkPos(0, 0));
        column.SetSection(column.Dimension.SectionIndexForY(64), ChunkSection.Filled(WorldTestData.StoneState));

        RegionSnapshot snapshot = world.CopyRegion(new BlockPos(0, 60, 0), new BlockPos(15, 79, 15));

        // Force the single-value container through promote-to-indirect and on to a wide palette.
        for (int i = 0; i < 400; i++)
            world.SetBlockStateId(new BlockPos(i & 15, 64 + ((i >> 4) & 15), (i >> 8) & 15), 1000 + i);

        for (int y = 60; y <= 79; y++)
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                {
                    int expected = y is >= 64 and <= 79 ? WorldTestData.StoneState : 0;
                    Assert.Equal(expected, snapshot.GetBlockStateId(new BlockPos(x, y, z)));
                }

    }
}
