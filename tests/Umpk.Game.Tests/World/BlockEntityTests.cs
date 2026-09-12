using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Xunit;

namespace Umpk.Game.Tests.World;

public class BlockEntityTests
{
    private static RegistryEntry<BlockEntityTypeDefinition> SignType()
    {
        var reg = Registry.FromEntries(
            new Identifier("minecraft", "block_entity_type"),
            [0],
            [Identifier.Minecraft("sign")],
            [new BlockEntityTypeDefinition()]);
        return reg[0];
    }

    [Fact]
    public void BlockEntity_PreservesNbtMemberOrder()
    {
        var nbt = new NbtCompound();
        nbt.PutString("Text1", "line one");
        nbt.PutInt("x", 5);
        nbt.PutString("Text2", "line two");
        nbt.PutByte("GlowingText", 1);

        var pos = new BlockPos(4, 70, -9);
        var be = new BlockEntityData(pos, SignType(), nbt);

        Assert.Equal(new[] { "Text1", "x", "Text2", "GlowingText" }, be.Nbt.Keys);
        Assert.Same(nbt, be.Nbt);
    }

    [Fact]
    public void World_AddGetRemove_BlockEntity()
    {
        var world = WorldTestData.NewWorld();
        var pos = new BlockPos(2, 65, 2);
        var nbt = new NbtCompound();
        nbt.PutString("id", "minecraft:sign");
        var be = new BlockEntityData(pos, SignType(), nbt);

        world.SetBlockEntity(be);
        Assert.Same(be, world.GetBlockEntity(pos));

        Assert.True(world.RemoveBlockEntity(pos));
        Assert.Null(world.GetBlockEntity(pos));
    }

    [Fact]
    public void Column_BlockEntities_EnumeratesStore()
    {
        var world = WorldTestData.NewWorld();
        var pos = new BlockPos(6, 66, 6);
        ChunkColumn column = world.LoadColumn(ChunkPos.Containing(pos));
        column.SetBlockEntity(new BlockEntityData(pos, SignType(), new NbtCompound()));

        Assert.Single(column.BlockEntities);
    }
}
