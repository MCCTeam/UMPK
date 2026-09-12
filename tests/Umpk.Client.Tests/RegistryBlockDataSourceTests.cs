using Umpk;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The client's own <see cref="IBlockDataSource"/>, exercised through the public <see cref="BlockState"/> surface a plugin actually uses.</summary>
/// <remarks>The tests cover property lookup, legacy ID and metadata decoding, and per-state flags through the public <see cref="BlockState"/> surface.</remarks>
public sealed class RegistryBlockDataSourceTests
{
    private static RegistryBlockDataSource Source(int protocol)
        => new(JavaGameData.Registries(protocol).Blocks, protocol < 393);

    private static BlockState DefaultState(RegistryBlockDataSource source, string name)
    {
        Assert.True(
            source.Blocks.TryGet(Identifier.Minecraft(name), out RegistryEntry<BlockDefinition> entry),
            $"minecraft:{name} is not in the registry.");
        return new BlockState(source, entry.Value.DefaultStateId);
    }

    [Theory]
    [InlineData(477)] // 1.14, the oldest band with property names
    [InlineData(766)] // 1.20.6
    [InlineData(770)] // 1.21.5
    [InlineData(776)] // 26.2
    public void BlockState_TryGetProperty_Reads_Crop_Age(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);
        Assert.True(source.Blocks.TryGet(Identifier.Minecraft("wheat"), out RegistryEntry<BlockDefinition> wheat));

        var seedling = new BlockState(source, wheat.Value.MinStateId);
        var ripe = new BlockState(source, wheat.Value.MaxStateId);

        Assert.True(seedling.TryGetProperty("age", out string young));
        Assert.Equal("0", young);
        Assert.True(ripe.TryGetProperty("age", out string grown));
        Assert.Equal("7", grown);

        Assert.Equal(["age"], seedling.PropertyNames);
        Assert.False(seedling.TryGetProperty("facing", out _));
    }

    [Theory]
    [InlineData(477)]
    [InlineData(770)]
    [InlineData(776)]
    public void BlockState_Flags_And_Friction_Are_Per_Block(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);

        BlockState air = DefaultState(source, "air");
        Assert.True(air.IsAir);
        Assert.False(air.BlocksMotion);

        BlockState water = DefaultState(source, "water");
        Assert.True(water.IsFluid);
        Assert.False(water.BlocksMotion);
        Assert.False(water.IsSolid);

        BlockState stone = DefaultState(source, "stone");
        Assert.True(stone.IsSolid);
        Assert.True(stone.BlocksMotion);
        Assert.Equal(0.6f, stone.Friction, 3);

        Assert.Equal(0.98f, DefaultState(source, "ice").Friction, 3);
        Assert.Equal(0.8f, DefaultState(source, "slime_block").Friction, 3);
        Assert.Equal(0.4f, DefaultState(source, "soul_sand").SpeedFactor, 3);

        // A ladder is climbable and, in vanilla, has a thin collision box against its wall, so it does block motion without being a full solid cube. Nothing reported Climbable at all before.
        BlockState ladder = DefaultState(source, "ladder");
        Assert.True(ladder.IsClimbable);
        Assert.True(ladder.BlocksMotion);
        Assert.False(ladder.IsSolid);

        BlockState flower = DefaultState(source, "dandelion");
        Assert.False(flower.BlocksMotion);
        Assert.False(flower.IsSolid);
    }

    [Theory]
    [InlineData(770)]
    [InlineData(776)]
    public void Waterlogged_Is_Reported_Per_State(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);
        Assert.True(source.Blocks.TryGet(Identifier.Minecraft("oak_stairs"), out RegistryEntry<BlockDefinition> stairs));

        var dry = new BlockState(source, stairs.Value.DefaultStateId);
        var wet = new BlockState(source, stairs.Value.DefaultStateId - 1);

        Assert.False(dry.IsWaterlogged);
        Assert.True(wet.IsWaterlogged);
        Assert.True(dry.TryGetProperty("waterlogged", out string dryValue));
        Assert.Equal("false", dryValue);
        Assert.True(wet.TryGetProperty("waterlogged", out string wetValue));
        Assert.Equal("true", wetValue);
    }

    [Theory]
    [InlineData(47)]  // 1.8
    [InlineData(340)] // 1.12.2
    public void Legacy_Bands_Decode_Id_And_Meta(int protocol)
    {
        // LegacyId and LegacyMeta were hardcoded to -1 by the unconditional false in TryDecodeLegacy.
        RegistryBlockDataSource source = Source(protocol);
        Assert.True(source.IsLegacy);

        // Stone is block id 1; the state id is (id << 4) | meta.
        var stone = new BlockState(source, (1 << 4) | 0);
        Assert.Equal(1, stone.LegacyId);
        Assert.Equal(0, stone.LegacyMeta);

        var wool = new BlockState(source, (35 << 4) | 14);
        Assert.Equal(35, wool.LegacyId);
        Assert.Equal(14, wool.LegacyMeta);
        Assert.Equal((35 << 4) | 14, source.EncodeLegacy(35, 14));

        // Properties genuinely do not exist before the flattening; the answer must be a refusal.
        Assert.Empty(stone.PropertyNames);
        Assert.False(stone.TryGetProperty("age", out _));
    }

    [Theory]
    [InlineData(393)] // 1.13, the first flattened band
    [InlineData(770)]
    public void Flattened_Bands_Refuse_The_Legacy_Decode(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);
        Assert.False(source.IsLegacy);

        BlockState stone = DefaultState(source, "stone");
        Assert.Equal(-1, stone.LegacyId);
        Assert.Equal(-1, stone.LegacyMeta);
    }

    [Fact]
    public void Block_Ownership_Is_Resolved_For_Every_State()
    {
        // The state-to-block index replaced a linear scan of the whole registry per lookup. Every state in every block's range must map back to that block.
        RegistryBlockDataSource source = Source(770);
        foreach (RegistryEntry<BlockDefinition> entry in source.Blocks)
        {
            Assert.Equal(entry.NetworkId, source.GetBlockNetworkId(entry.Value.MinStateId));
            Assert.Equal(entry.NetworkId, source.GetBlockNetworkId(entry.Value.MaxStateId));
            Assert.Equal(entry.NetworkId, source.GetBlockNetworkId(entry.Value.DefaultStateId));
            Assert.True(source.IsValidState(entry.Value.DefaultStateId));
        }

        Assert.False(source.IsValidState(-1));
        Assert.False(source.IsValidState(source.StateCount + 1));
    }
}
