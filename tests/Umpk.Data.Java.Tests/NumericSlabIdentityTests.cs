using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>On protocol 47 (MC 1.8) a double slab and its half sibling must be distinguishable by name.</summary>
/// <remarks>
/// <para>Pre-flattening block identity must use the protocol-47 registry name, because later flattened names are not unique across lit/unlit, still/flowing, or double/half pairs. All 198 block ids have distinct registry entries.</para>
/// <para>Double and half slabs also differ geometrically: the double is a full cube and the half is <c>[0,0,0,1,0.5,1]</c>. Their legacy names therefore must resolve to separate block ids.</para>
/// </remarks>
public sealed class NumericSlabIdentityTests
{
    private const int Protocol = 47;

    private static Registry<BlockDefinition> Blocks => JavaGameData.Registries(Protocol).Blocks;

    private static ReadOnlySpan<Aabb> ShapeOf(int blockId) =>
        JavaGameData.BlockShapes(Protocol).GetCollisionShapes(blockId << 4);

    /// <summary>Every (double, half) slab pair on 1.8, by block id and registration name.</summary>
    public static TheoryData<int, string, int, string> SlabPairs => new()
    {
        { 43, "double_stone_slab", 44, "stone_slab" },
        { 125, "double_wooden_slab", 126, "wooden_slab" },
        { 181, "double_stone_slab2", 182, "stone_slab2" },
    };

    [Theory]
    [MemberData(nameof(SlabPairs))]
    public void The_double_and_the_half_have_distinct_names(int doubleId, string doubleName, int halfId, string halfName)
    {
        Assert.NotEqual(doubleName, halfName);

        Assert.True(Blocks.TryGetNetworkId(Identifier.Minecraft(doubleName), out int resolvedDouble), doubleName);
        Assert.True(Blocks.TryGetNetworkId(Identifier.Minecraft(halfName), out int resolvedHalf), halfName);

        // Each name resolves its own block id.
        Assert.Equal(doubleId, resolvedDouble);
        Assert.Equal(halfId, resolvedHalf);
    }

    [Theory]
    [MemberData(nameof(SlabPairs))]
    public void The_double_and_the_half_have_distinct_collision(int doubleId, string doubleName, int halfId, string halfName)
    {
        _ = doubleName;
        _ = halfName;

        ReadOnlySpan<Aabb> full = ShapeOf(doubleId);
        ReadOnlySpan<Aabb> half = ShapeOf(halfId);

        Assert.Equal(1, full.Length);
        Assert.Equal(1.0, full[0].MaxY, 6);

        Assert.Equal(1, half.Length);
        Assert.Equal(0.5, half[0].MaxY, 6);

        Assert.NotEqual(full[0].MaxY, half[0].MaxY);
    }

    /// <summary>Resolving each pair by name gives the geometry associated with that identifier.</summary>
    [Theory]
    [MemberData(nameof(SlabPairs))]
    public void A_name_lookup_gets_the_geometry_that_name_belongs_to(int doubleId, string doubleName, int halfId, string halfName)
    {
        _ = doubleId;
        _ = halfId;

        Assert.Equal(1.0, ShapeByName(doubleName).MaxY, 6);
        Assert.Equal(0.5, ShapeByName(halfName).MaxY, 6);
    }

    /// <summary>Ten legacy pairs map to one later block that differs only by a property. For example, legacy 8:0 and 9:0 both map to <c>water[level=0]</c>, 73:0 and 74:0 to <c>redstone_ore</c> with <c>lit</c> false and true, 151:0 and 178:0 to <c>daylight_detector</c> with <c>inverted</c> false and true, so no flattened name separates them. The legacy registry names keep both ids addressable.</summary>
    [Fact]
    public void The_ten_formerly_colliding_1_8_pairs_are_each_their_own_block()
    {
        (int Id, string Name)[] pairs =
        [
            (8, "flowing_water"), (9, "water"),
            (10, "flowing_lava"), (11, "lava"),
            (31, "tallgrass"), (32, "deadbush"),      // tallgrass meta 0 is the dead shrub
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
            Assert.True(Blocks.TryGet(id, out RegistryEntry<BlockDefinition> entry), $"block id {id}");
            Assert.Equal(Identifier.Minecraft(name), entry.Id);
            Assert.True(Blocks.TryGetNetworkId(Identifier.Minecraft(name), out int resolved), name);
            Assert.Equal(id, resolved);
        }

        // Geometry is keyed by state id and remains equal within each pair.
        foreach ((int a, int b) in new[] { (8, 9), (10, 11), (61, 62), (73, 74), (75, 76), (93, 94), (123, 124), (149, 150), (151, 178) })
            Assert.Equal<IEnumerable<Aabb>>(ShapeOf(a).ToArray(), ShapeOf(b).ToArray());

    }

    /// <summary>The 1.8 registry has no duplicate identifier left at all, so nothing can be silently dropped.</summary>
    [Fact]
    public void Every_1_8_block_id_owns_a_registry_entry()
    {
        Assert.Equal(198, Blocks.Count);
        for (int blockId = 0; blockId <= 197; blockId++)
            Assert.True(Blocks.TryGet(blockId, out _), $"block id {blockId} is missing from the 1.8 registry");

    }

    /// <summary>All six slab ids own a block-registry entry.</summary>
    /// <remarks>A duplicate identifier is dropped by <c>BuildBlocks</c>. Distinct legacy names therefore keep the double and half variants addressable independently.</remarks>
    [Fact]
    public void All_six_1_8_slab_ids_are_in_the_block_registry()
    {
        foreach (int blockId in new[] { 43, 44, 125, 126, 181, 182 })
        {
            Assert.True(Blocks.TryGet(blockId, out RegistryEntry<BlockDefinition> entry), $"block id {blockId}");
            Assert.Equal(blockId << 4, entry.Value.MinStateId);
        }
    }

    private static Aabb ShapeByName(string name)
    {
        Assert.True(Blocks.TryGetNetworkId(Identifier.Minecraft(name), out int blockId), name);
        ReadOnlySpan<Aabb> boxes = ShapeOf(blockId);
        Assert.Equal(1, boxes.Length);
        return boxes[0];
    }
}
