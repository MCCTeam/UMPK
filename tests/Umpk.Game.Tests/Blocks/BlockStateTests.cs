using Umpk.Game.Blocks;
using Xunit;

namespace Umpk.Game.Tests.Blocks;

public class BlockStateTests
{
    private static readonly FakeBlockDataSource Source = new();

    [Fact]
    public void State_ResolvesOwningBlock()
    {
        var state = Source.GetState(3); // oak_stairs facing east
        Assert.Equal(Identifier.Minecraft("oak_stairs"), state.Block.Id);
        Assert.Equal(2, state.Block.NetworkId);
        Assert.Equal(3, state.StateId);
    }

    [Fact]
    public void Air_HasAirFlag()
    {
        var air = Source.GetState(0);
        Assert.True(air.IsAir);
        Assert.False(air.BlocksMotion);
        Assert.False(air.IsSolid);
    }

    [Fact]
    public void Stone_HasSolidAndBlocksMotion()
    {
        var stone = Source.GetState(1);
        Assert.True(stone.IsSolid);
        Assert.True(stone.BlocksMotion);
        Assert.False(stone.IsAir);
        Assert.Equal(0.6f, stone.Friction);
    }

    [Fact]
    public void Waterlogged_State_ReportsWaterlogged()
    {
        var west = Source.GetState(5); // stairs facing west, waterlogged
        Assert.True(west.IsWaterlogged);
        Assert.False(Source.GetState(2).IsWaterlogged);
    }

    [Fact]
    public void PropertyAccess_ReturnsValue()
    {
        var north = Source.GetState(2);
        Assert.True(north.TryGetProperty("facing", out var value));
        Assert.Equal("north", value);
        Assert.Equal(new[] { "facing" }, north.PropertyNames);
    }

    [Fact]
    public void PropertyAccess_UnknownProperty_ReturnsFalse()
    {
        var north = Source.GetState(2);
        Assert.False(north.TryGetProperty("half", out _));
    }

    [Fact]
    public void PropertyAccess_PropertylessBlock_ReturnsFalse()
    {
        var stone = Source.GetState(1);
        Assert.False(stone.TryGetProperty("facing", out _));
        Assert.Empty(stone.PropertyNames);
    }

    [Fact]
    public void DefaultState_ResolvesFromBlock()
    {
        var stairs = Source.Blocks[Identifier.Minecraft("oak_stairs")];
        var def = Source.GetDefaultState(stairs);
        Assert.Equal(3, def.StateId);

        Assert.True(Source.TryGetDefaultState(Identifier.Minecraft("stone"), out var stoneDefault));
        Assert.Equal(1, stoneDefault.StateId);
    }

    [Fact]
    public void TryGetDefaultState_UnknownBlock_ReturnsUnknownStateAndFalse()
    {
        Assert.False(Source.TryGetDefaultState(Identifier.Minecraft("nonexistent"), out var state));
        Assert.Equal(Source.UnknownStateId, state.StateId);
    }

    [Fact]
    public void Equality_SameSourceAndId_AreEqual()
    {
        var a = Source.GetState(3);
        var b = Source.GetState(3);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentSource_NotEqual()
    {
        var other = new FakeBlockDataSource();
        Assert.NotEqual(Source.GetState(3), other.GetState(3));
        Assert.True(Source.GetState(3) != other.GetState(3));
    }

    [Fact]
    public void Default_IsDefaultAndSourceThrows()
    {
        BlockState def = default;
        Assert.True(def.IsDefault);
        Assert.False(def.IsValid);
        Assert.False(def.TryGetProperty("facing", out _));
        Assert.Throws<InvalidOperationException>(() => def.Source);
        Assert.Equal("<default>", def.ToString());
    }

    [Fact]
    public void Constructor_NullSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BlockState(null!, 0));
    }

    [Fact]
    public void InvalidStateId_IsValidFalse()
    {
        var state = Source.GetState(999);
        Assert.False(state.IsValid);
    }

    [Fact]
    public void ModernState_HasNoLegacyIdentity()
    {
        var state = Source.GetState(3);
        Assert.Equal(-1, state.LegacyId);
        Assert.Equal(-1, state.LegacyMeta);
    }

    /// <summary>Whether the state is scaffolding, which affects both its context-dependent collision shape and the climb clamp's sneak exemption. It is keyed on the registry path and checked only after the climbable flag, so the string compare is unreachable for every state that is not climbable at all.</summary>
    [Theory]
    [InlineData(FakeBlockDataSource.ScaffoldingStateId, true, "the one block whose shape yields to a descending body")]
    [InlineData(FakeBlockDataSource.LadderStateId, false, "climbable, but its panel is real from every direction")]
    [InlineData(FakeBlockDataSource.VineStateId, false, "climbable, and has no collision shape at all")]
    [InlineData(1, false, "stone is not climbable, so the flag rejects it before the name is read")]
    [InlineData(0, false, "air")]
    [InlineData(3, false, "oak_stairs, the non-climbable state with properties")]
    public void IsScaffolding_IsTrueForScaffoldingAlone(int stateId, bool expected, string why)
    {
        Assert.Equal(expected, Source.GetState(stateId).IsScaffolding);
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>Powder snow collision is keyed on the registry path because the behavior is a per-block override, exactly as <see cref="BlockState.IsScaffolding"/> is.</summary>
    [Theory]
    [InlineData(FakeBlockDataSource.PowderSnowStateId, true, "the block a booted body walks on")]
    [InlineData(FakeBlockDataSource.SnowLayerStateId, false, "minecraft:snow is a different block with a similar name")]
    [InlineData(1, false, "stone")]
    [InlineData(0, false, "air, rejected by the flag before the name is read")]
    [InlineData(FakeBlockDataSource.ScaffoldingStateId, false, "the OTHER context-dependent shape")]
    [InlineData(FakeBlockDataSource.VineStateId, false, "shapeless, like powder snow, and not it")]
    public void IsPowderSnow_IsTrueForPowderSnowAlone(int stateId, bool expected, string why)
    {
        Assert.Equal(expected, Source.GetState(stateId).IsPowderSnow);
        Assert.False(string.IsNullOrEmpty(why));
    }

    /// <summary>The same guard <see cref="BlockState.IsScaffolding"/> carries and for the same reason: every consumer reads this off a world lookup that can return the default for an unloaded cell, and <see cref="BlockState.Flags"/> throws there.</summary>
    [Fact]
    public void IsPowderSnow_OnADefaultState_IsFalseRatherThanThrowing()
    {
        var state = default(BlockState);
        Assert.True(state.IsDefault);
        Assert.Throws<InvalidOperationException>(() => state.IsAir);
        Assert.False(state.IsPowderSnow);
    }

    /// <summary>A default-constructed state has no source, so <see cref="BlockState.Flags"/> throws. Every consumer of this property reads it off a world lookup that can legitimately return the default (an unloaded cell), so the property has to answer rather than throw - the same guard <c>ScaffoldingCollision.ShapesFor</c> and <c>PlayerPhysics.UpdateEnvironment</c> already carry.</summary>
    [Fact]
    public void IsScaffolding_OnADefaultState_IsFalseRatherThanThrowing()
    {
        var state = default(BlockState);
        Assert.True(state.IsDefault);
        Assert.Throws<InvalidOperationException>(() => state.IsClimbable);
        Assert.False(state.IsScaffolding);
    }
}
