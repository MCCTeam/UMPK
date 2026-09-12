using Umpk.Game.Blocks;
using Xunit;

namespace Umpk.Game.Tests.Blocks;

public class NumericBlockStateTests
{
    private static readonly NumericBlockDataSource Source = new();

    [Theory]
    [InlineData(1, 0, 16)]     // stone, meta 0 -> (1<<4)|0
    [InlineData(1, 5, 21)]     // stone, meta 5
    [InlineData(35, 14, 574)]  // wool red, meta 14
    [InlineData(0, 0, 0)]      // air
    public void LegacyIdentity_RoundTrips(int blockId, int meta, int expectedStateId)
    {
        int stateId = Source.EncodeLegacy(blockId, meta);
        Assert.Equal(expectedStateId, stateId);

        Assert.True(Source.TryDecodeLegacy(stateId, out int decodedBlock, out int decodedMeta));
        Assert.Equal(blockId, decodedBlock);
        Assert.Equal(meta, decodedMeta);
    }

    [Fact]
    public void LegacyState_ExposesIdAndMeta()
    {
        var state = Source.GetLegacyState(35, 14); // red wool
        Assert.Equal(35, state.LegacyId);
        Assert.Equal(14, state.LegacyMeta);
        Assert.Equal((35 << 4) | 14, state.StateId);
    }

    [Fact]
    public void LegacyState_ResolvesOwningBlock()
    {
        var state = Source.GetLegacyState(1, 3); // stone
        Assert.Equal(Identifier.Minecraft("stone"), state.Block.Id);
        Assert.True(state.IsSolid);
    }

    [Fact]
    public void LegacyState_PropertyAccessDegradesGracefully()
    {
        var state = Source.GetLegacyState(35, 14);
        Assert.False(state.TryGetProperty("facing", out _));
        Assert.Empty(state.PropertyNames);
    }

    [Fact]
    public void LegacyAir_HasAirFlag()
    {
        var air = Source.GetLegacyState(0, 0);
        Assert.True(air.IsAir);
    }

    [Fact]
    public void Source_IsLegacy()
    {
        Assert.True(Source.IsLegacy);
    }

    [Fact]
    public void UnknownLegacyBlock_ResolvesToAirNetworkId()
    {
        // Block id 200 is not in the source; it maps to network id 0 (air).
        var state = Source.GetLegacyState(200, 0);
        Assert.False(state.IsValid);
        Assert.Equal(Identifier.Minecraft("air"), state.Block.Id);
    }
}
