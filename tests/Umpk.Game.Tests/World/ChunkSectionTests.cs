using Umpk.Game.World;
using Xunit;

namespace Umpk.Game.Tests.World;

public class ChunkSectionTests
{
    [Fact]
    public void SingleValue_ReadsFillEverywhere()
    {
        var section = ChunkSection.Filled(7);
        for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                    Assert.Equal(7, section.GetBlockStateId(x, y, z));

    }

    [Fact]
    public void SingleValue_PromotesToIndirect_OnFirstDifferingWrite()
    {
        var section = ChunkSection.Filled(0);
        section.SetBlockStateId(1, 2, 3, 42);

        Assert.Equal(42, section.GetBlockStateId(1, 2, 3));
        Assert.Equal(0, section.GetBlockStateId(0, 0, 0));
        Assert.Equal(0, section.GetBlockStateId(15, 15, 15));
    }

    [Fact]
    public void Indirect_RoundTrips_AllCells()
    {
        var section = ChunkSection.Filled(0);
        // Write a handful of distinct ids so the palette grows through several indirect widths.
        var expected = new int[16, 16, 16];
        int id = 1;
        for (int y = 0; y < 16; y += 3)
            for (int z = 0; z < 16; z += 5)
                for (int x = 0; x < 16; x += 7)
                {
                    section.SetBlockStateId(x, y, z, id);
                    expected[x, y, z] = id;
                    id++;
                }

        for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
                for (int x = 0; x < 16; x++)
                    Assert.Equal(expected[x, y, z], section.GetBlockStateId(x, y, z));

    }

    [Fact]
    public void PaletteGrowth_TransitionsIndirectToDirect()
    {
        var section = ChunkSection.Filled(0);
        // Assign a unique id to every cell (4096 distinct ids) forcing indirect -> direct promotion.
        for (int i = 0; i < ChunkSection.BlockCells; i++)
        {
            int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
            section.SetBlockStateId(x, y, z, i + 1);
        }

        for (int i = 0; i < ChunkSection.BlockCells; i++)
        {
            int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
            Assert.Equal(i + 1, section.GetBlockStateId(x, y, z));
        }
    }

    [Fact]
    public void FromIndirectBlocks_DecodesPackedIndices()
    {
        // Build a 4-bit indirect section by hand: palette [0, 5], every cell index 0 except cell 0 = index 1.
        int bits = 4;
        int entriesPerLong = 64 / bits;
        int longCount = (ChunkSection.BlockCells + entriesPerLong - 1) / entriesPerLong;
        var data = new long[longCount];
        data[0] = 0x1L; // cell 0 -> palette index 1

        var section = ChunkSection.FromIndirectBlocks([0, 5], bits, data);
        Assert.Equal(5, section.GetBlockStateId(0, 0, 0));
        Assert.Equal(0, section.GetBlockStateId(1, 0, 0));
    }

    [Fact]
    public void FromDirectBlocks_DecodesRawIds()
    {
        // 8-bit direct: cell 0 = 200, cell 1 = 13.
        int bits = 8;
        int entriesPerLong = 64 / bits;
        int longCount = (ChunkSection.BlockCells + entriesPerLong - 1) / entriesPerLong;
        var data = new long[longCount];
        data[0] = 200L | (13L << 8);

        var section = ChunkSection.FromDirectBlocks(bits, data);
        Assert.Equal(200, section.GetBlockStateId(0, 0, 0));
        Assert.Equal(13, section.GetBlockStateId(1, 0, 0));
    }

    [Fact]
    public void Biomes_SingleValueAndOverwrite_RoundTrip()
    {
        var section = ChunkSection.Filled(0, biomeId: 1);
        Assert.Equal(1, section.GetBiomeId(0, 0, 0));

        section.SetBiomeId(3, 2, 1, 2);
        Assert.Equal(2, section.GetBiomeId(3, 2, 1));
        Assert.Equal(1, section.GetBiomeId(0, 0, 0));
    }

    [Fact]
    public void Biomes_IndirectInstall_RoundTrips()
    {
        var section = ChunkSection.Filled(0);
        // 4-bit indirect biomes over 64 cells: palette [0,1], cell 0 -> index 1.
        int bits = 4;
        int longCount = (ChunkSection.BiomeCells + (64 / bits) - 1) / (64 / bits);
        var data = new long[longCount];
        data[0] = 1L;
        section.SetIndirectBiomes([0, 1], bits, data);

        Assert.Equal(1, section.GetBiomeId(0, 0, 0));
        Assert.Equal(0, section.GetBiomeId(1, 0, 0));
    }

    [Fact]
    public void Light_RoundTrips_AndDefaultsDark()
    {
        var section = ChunkSection.Filled(0);
        Assert.Equal(LightLevels.Dark, section.GetLight(0, 0, 0));

        var sky = new NibbleArray();
        var block = new NibbleArray();
        sky.Set(ChunkSection.BlockIndex(2, 3, 4), 15);
        block.Set(ChunkSection.BlockIndex(2, 3, 4), 7);
        section.SetSkyLight(sky);
        section.SetBlockLight(block);

        LightLevels light = section.GetLight(2, 3, 4);
        Assert.Equal(15, light.Sky);
        Assert.Equal(7, light.Block);
        Assert.Equal(new LightLevels(0, 0), section.GetLight(0, 0, 0));
    }

    [Fact]
    public void Capture_IsIndependentOfLaterWrites()
    {
        var section = ChunkSection.Filled(0);
        section.SetBlockStateId(5, 5, 5, 99);
        SectionSnapshot snap = section.Capture();

        section.SetBlockStateId(5, 5, 5, 123);

        Assert.Equal(99, snap.GetBlockStateId(5, 5, 5));
        Assert.Equal(123, section.GetBlockStateId(5, 5, 5));
    }

    [Fact]
    public void NibbleArray_Rejects_WrongLength()
    {
        Assert.Throws<ArgumentException>(() => new NibbleArray(new byte[10]));
    }

    // Biome palettes use the 1-3 bit linear ladder. Case 1 -> ONE_BIT, 2 -> TWO_BITS, 3 -> THREE_BITS,
    // else global). The indirect widths must remain below the 3-bit biome ceiling so a third distinct
    // biome stays in 2-bit indirect storage.

    [Fact]
    public void Biomes_GrowThroughSmallIndirectWidths_NotStraightToDirect()
    {
        var section = ChunkSection.Filled(0, biomeId: 0);

        section.SetBiomeId(0, 0, 0, 1); // 2 entries -> 1 bit
        Assert.False(section.BiomesAreDirect);
        Assert.Equal(1, section.BiomePaletteBits);

        section.SetBiomeId(1, 0, 0, 2); // 3 entries -> 2 bits
        Assert.False(section.BiomesAreDirect);
        Assert.Equal(2, section.BiomePaletteBits);

        section.SetBiomeId(2, 0, 0, 3); // 4 entries -> still 2 bits
        Assert.Equal(2, section.BiomePaletteBits);

        section.SetBiomeId(3, 0, 0, 4); // 5 entries -> 3 bits
        Assert.False(section.BiomesAreDirect);
        Assert.Equal(3, section.BiomePaletteBits);

        for (int i = 5; i <= 7; i++)
        {
            section.SetBiomeId(i - 5, 1, 0, i); // 8 distinct entries -> still 3 bits indirect
        }

        Assert.False(section.BiomesAreDirect);
        Assert.Equal(3, section.BiomePaletteBits);

        section.SetBiomeId(3, 1, 0, 8); // 9th distinct biome exceeds 3 bits -> direct
        Assert.True(section.BiomesAreDirect);

        // Round-trip everything after all the transitions.
        Assert.Equal(1, section.GetBiomeId(0, 0, 0));
        Assert.Equal(2, section.GetBiomeId(1, 0, 0));
        Assert.Equal(3, section.GetBiomeId(2, 0, 0));
        Assert.Equal(4, section.GetBiomeId(3, 0, 0));
        Assert.Equal(7, section.GetBiomeId(2, 1, 0));
        Assert.Equal(8, section.GetBiomeId(3, 1, 0));
        Assert.Equal(0, section.GetBiomeId(3, 3, 3));
    }

    // Mutation-promoted block sections use the global width.

    [Fact]
    public void BlockPromotion_WithKnownStateCount_UsesCeilLog2Width()
    {
        // 26.2-scale state count (~29k) -> ceillog2 = 15 bits, not a fixed 32.
        var section = ChunkSection.Filled(0, biomeId: 0, blockStateCount: 29000);
        for (int i = 0; i < 300; i++)
        {
            int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
            section.SetBlockStateId(x, y, z, 20000 + i);
        }

        Assert.True(section.BlocksAreDirect);
        Assert.Equal(15, section.BlockPaletteBits);
        Assert.Equal(20000, section.GetBlockStateId(0, 0, 0));
        Assert.Equal(20299, section.GetBlockStateId(11, 1, 2));
    }

    [Fact]
    public void BlockPromotion_WithoutStateCount_DerivesWidthAndReWidens()
    {
        // Unknown state count: promotion picks the width of the widest id present, then re-widens (rebuild-and-swap) when a wider id arrives instead of silently truncating.
        var section = ChunkSection.Filled(0);
        for (int i = 0; i < 300; i++)
        {
            int x = i & 15, z = (i >> 4) & 15, y = i >> 8;
            section.SetBlockStateId(x, y, z, i + 1);
        }

        Assert.True(section.BlocksAreDirect);
        int narrowBits = section.BlockPaletteBits;
        Assert.True(narrowBits <= 10, $"derived direct width should be near ceillog2(301), was {narrowBits}");

        section.SetBlockStateId(15, 15, 15, 1_000_000); // needs 20 bits -> re-widen
        Assert.Equal(1_000_000, section.GetBlockStateId(15, 15, 15));
        Assert.True(section.BlockPaletteBits >= 20);
        Assert.Equal(2, section.GetBlockStateId(1, 0, 0)); // old cells survive the re-widen (i=1 -> value 2)
        Assert.Equal(300, section.GetBlockStateId(11, 1, 2));
    }
}
