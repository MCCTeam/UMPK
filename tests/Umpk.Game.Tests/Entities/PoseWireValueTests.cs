using Umpk.Game.Entities;
using Xunit;

namespace Umpk.Game.Tests.Entities;

public sealed class PoseWireValueTests
{
    [Theory]
    [InlineData(EntityPose.Standing, -1)]
    [InlineData(EntityPose.Standing, int.MinValue)]
    [InlineData(EntityPose.Standing, int.MaxValue)]
    [InlineData(EntityPose.Dying, 6)]
    [InlineData(EntityPose.Unknown, int.MaxValue)]
    public void PackedPoseKeepsCanonicalMeaningAndEveryWireIdentifierBit(EntityPose pose, int id)
    {
        MetadataValue value = MetadataValue.PoseWithWireId(pose, id);

        Assert.Equal(pose, value.AsPose());
        Assert.Equal(id, value.AsPoseWireId());
        Assert.Equal(value, MetadataValue.PoseWithWireId(pose, id));
        Assert.NotEqual(value, MetadataValue.PoseWithWireId(pose, id ^ 0x100));
        Assert.Null(MetadataValue.Pose(pose).AsPoseWireId());
    }

    [Fact]
    public void PoseWireIdentifierRemainsKindChecked()
    {
        Assert.Throws<InvalidOperationException>(() => MetadataValue.VarInt(1).AsPoseWireId());
    }
}
