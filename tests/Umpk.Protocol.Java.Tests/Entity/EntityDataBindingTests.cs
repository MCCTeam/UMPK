using Umpk.Game.Entities;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

public class EntityDataBindingTests
{
    public static TheoryData<int> EntityDataProtocols => [735, 736, 751, 753, 754, 755, 756, 757, 758];

    [Theory]
    [MemberData(nameof(EntityDataProtocols))]
    public void SetEntityData_DecodesEveryFieldKind(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:set_entity_data");
        var list = new EntityMetadataList(
            [
                new EntityDataEntry(0, MetadataValue.Byte(0x21), 0),
                new EntityDataEntry(1, MetadataValue.VarInt(271), 1),
                new EntityDataEntry(2, MetadataValue.OptionalComponent(Umpk.Text.Component.Text("Sentinel")), 5),
                new EntityDataEntry(3, MetadataValue.Boolean(true), 7),
                new EntityDataEntry(6, MetadataValue.Pose(EntityPose.FallFlying), 18),
                new EntityDataEntry(8, MetadataValue.Float(17.5f), 2),
                new EntityDataEntry(13, MetadataValue.OptionalPosition(new Umpk.Geometry.BlockPos(11, 66, -21)), 10),
                new EntityDataEntry(16, MetadataValue.VillagerData(new VillagerData(2, 5, 3)), 16)
            ],
            []);
        byte[] wire = bound.Encode(new ClientboundSetEntityDataPacket(0x2BCD, list));
        var back = Assert.IsType<ClientboundSetEntityDataPacket>(bound.DecodeFrame(wire));
        Assert.Equal(0x2BCD, back.EntityId);
        Assert.Equal(8, back.Metadata.Entries.Count);
        Assert.Empty(back.Metadata.RawTail);
        Assert.Equal(0x21, back.Metadata.Entries[0].Value.AsByte());
        Assert.Equal(271, back.Metadata.Entries[1].Value.AsVarInt());
        Assert.Equal("Sentinel", back.Metadata.Entries[2].Value.AsOptionalComponent()!.ToPlainText());
        Assert.True(back.Metadata.Entries[3].Value.AsBoolean());
        Assert.Equal(EntityPose.FallFlying, back.Metadata.Entries[4].Value.AsPose());
        Assert.Equal(17.5f, back.Metadata.Entries[5].Value.AsFloat());
        Assert.Equal(new Umpk.Geometry.BlockPos(11, 66, -21), back.Metadata.Entries[6].Value.AsOptionalPosition());
        Assert.Equal(new VillagerData(2, 5, 3), back.Metadata.Entries[7].Value.AsVillagerData());
    }

    [Fact]
    public void SetEntityData_RejectsNeighbourSerializerTables()
    {
        byte[] villager = [0x01, 16, 16, 0x02, 0x05, 0x03, 0xFF];
        var current = Assert.IsType<ClientboundSetEntityDataPacket>(
            BoundCodec.At(754, PacketFlow.Clientbound, "minecraft:set_entity_data").DecodeFrame(villager));
        Assert.Single(current.Metadata.Entries);
        Assert.Equal(new VillagerData(2, 5, 3), current.Metadata.Entries[0].Value.AsVillagerData());
        Assert.Empty(current.Metadata.RawTail);
        var older = Assert.IsType<ClientboundSetEntityDataPacket>(
            BoundCodec.At(404, PacketFlow.Clientbound, "minecraft:set_entity_data").DecodeFrame(villager));
        Assert.Empty(older.Metadata.Entries);
        Assert.NotEmpty(older.Metadata.RawTail);
        byte[] globalPos = [0x01, 21, 21, 0x00, 0xFF];
        var newer = Assert.IsType<ClientboundSetEntityDataPacket>(
            BoundCodec.At(759, PacketFlow.Clientbound, "minecraft:set_entity_data").DecodeFrame(globalPos));
        Assert.Single(newer.Metadata.Entries);
        Assert.Null(newer.Metadata.Entries[0].Value.AsOptionalGlobalPosition());
        var prior = Assert.IsType<ClientboundSetEntityDataPacket>(
            BoundCodec.At(758, PacketFlow.Clientbound, "minecraft:set_entity_data").DecodeFrame(globalPos));
        Assert.Empty(prior.Metadata.Entries);
        Assert.NotEmpty(prior.Metadata.RawTail);
    }
}
