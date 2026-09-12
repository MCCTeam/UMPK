using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>Protocol 401 (MC 1.13.1) ships 1.13.1's own block and item palettes, not 1.13's.</summary>
/// <remarks>
/// <para>Protocol 401 has 598 blocks and 790 items, while protocol 393 has 593 and 785. Protocol 401 adds five <c>dead_*_coral</c> blocks and items, gives <c>waterlogged</c> to the five live corals and the conduit, and gives <c>unstable</c> to TNT. Block-state ids are a Cartesian product assigned in registry order, so those additions shifted the state range of 460 of the 593 blocks, from <c>minecraft:tnt</c> onward, and the item ids of 347 of the 785 items, from id 443 onward.</para>
/// <para>The assertions pin concrete state ids, registry boundaries, and collision geometry. They also verify that protocols 393 and 404 retain their distinct palettes.</para>
/// </remarks>
public sealed class Palette1131Tests
{
    private static Registry<BlockDefinition> Blocks(int protocol) => JavaGameData.Registries(protocol).Blocks;

    private static BlockDefinition Block(int protocol, string name)
    {
        Assert.True(
            Blocks(protocol).TryGetValue(Identifier.Minecraft(name), out BlockDefinition? definition),
            $"protocol {protocol}: minecraft:{name} is not in the block registry.");
        return definition;
    }

    /// <summary>The block that owns a state id, or null when no block does.</summary>
    private static string? BlockAtState(int protocol, int stateId)
    {
        foreach (RegistryEntry<BlockDefinition> entry in Blocks(protocol))
            if (entry.Value.OwnsState(stateId))
                return entry.Id.ToString();

        return null;
    }

    private static string? ItemAtId(int protocol, int itemId) =>
        JavaGameData.Registries(protocol).Items.TryGetKey(itemId, out Identifier id) ? id.ToString() : null;

    [Fact]
    public void The_1_13_1_Block_Palette_Has_598_Blocks_And_8599_States()
    {
        Assert.Equal(598, Blocks(401).Count);

        // The last block in registry order owns the last state id, and the space is gapless from 0.
        Assert.Equal(8598, Block(401, "structure_block").MaxStateId);
        Assert.Equal("minecraft:air", BlockAtState(401, 0));
        Assert.Null(BlockAtState(401, 8599));

        // 1.13 has 593 blocks and stops at 8581, five blocks and seventeen states below 1.13.1.
        Assert.Equal(593, Blocks(393).Count);
        Assert.Equal(8581, Block(393, "structure_block").MaxStateId);
    }

    [Fact]
    public void The_Five_Blocks_1_13_1_Added_Exist_And_Are_Waterloggable()
    {
        // Registry ids 561-565, immediately before the five live corals they were inserted beside.
        (string Name, int MinState, int BlockId)[] added =
        [
            ("dead_tube_coral", 8460, 561),
            ("dead_brain_coral", 8462, 562),
            ("dead_bubble_coral", 8464, 563),
            ("dead_fire_coral", 8466, 564),
            ("dead_horn_coral", 8468, 565),
        ];

        foreach ((string name, int minState, int blockId) in added)
        {
            Assert.False(
                Blocks(393).ContainsKey(Identifier.Minecraft(name)),
                $"1.13 must not have minecraft:{name}; it arrived in 1.13.1.");

            Assert.True(Blocks(401).TryGet(Identifier.Minecraft(name), out RegistryEntry<BlockDefinition> entry));
            Assert.Equal(blockId, entry.NetworkId);

            BlockDefinition definition = entry.Value;
            Assert.Equal(minState, definition.MinStateId);
            Assert.Equal(minState + 1, definition.MaxStateId);
            Assert.Equal("waterlogged", Assert.Single(definition.Properties).Name);
        }
    }

    /// <summary>The two property additions that do the shifting, and the exact state id where it starts.</summary>
    [Fact]
    public void Tnt_And_The_Corals_Gained_Their_Properties_And_The_Shift_Starts_At_Tnt()
    {
        // tnt is registry id 133 and the first block whose width changed: one state became two.
        BlockDefinition tnt = Block(401, "tnt");
        Assert.Equal(1126, tnt.MinStateId);
        Assert.Equal(1127, tnt.MaxStateId);
        Assert.Equal(1127, tnt.DefaultStateId);
        Assert.Equal("unstable", Assert.Single(tnt.Properties).Name);

        // Everything from the next block on is displaced by one. bookshelf is registry id 134.
        Assert.Equal(1128, Block(401, "bookshelf").MinStateId);
        Assert.Equal(1127, Block(393, "bookshelf").MinStateId);

        // 1126 is tnt on BOTH bands, which is why "the block resolved" was never the question. 1127 is the first state id the two bands disagree about, and it is tnt's own default placement state: protocol 401 assigns state 1127 to TNT while protocol 393 assigns it to bookshelf.
        Assert.Equal("minecraft:tnt", BlockAtState(401, 1126));
        Assert.Equal("minecraft:tnt", BlockAtState(393, 1126));
        Assert.Equal("minecraft:tnt", BlockAtState(401, 1127));
        Assert.Equal("minecraft:bookshelf", BlockAtState(393, 1127));

        // Below tnt (registry ids 0-132) nothing moved at all, which is the other half of the claim.
        foreach (string name in new[]
        {
            "stone", "grass_block", "dirt", "cobblestone", "bedrock", "sand", "gold_ore", "sponge",
            "glass",
        })
        {
            Assert.Equal(Block(393, name).MinStateId, Block(401, name).MinStateId);
            Assert.Equal(Block(393, name).MaxStateId, Block(401, name).MaxStateId);
        }
    }

    /// <summary>Blocks far above TNT in registry order, paired with protocol 393's meaning for the same ids.</summary>
    [Theory]
    // block, correct 1.13.1 default state, what 1.13's palette named that same state id
    [InlineData("obsidian", 1130, "minecraft:torch")]
    [InlineData("diamond_block", 3050, "minecraft:crafting_table")]
    [InlineData("glowstone", 3496, "minecraft:nether_portal")]
    [InlineData("nether_bricks", 4496, "minecraft:nether_brick_fence")]
    [InlineData("enchanting_table", 4613, "minecraft:brewing_stand")]
    [InlineData("emerald_block", 4884, "minecraft:spruce_stairs")]
    [InlineData("beacon", 5137, "minecraft:cobblestone_wall")]
    [InlineData("redstone_block", 5684, "minecraft:nether_quartz_ore")]
    [InlineData("sea_pickle", 8580, "minecraft:structure_block")]
    public void Blocks_Above_Tnt_Resolve_The_State_Ids_The_1_13_1_Report_Gives_Them(
        string name, int defaultState, string whatTheOldPaletteCalledIt)
    {
        Assert.Equal(defaultState, Block(401, name).DefaultStateId);
        Assert.Equal($"minecraft:{name}", BlockAtState(401, defaultState));
        Assert.Equal(whatTheOldPaletteCalledIt, BlockAtState(393, defaultState));
    }

    /// <summary>The end of protocol 401's palette extends beyond protocol 393's highest state.</summary>
    [Fact]
    public void Blue_Ice_Sits_Above_The_Old_Palettes_Highest_State()
    {
        BlockDefinition blueIce = Block(401, "blue_ice");
        Assert.Equal(8588, blueIce.DefaultStateId);
        Assert.Equal("minecraft:blue_ice", BlockAtState(401, 8588));

        // 1.13 stops at 8581, so 8588 resolved NO block there. A client reading it fell back to air: a solid, walkable, 0.989-friction block read as empty space.
        Assert.Null(BlockAtState(393, 8588));
        Assert.Equal(0.989f, blueIce.Friction, 3);
        Assert.True((blueIce.FlagsForState(8588) & BlockFlags.Solid) != 0);
    }

    /// <summary>The item path. The five dead corals are items too, and they shift the item registry the same way.</summary>
    [Fact]
    public void The_1_13_1_Item_Registry_Has_790_Items_And_Shifts_From_443()
    {
        Assert.Equal(790, JavaGameData.Registries(401).Items.Count);
        Assert.Equal(785, JavaGameData.Registries(393).Items.Count);

        // 442 is the last id the two bands agree on; 443 is where the five insertions begin.
        Assert.Equal("minecraft:horn_coral", ItemAtId(401, 442));
        Assert.Equal("minecraft:horn_coral", ItemAtId(393, 442));
        Assert.Equal("minecraft:dead_brain_coral", ItemAtId(401, 443));
        Assert.Equal("minecraft:dead_tube_coral", ItemAtId(401, 447));

        // and everything after is displaced by exactly five, all the way to the end of the registry.
        Assert.Equal("minecraft:tube_coral_fan", ItemAtId(401, 448));
        Assert.Equal("minecraft:tube_coral_fan", ItemAtId(393, 443));
        Assert.Equal("minecraft:heart_of_the_sea", ItemAtId(401, 789));
        Assert.Equal("minecraft:heart_of_the_sea", ItemAtId(393, 784));
        Assert.Null(ItemAtId(393, 789));
    }

    /// <summary>The shift lands in the collision table too, so physics reads the right geometry.</summary>
    [Fact]
    public void The_Shifted_State_Ids_Carry_The_Right_Collision_Geometry()
    {
        IBlockShapeSource shapes = JavaGameData.BlockShapes(401);

        // State 5137 is a full-cube beacon in protocol 401 and a non-full cobblestone wall in 393.
        ReadOnlySpan<Aabb> beacon = shapes.GetCollisionShapes(5137);
        Assert.Equal(1, beacon.Length);
        Assert.Equal(1.0, beacon[0].MaxY, 6);
        Assert.Equal(1.0, beacon[0].MaxX, 6);

        Aabb[] whatTheOldPaletteGave = JavaGameData.BlockShapes(393).GetCollisionShapes(5137).ToArray();
        Assert.NotEqual<IEnumerable<Aabb>>(whatTheOldPaletteGave, beacon.ToArray());

        // The two coral states share the live coral's empty box: waterlogging changes no geometry.
        Assert.True(shapes.GetCollisionShapes(Block(401, "tube_coral").MinStateId).IsEmpty);
        Assert.True(shapes.GetCollisionShapes(Block(401, "tube_coral").MaxStateId).IsEmpty);
        Assert.True(shapes.GetCollisionShapes(Block(401, "dead_tube_coral").MinStateId).IsEmpty);
    }

    /// <summary>Protocols 393 and 404 retain their expected independent palette boundaries.</summary>
    [Fact]
    public void The_1_13_And_1_13_2_Palettes_Are_Untouched()
    {
        Assert.Equal(593, Blocks(393).Count);
        Assert.Equal(598, Blocks(404).Count);
        Assert.Equal(785, JavaGameData.Registries(393).Items.Count);
        Assert.Equal(790, JavaGameData.Registries(404).Items.Count);

        // 393 keeps 1.13's ranges for the blocks 401 moved.
        Assert.Equal(1126, Block(393, "tnt").MinStateId);
        Assert.Equal(1126, Block(393, "tnt").MaxStateId);
        Assert.Equal(1127, Block(393, "bookshelf").MinStateId);
        Assert.Equal(8581, Block(393, "structure_block").MaxStateId);
        Assert.False(Blocks(393).ContainsKey(Identifier.Minecraft("dead_tube_coral")));

        // 404 keeps 1.13.2's, which happen to be the ones 401 now shares.
        Assert.Equal(1127, Block(404, "tnt").MaxStateId);
        Assert.Equal(1128, Block(404, "bookshelf").MinStateId);
        Assert.Equal(8598, Block(404, "structure_block").MaxStateId);
    }

    /// <summary>Protocols 401 and 404 have identical block and item palettes.</summary>
    /// <remarks>The only relevant wire difference between these protocols is the item-stack format. Their block and item identifiers, ids, state ranges, defaults, and property names remain equal.</remarks>
    [Fact]
    public void The_1_13_1_And_1_13_2_Palettes_Are_The_Same_Palette()
    {
        Assert.Equal(Blocks(404).Count, Blocks(401).Count);
        foreach (RegistryEntry<BlockDefinition> entry in Blocks(404))
        {
            Assert.True(Blocks(401).TryGet(entry.Id, out RegistryEntry<BlockDefinition> mine), entry.Id.ToString());
            Assert.Equal(entry.NetworkId, mine.NetworkId);
            Assert.Equal(entry.Value.MinStateId, mine.Value.MinStateId);
            Assert.Equal(entry.Value.MaxStateId, mine.Value.MaxStateId);
            Assert.Equal(entry.Value.DefaultStateId, mine.Value.DefaultStateId);
            Assert.Equal(
                entry.Value.Properties.Select(p => p.Name),
                mine.Value.Properties.Select(p => p.Name));
        }

        Assert.Equal(JavaGameData.Registries(404).Items.Count, JavaGameData.Registries(401).Items.Count);
        for (int id = 0; id < JavaGameData.Registries(404).Items.Count; id++)
            Assert.Equal(ItemAtId(404, id), ItemAtId(401, id));

    }
}
