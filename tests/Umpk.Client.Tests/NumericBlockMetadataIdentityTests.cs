using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Block IDENTITY on the pre-flattening band: what block does a state id actually name.</summary>
/// <remarks>
/// The datasets carry one row per block ID. All 16 metadata values must resolve to that block instead of leaving nonzero metadata states unowned.
/// <para>These tests assert both <c>LegacyId</c>/<c>LegacyMeta</c> and the resolved block identity that a consumer reads.</para>
/// <para>The identity claimed here is the version's OWN registry name (<c>minecraft:wool</c>), not the 1.13 flattened per-meta name (<c>minecraft:red_wool</c>), because the latter is not an identifier these servers have. See <c>JavaGameData.BuildBlocks</c> for the reasoning.</para>
/// </remarks>
public sealed class NumericBlockMetadataIdentityTests
{
    /// <summary>Every pre-flattening protocol the library ships.</summary>
    public static TheoryData<int> LegacyProtocols =>
        [47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340];

    private static RegistryBlockDataSource Source(int protocol)
        => new(JavaGameData.Registries(protocol).Blocks, protocol < 393);

    private static Identifier Resolve(RegistryBlockDataSource source, int blockId, int meta)
        => new BlockState(source, (blockId << 4) | meta).Block.Id;

    /// <summary>Representative nonzero metadata states, including naturally oriented blocks. Values are <c>(block id, meta, state id, expected identifier)</c>; the state id is spelled out so each row is independently checkable.</summary>
    public static TheoryData<int, int, int, int, string> NonZeroMetaStates
    {
        get
        {
            var data = new TheoryData<int, int, int, int, string>();
            (int Block, int Meta, int State, string Name)[] rows =
            [
                (35, 14, 574, "wool"),        // red wool: "minecraft:air (state 574)"
                (35, 15, 575, "wool"),        // black wool, the top of the nibble
                (17, 2, 274, "log"),          // birch log: "minecraft:air (state 274)"
                (1, 5, 21, "stone"),          // andesite: "minecraft:air (state 21)"
                (54, 2, 866, "chest"),        // a server-placed chest always carries a facing
                (61, 2, 978, "furnace"),      // likewise
                (44, 8, 712, "stone_slab"),   // a TOP slab; the bottom one is meta 0
                (5, 3, 83, "planks"),         // jungle planks
                (159, 11, 2555, "stained_hardened_clay"),
            ];
            foreach ((int block, int meta, int state, string name) in rows)
                foreach (int protocol in new[] { 47, 107, 210, 316, 340 })
                    data.Add(protocol, block, meta, state, name);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NonZeroMetaStates))]
    public void NonZeroMetadata_Resolves_To_TheRightBlock(
        int protocol, int blockId, int meta, int stateId, string expected)
    {
        RegistryBlockDataSource source = Source(protocol);
        Assert.Equal((blockId << 4) | meta, stateId);

        var state = new BlockState(source, stateId);

        Assert.Equal(Identifier.Minecraft(expected), state.Block.Id);
        Assert.Equal(blockId, state.Block.NetworkId);
        Assert.Equal(blockId, state.LegacyId);
        Assert.Equal(meta, state.LegacyMeta);
        Assert.True(state.IsValid);

        // The meta-0 sibling has always resolved; the point is that the variant now agrees with it.
        Assert.Equal(state.Block.Id, new BlockState(source, blockId << 4).Block.Id);
    }

    /// <summary>Nonzero metadata states retain the owning block's flags, including solidity and motion blocking.</summary>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void NonZeroMetadata_Carries_TheBlocksOwnFlags(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);

        foreach (int meta in new[] { 0, 1, 7, 14, 15 })
        {
            var wool = new BlockState(source, (35 << 4) | meta);
            Assert.True(wool.IsSolid, $"wool meta {meta} is not solid on protocol {protocol}");
            Assert.True(wool.BlocksMotion, $"wool meta {meta} does not block motion on protocol {protocol}");
            Assert.False(wool.IsAir);

            var log = new BlockState(source, (17 << 4) | meta);
            Assert.True(log.BlocksMotion, $"log meta {meta} does not block motion on protocol {protocol}");
        }
    }

    /// <summary>Whole-nibble coverage: on every pre-flattening protocol, all sixteen metas of every block id in the dataset resolve to that same block.</summary>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void EveryBlockOwns_AllSixteenMetadataStates(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);
        Assert.True(source.IsLegacy);

        foreach (RegistryEntry<BlockDefinition> entry in source.Blocks)
        {
            for (int meta = 0; meta < 16; meta++)
            {
                int state = (entry.NetworkId << 4) | meta;
                Assert.True(
                    source.IsValidState(state),
                    $"protocol {protocol}: state {state} ({entry.Id} meta {meta}) has no owning block");
                Assert.Equal(entry.NetworkId, source.GetBlockNetworkId(state));
            }

            Assert.Equal(entry.NetworkId << 4, entry.Value.DefaultStateId);
        }
    }

    /// <summary>The widening claims the nibble, not the id space. A block id the version does not have must stay unresolved rather than becoming a plausible-looking block.</summary>
    [Theory]
    [InlineData(47, 198)]  // 1.8 registers 0-197 and stops
    [InlineData(107, 213)] // 1.9's table has a hole from 213 to 254
    [InlineData(340, 253)]
    public void AnAbsentBlockId_StaysUnresolved(int protocol, int absentBlockId)
    {
        RegistryBlockDataSource source = Source(protocol);
        Assert.False(source.Blocks.TryGet(absentBlockId, out _));

        for (int meta = 0; meta < 16; meta++)
            Assert.False(source.IsValidState((absentBlockId << 4) | meta));

    }

    /// <summary>The ten protocol-47 block ids that had NO registry entry at all, even at meta 0, because 1.8's dataset named blocks after the flattened identifier of their meta-0 variant and that folds still/flowing, lit/unlit and powered/unpowered pairs onto one name, which <c>JavaGameData.BuildBlocks</c> resolves by dropping the second. It is the same mechanism behind the three double slabs fixed earlier. Asserted across the band because 107-340 already had the right names and must keep them.</summary>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void TheFormerlyCollidingIds_EachResolveToTheirOwnBlock(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);
        (int Id, string Name)[] pairs =
        [
            (8, "flowing_water"), (9, "water"),
            (10, "flowing_lava"), (11, "lava"),
            (31, "tallgrass"), (32, "deadbush"),
            (61, "furnace"), (62, "lit_furnace"),
            (73, "redstone_ore"), (74, "lit_redstone_ore"),
            (75, "unlit_redstone_torch"), (76, "redstone_torch"),
            (93, "unpowered_repeater"), (94, "powered_repeater"),
            (123, "redstone_lamp"), (124, "lit_redstone_lamp"),
            (149, "unpowered_comparator"), (150, "powered_comparator"),
            (151, "daylight_detector"), (178, "daylight_detector_inverted"),
        ];

        foreach ((int id, string name) in pairs)
        {
            Assert.Equal(Identifier.Minecraft(name), Resolve(source, id, 0));
            Assert.Equal(Identifier.Minecraft(name), Resolve(source, id, 3));
        }
    }

    /// <summary>A pre-flattening registry must use the era's identifiers because those are the names the server accepts and reports. The flattened names must remain absent.</summary>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void TheBandUses_TheWireLayoutOwnIdentifiers(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);
        (int Id, string Legacy, string Flattened)[] renames =
        [
            (2, "grass", "grass_block"),
            (5, "planks", "oak_planks"),
            (17, "log", "oak_log"),
            (35, "wool", "white_wool"),
            (44, "stone_slab", "smooth_stone_slab"),
            (159, "stained_hardened_clay", "white_terracotta"),
            (165, "slime", "slime_block"),
            (175, "double_plant", "sunflower"),
        ];

        foreach ((int id, string legacy, string flattened) in renames)
        {
            Assert.Equal(Identifier.Minecraft(legacy), Resolve(source, id, 0));
            Assert.False(
                source.Blocks.TryGet(Identifier.Minecraft(flattened), out _),
                $"protocol {protocol} carries the flattened name {flattened}, which does not exist on this version");
        }
    }

    /// <summary><c>minecraft:slime</c> is the pre-flattening spelling of the slime block and has friction 0.8.</summary>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void SlimeFriction_IsKnown_UnderTheWireLayoutName(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);
        Assert.True(source.Blocks.TryGet(Identifier.Minecraft("slime"), out RegistryEntry<BlockDefinition> slime));
        Assert.Equal(0.8f, new BlockState(source, slime.Value.DefaultStateId).Friction, 3);
    }

    /// <summary>Before the flattening <c>minecraft:grass</c> is the grass BLOCK and <c>minecraft:snow</c> is the snow BLOCK; the replaceable plant and layer are <c>tallgrass</c> and <c>snow_layer</c>. The curated flag list used the flattened spellings unscoped, so the band reported that a solid grass block and a solid snow block could be built over, and that the layer and the grass could not. The flags must follow the meaning of each era-specific identifier.</summary>
    [Theory]
    [MemberData(nameof(LegacyProtocols))]
    public void ReplaceableByPlacement_UsesTheWireLayoutMeaning(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);

        Assert.True(new BlockState(source, 31 << 4).IsReplaceable, "tallgrass must be replaceable");
        Assert.True(new BlockState(source, 175 << 4).IsReplaceable, "double_plant must be replaceable");
        Assert.True(new BlockState(source, 78 << 4).IsReplaceable, "snow_layer must be replaceable");

        Assert.False(new BlockState(source, 2 << 4).IsReplaceable, "the grass BLOCK must not be replaceable");
        Assert.False(new BlockState(source, 80 << 4).IsReplaceable, "the snow BLOCK must not be replaceable");
    }

    /// <summary>The flattened bands must be untouched: their state ids are dense and a widening there would make neighbouring blocks overlap. Spot-checked at the first flattened protocol and at the head.</summary>
    [Theory]
    [InlineData(393)]
    [InlineData(770)]
    [InlineData(776)]
    public void FlattenedBands_KeepTheirOwnStateRanges(int protocol)
    {
        RegistryBlockDataSource source = Source(protocol);
        Assert.False(source.IsLegacy);

        foreach (RegistryEntry<BlockDefinition> entry in source.Blocks)
        {
            Assert.Equal(entry.NetworkId, source.GetBlockNetworkId(entry.Value.MinStateId));
            Assert.Equal(entry.NetworkId, source.GetBlockNetworkId(entry.Value.MaxStateId));
        }

        Assert.True(source.Blocks.TryGet(Identifier.Minecraft("stone"), out RegistryEntry<BlockDefinition> stone));
        Assert.Equal(1, stone.Value.StateCount);
    }
}
