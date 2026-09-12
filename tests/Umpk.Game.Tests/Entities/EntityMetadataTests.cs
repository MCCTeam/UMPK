using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;
using Xunit;

namespace Umpk.Game.Tests.Entities;

public sealed class EntityMetadataTests
{
    private static EntityMetadata NewStore() => new(EntityTestFixtures.Zombie);

    [Fact]
    public void Tier1_SetGet_Across_The_Value_Union()
    {
        var m = NewStore();
        m.Set(0, MetadataValue.Byte(5));
        m.Set(1, MetadataValue.VarInt(42));
        m.Set(2, MetadataValue.VarLong(9_000_000_000L));
        m.Set(3, MetadataValue.Float(1.5f));
        m.Set(4, MetadataValue.Boolean(true));
        m.Set(5, MetadataValue.String("hi"));
        m.Set(6, MetadataValue.Component(Component.Text("name")));
        m.Set(7, MetadataValue.Direction(Direction.East));
        m.Set(8, MetadataValue.BlockState(1234));
        m.Set(9, MetadataValue.Position(new BlockPos(1, 2, 3)));
        m.Set(10, MetadataValue.Rotations(new Rotations(1, 2, 3)));
        m.Set(11, MetadataValue.Pose(EntityPose.Sleeping));
        m.Set(12, MetadataValue.VillagerData(new VillagerData(1, 2, 3)));
        m.Set(13, MetadataValue.Nbt(new NbtString("x")));
        m.Set(14, MetadataValue.Quaternion(new Quaternion(0, 0, 0, 1)));
        m.Set(15, MetadataValue.Vector3(new Vec3d(4, 5, 6)));
        m.Set(16, MetadataValue.GlobalPosition(new GlobalPosition(Identifier.Minecraft("overworld"), new BlockPos(7, 8, 9))));

        Assert.Equal((sbyte)5, Read(m, 0).AsByte());
        Assert.Equal(42, Read(m, 1).AsVarInt());
        Assert.Equal(9_000_000_000L, Read(m, 2).AsVarLong());
        Assert.Equal(1.5f, Read(m, 3).AsFloat());
        Assert.True(Read(m, 4).AsBoolean());
        Assert.Equal("hi", Read(m, 5).AsString());
        Assert.Equal("name", Read(m, 6).AsComponent().ToPlainText());
        Assert.Equal(Direction.East, Read(m, 7).AsDirection());
        Assert.Equal(1234, Read(m, 8).AsBlockState());
        Assert.Equal(new BlockPos(1, 2, 3), Read(m, 9).AsPosition());
        Assert.Equal(new Rotations(1, 2, 3), Read(m, 10).AsRotations());
        Assert.Equal(EntityPose.Sleeping, Read(m, 11).AsPose());
        Assert.Equal(new VillagerData(1, 2, 3), Read(m, 12).AsVillagerData());
        Assert.Equal(NbtTagType.String, Read(m, 13).AsNbt().Type);
        Assert.Equal(new Quaternion(0, 0, 0, 1), Read(m, 14).AsQuaternion());
        Assert.Equal(new Vec3d(4, 5, 6), Read(m, 15).AsVector3());
        Assert.Equal(new BlockPos(7, 8, 9), Read(m, 16).AsGlobalPosition().Position);
    }

    [Fact]
    public void Tier1_Optionals_Present_And_Absent()
    {
        var m = NewStore();
        m.Set(0, MetadataValue.OptionalComponent(null));
        m.Set(1, MetadataValue.OptionalComponent(Component.Text("named")));
        m.Set(2, MetadataValue.OptionalPosition(null));
        m.Set(3, MetadataValue.OptionalPosition(new BlockPos(5, 6, 7)));
        m.Set(4, MetadataValue.OptionalUuid(null));
        var guid = Guid.NewGuid();
        m.Set(5, MetadataValue.OptionalUuid(guid));
        m.Set(6, MetadataValue.OptionalBlockState(null));
        m.Set(7, MetadataValue.OptionalBlockState(99));
        m.Set(8, MetadataValue.OptionalVarInt(null));
        m.Set(9, MetadataValue.OptionalVarInt(-3));

        Assert.Null(Read(m, 0).AsOptionalComponent());
        Assert.False(Read(m, 0).HasValue);
        Assert.Equal("named", Read(m, 1).AsOptionalComponent()!.ToPlainText());
        Assert.True(Read(m, 1).HasValue);
        Assert.Null(Read(m, 2).AsOptionalPosition());
        Assert.Equal(new BlockPos(5, 6, 7), Read(m, 3).AsOptionalPosition());
        Assert.Null(Read(m, 4).AsOptionalUuid());
        Assert.Equal(guid, Read(m, 5).AsOptionalUuid());
        Assert.Null(Read(m, 6).AsOptionalBlockState());
        Assert.Equal(99, Read(m, 7).AsOptionalBlockState());
        Assert.Null(Read(m, 8).AsOptionalVarInt());
        Assert.Equal(-3, Read(m, 9).AsOptionalVarInt());
    }

    [Fact]
    public void Tier1_Slot_Placeholder_Null_And_NonNull()
    {
        var m = NewStore();
        m.Set(0, MetadataValue.Slot(null));
        m.Set(1, MetadataValue.Slot(new FakeSlot()));

        Assert.Null(Read(m, 0).AsSlot());
        Assert.NotNull(Read(m, 1).AsSlot());
    }

    [Fact]
    public void Tier1_Overwrite_Replaces()
    {
        var m = NewStore();
        m.Set(3, MetadataValue.VarInt(1));
        m.Set(3, MetadataValue.VarInt(2));

        Assert.Equal(1, m.Count);
        Assert.Equal(2, Read(m, 3).AsVarInt());
    }

    [Fact]
    public void Tier1_Absent_Index_Returns_False()
    {
        var m = NewStore();
        Assert.False(m.TryGet(99, out _));
        Assert.False(m.Contains(99));
    }

    [Fact]
    public void Tier1_Remove_And_Clear()
    {
        var m = NewStore();
        m.Set(0, MetadataValue.Byte(1));
        m.Set(1, MetadataValue.Byte(2));

        Assert.True(m.Remove(0));
        Assert.False(m.Remove(0));
        Assert.Equal(1, m.Count);

        m.Clear();
        Assert.Equal(0, m.Count);
    }

    [Fact]
    public void Wrong_Kind_Accessor_Throws()
    {
        var m = NewStore();
        m.Set(0, MetadataValue.VarInt(1));
        MetadataValue v = Read(m, 0);
        Assert.Throws<InvalidOperationException>(() => v.AsFloat());
    }

    [Fact]
    public void Tier2_Resolves_Through_Key_Source()
    {
        var keySource = new FakeMetadataKeySource()
            .Map(EntityTestFixtures.Zombie, EntityMetadataKeys.Health, 9)
            .Map(EntityTestFixtures.Zombie, EntityMetadataKeys.Pose, 6);
        var m = new EntityMetadata(EntityTestFixtures.Zombie, keySource);
        m.Set(9, MetadataValue.Float(17.5f));
        m.Set(6, MetadataValue.Pose(EntityPose.Swimming));

        Assert.True(m.TryGet(EntityMetadataKeys.Health, out float health));
        Assert.Equal(17.5f, health);
        Assert.True(m.TryGet(EntityMetadataKeys.Pose, out EntityPose pose));
        Assert.Equal(EntityPose.Swimming, pose);
    }

    [Fact]
    public void Tier2_Miss_With_No_Source_Returns_False_Not_Throw()
    {
        var m = NewStore();
        m.Set(9, MetadataValue.Float(20f));

        Assert.False(m.HasKeySource);
        Assert.False(m.TryGet(EntityMetadataKeys.Health, out float health));
        Assert.Equal(default, health);
    }

    [Fact]
    public void Tier2_Unmapped_Key_Returns_False()
    {
        var keySource = new FakeMetadataKeySource(); // maps nothing
        var m = new EntityMetadata(EntityTestFixtures.Zombie, keySource);
        m.Set(9, MetadataValue.Float(20f));

        Assert.False(m.TryGet(EntityMetadataKeys.Health, out float health));
        Assert.Equal(default, health);
    }

    [Fact]
    public void Tier2_Resolved_Index_Absent_Returns_False()
    {
        var keySource = new FakeMetadataKeySource().Map(EntityTestFixtures.Zombie, EntityMetadataKeys.Health, 9);
        var m = new EntityMetadata(EntityTestFixtures.Zombie, keySource);
        // index 9 never set
        Assert.False(m.TryGet(EntityMetadataKeys.Health, out float health));
        Assert.Equal(default, health);
    }

    [Fact]
    public void Tier2_Kind_Mismatch_Is_Graceful_Miss()
    {
        var keySource = new FakeMetadataKeySource().Map(EntityTestFixtures.Zombie, EntityMetadataKeys.Health, 9);
        var m = new EntityMetadata(EntityTestFixtures.Zombie, keySource);
        // Health key projects AsFloat, but store a VarInt at the resolved index.
        m.Set(9, MetadataValue.VarInt(3));

        Assert.False(m.TryGet(EntityMetadataKeys.Health, out float health));
        Assert.Equal(default, health);
    }

    private static MetadataValue Read(EntityMetadata m, int index)
    {
        Assert.True(m.TryGet(index, out MetadataValue value));
        return value;
    }

    private sealed class FakeSlot : IMetadataSlot
    {
        public bool IsEmpty => false;
    }
}
