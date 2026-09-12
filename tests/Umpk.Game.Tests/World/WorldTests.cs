using Umpk.Game.World;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Game.Tests.World;

public class WorldTests
{
    [Fact]
    public void GetBlock_OutsideLoadedChunks_IsAir()
    {
        var world = WorldTestData.NewWorld();
        Assert.Equal(0, world.GetBlockStateId(new BlockPos(100, 60, 100)));
        Assert.True(world.GetBlock(new BlockPos(100, 60, 100)).IsAir);
    }

    [Fact]
    public void SetAndGet_AcrossChunkBorders()
    {
        var world = WorldTestData.NewWorld();
        // Two positions straddling the x=0 chunk boundary.
        var a = new BlockPos(-1, 64, 5);
        var b = new BlockPos(0, 64, 5);
        world.SetBlockStateId(a, WorldTestData.StoneState);
        world.SetBlockStateId(b, WorldTestData.WaterState);

        Assert.Equal(WorldTestData.StoneState, world.GetBlockStateId(a));
        Assert.Equal(WorldTestData.WaterState, world.GetBlockStateId(b));
        Assert.NotNull(world.GetColumn(ChunkPos.Containing(a)));
        Assert.NotNull(world.GetColumn(ChunkPos.Containing(b)));
        Assert.NotEqual(ChunkPos.Containing(a), ChunkPos.Containing(b));
    }

    [Fact]
    public void SetAndGet_NegativeCoordinates_AndNegativeY()
    {
        var world = WorldTestData.NewWorld();
        var pos = new BlockPos(-37, -63, -128);
        world.SetBlockStateId(pos, 5);
        Assert.Equal(5, world.GetBlockStateId(pos));
    }

    [Fact]
    public void UnloadColumn_DropsState()
    {
        var world = WorldTestData.NewWorld();
        var pos = new BlockPos(3, 70, 3);
        world.SetBlockStateId(pos, WorldTestData.StoneState);
        Assert.Equal(WorldTestData.StoneState, world.GetBlockStateId(pos));

        Assert.True(world.UnloadColumn(ChunkPos.Containing(pos)));
        Assert.Equal(0, world.GetBlockStateId(pos));
    }

    [Fact]
    public void CopyRegion_IsIndependentSnapshot()
    {
        var world = WorldTestData.NewWorld();
        var origin = new BlockPos(10, 64, 10);
        world.SetBlockStateId(origin, WorldTestData.StoneState);

        RegionSnapshot snap = world.CopyRegion(new BlockPos(8, 62, 8), new BlockPos(12, 66, 12));
        Assert.Equal(WorldTestData.StoneState, snap.GetBlockStateId(origin));

        world.SetBlockStateId(origin, WorldTestData.WaterState);
        Assert.Equal(WorldTestData.StoneState, snap.GetBlockStateId(origin));
        Assert.Equal(WorldTestData.WaterState, world.GetBlockStateId(origin));

        Assert.Equal(0, snap.GetBlockStateId(new BlockPos(100, 100, 100)));
        Assert.False(snap.Contains(new BlockPos(100, 100, 100)));
    }

    [Fact]
    public void GetLight_ReadsInstalledSectionLight()
    {
        var world = WorldTestData.NewWorld();
        var pos = new BlockPos(1, 64, 1);
        ChunkColumn column = world.LoadColumn(ChunkPos.Containing(pos));
        int sectionIndex = column.Dimension.SectionIndexForY(pos.Y);
        ChunkSection section = column.GetOrCreateSection(sectionIndex);
        var sky = new NibbleArray();
        sky.Set(ChunkSection.BlockIndex(pos.X & 15, pos.Y & 15, pos.Z & 15), 12);
        section.SetSkyLight(sky);

        Assert.Equal(12, world.GetLight(pos).Sky);
        Assert.Equal(0, world.GetLight(pos).Block);
    }

    [Fact]
    public void GetBiome_ResolvesRegistryEntry()
    {
        var world = WorldTestData.NewWorld();
        var pos = new BlockPos(20, 64, 20);
        ChunkColumn column = world.LoadColumn(ChunkPos.Containing(pos));
        ChunkSection section = column.GetOrCreateSection(column.Dimension.SectionIndexForY(pos.Y));
        section.SetBiomeId((pos.X & 15) >> 2, (pos.Y & 15) >> 2, (pos.Z & 15) >> 2, 2);

        var biome = world.GetBiome(pos);
        Assert.False(biome.IsDefault);
        Assert.Equal(Identifier.Minecraft("ocean"), biome.Id);
    }

    [Fact]
    public void Time_And_BorderState_RoundTrip()
    {
        var world = WorldTestData.NewWorld();
        world.SetTime(123, 6000);
        Assert.Equal(123, world.WorldAge);
        Assert.Equal(6000, world.TimeOfDay);

        var border = new WorldBorderState(10, -5, 100, 50, 2000, 8, 20);
        world.SetBorder(border);
        Assert.Equal(100, world.Border.Size);
        Assert.Equal(50, world.Border.TargetSize);
        Assert.True(world.Border.IsLerping);
        Assert.Equal(10, world.Border.CenterX);
        Assert.Equal(-5, world.Border.CenterZ);
        Assert.False(WorldBorderState.Default.IsLerping);
    }

    /// <summary>Vanilla <c>world-border containment</c>: a half-open box, so a coordinate exactly ON the negative edge is inside and one exactly on the positive edge is not. Run against BOTH <see cref="WorldBorderContainmentEra"/> values: the border's own bounds are integers here (minX/minZ = -50), which is exactly the case where <see cref="WorldBorderContainmentEra.Legacy"/>'s <c>(x+1) &gt; minX</c> and <see cref="WorldBorderContainmentEra.Modern"/>'s <c>x &gt;= minX</c> agree - see <see cref="A_non_integer_border_accepts_its_own_min_edge_only_under_the_legacy_formula"/> for where they do not.</summary>
    [Theory]
    [InlineData(WorldBorderContainmentEra.Legacy)]
    [InlineData(WorldBorderContainmentEra.Modern)]
    public void IsWithinBounds_IsAHalfOpenBoxAroundTheCenter(WorldBorderContainmentEra era)
    {
        var border = new WorldBorderState(centerX: 0, centerZ: 0, size: 100, targetSize: 100, lerpTimeMillis: 0, warningBlocks: 5, warningTimeSeconds: 15);

        // [-50, 50) on both axes.
        Assert.True(border.IsWithinBounds(-50, -50, era));
        Assert.True(border.IsWithinBounds(49, 49, era));
        Assert.True(border.IsWithinBounds(0, 0, era));
        Assert.False(border.IsWithinBounds(50, 0, era));
        Assert.False(border.IsWithinBounds(0, 50, era));
        Assert.False(border.IsWithinBounds(-51, 0, era));
        Assert.False(border.IsWithinBounds(0, -51, era));
    }

    /// <summary>An off-center border shifts the whole box, not just its size. Integer bounds again, so both eras agree; see the class remarks on <see cref="IsWithinBounds_IsAHalfOpenBoxAroundTheCenter"/>.</summary>
    [Theory]
    [InlineData(WorldBorderContainmentEra.Legacy)]
    [InlineData(WorldBorderContainmentEra.Modern)]
    public void IsWithinBounds_FollowsAnOffCenterBorder(WorldBorderContainmentEra era)
    {
        var border = new WorldBorderState(centerX: 200, centerZ: -300, size: 20, targetSize: 20, lerpTimeMillis: 0, warningBlocks: 5, warningTimeSeconds: 15);

        // [190, 210) x [-310, -290).
        Assert.True(border.IsWithinBounds(190, -310, era));
        Assert.True(border.IsWithinBounds(209, -291, era));
        Assert.False(border.IsWithinBounds(210, -300, era));
        Assert.False(border.IsWithinBounds(189, -300, era));
    }

    /// <summary>The two <see cref="WorldBorderContainmentEra"/> formulas disagree on the min-X/ min-Z edge whenever the border's own bounds are NOT integers. Size 101 centered at the origin gives minX=minZ=-50.5. Legacy's <c>(x+1) &gt; minX</c> accepts x=-51 (<c>-50 &gt; -50.5</c>); Modern's <c>x &gt;= minX</c> does not (<c>-51 &gt;= -50.5</c> is false). The max edge is identical in both eras (<c>x &lt; maxX</c>), so it never diverges.</summary>
    [Fact]
    public void A_non_integer_border_accepts_its_own_min_edge_only_under_the_legacy_formula()
    {
        var border = new WorldBorderState(centerX: 0, centerZ: 0, size: 101, targetSize: 101, lerpTimeMillis: 0, warningBlocks: 5, warningTimeSeconds: 15);

        Assert.True(border.IsWithinBounds(-51, 0, WorldBorderContainmentEra.Legacy));
        Assert.False(border.IsWithinBounds(-51, 0, WorldBorderContainmentEra.Modern));

        // The max edge (50.5) never diverges: neither era accepts x=51, both accept x=50.
        Assert.False(border.IsWithinBounds(51, 0, WorldBorderContainmentEra.Legacy));
        Assert.False(border.IsWithinBounds(51, 0, WorldBorderContainmentEra.Modern));
        Assert.True(border.IsWithinBounds(50, 0, WorldBorderContainmentEra.Legacy));
        Assert.True(border.IsWithinBounds(50, 0, WorldBorderContainmentEra.Modern));
    }

    /// <summary><see cref="WorldBorderState.ContainmentEraForProtocol"/> pins the exact cut: 766 (1.20.6) is the last Legacy protocol, 767 (1.21) is the first Modern one.</summary>
    [Fact]
    public void ContainmentWireLayoutForProtocol_CutsExactlyAtProtocol767()
    {
        Assert.Equal(WorldBorderContainmentEra.Legacy, WorldBorderState.ContainmentEraForProtocol(766));
        Assert.Equal(WorldBorderContainmentEra.Modern, WorldBorderState.ContainmentEraForProtocol(767));

        // The resolver's own two boundary-adjacent measured protocols.
        Assert.Equal(WorldBorderContainmentEra.Legacy, WorldBorderState.ContainmentEraForProtocol(759)); // 1.19
        Assert.Equal(WorldBorderContainmentEra.Modern, WorldBorderState.ContainmentEraForProtocol(774)); // 1.21.11
    }
}
