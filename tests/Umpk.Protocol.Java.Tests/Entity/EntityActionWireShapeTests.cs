using System.Buffers;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;
using static Umpk.Protocol.Java.Tests.Support.LiteralFrame;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Pins literal frames and incompatible layouts for entity action packets.</summary>
public sealed class EntityActionWireShapeTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public void Animate_LiteralFrame(int protocol)
    {
        byte[] frame = Cat(VarInt(0x1F2E3D), [3]);
        var p = (ClientboundAnimatePacket)Clientbound(protocol, "animate").DecodeFrame(frame);
        Assert.Equal(0x1F2E3D, p.EntityId);
        Assert.Equal(3, p.Action);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void OpenSignEditor_And_SetCamera_And_SetEntityLink_LiteralFrames(int protocol)
    {
        var sign = (ClientboundOpenSignEditorPacket)Clientbound(protocol, "open_sign_editor").DecodeFrame(PackedBlockPosition(-1, 5, -1));
        Assert.Equal(new BlockPos(-1, 5, -1), sign.Pos);

        var camera = (ClientboundSetCameraPacket)Clientbound(protocol, "set_camera").DecodeFrame(VarInt(9001));
        Assert.Equal(9001, camera.CameraId);

        // 1.9 dropped 1.8's trailing leash bool: exactly two ints and nothing else.
        byte[] link = Cat(I32(11), I32(-1));
        var attached = (ClientboundSetEntityLinkPacket)Clientbound(protocol, "set_entity_link").DecodeFrame(link);
        Assert.Equal(11, attached.SourceId);
        Assert.Equal(-1, attached.DestId);
        Assert.Null(attached.LegacyLeash);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void SetPassengers_LiteralFrame(int protocol)
    {
        byte[] frame = Cat(VarInt(500), VarInt(3), VarInt(1), VarInt(2), VarInt(300));
        var p = (ClientboundSetPassengersPacket)Clientbound(protocol, "set_passengers").DecodeFrame(frame);
        Assert.Equal(500, p.VehicleId);
        Assert.Equal(new[] { 1, 2, 300 }, p.Passengers);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(210)]
    public void TakeItemEntity_WithoutCount_LiteralFrame(int protocol)
    {
        byte[] frame = Cat(VarInt(77), VarInt(88));
        var p = (ClientboundTakeItemEntityPacket)Clientbound(protocol, "take_item_entity").DecodeFrame(frame);
        Assert.Equal(77, p.ItemEntityId);
        Assert.Equal(88, p.CollectorEntityId);
        Assert.Null(p.Amount);
        Assert.Equal(frame, Clientbound(protocol, "take_item_entity").Encode(p));
    }

    [Theory]
    [InlineData(315)]
    [InlineData(404)]
    public void TakeItemEntity_WithCount_LiteralFrame(int protocol)
    {
        byte[] frame = Cat(VarInt(77), VarInt(88), VarInt(16));
        var p = (ClientboundTakeItemEntityPacket)Clientbound(protocol, "take_item_entity").DecodeFrame(frame);
        Assert.Equal(16, p.Amount);
        Assert.Equal(frame, Clientbound(protocol, "take_item_entity").Encode(p));
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void UseBed_LiteralFrame(int protocol)
    {
        byte[] frame = Cat(VarInt(31), PackedBlockPosition(8, 63, -8));
        var p = (ClientboundUseBedPacket)Clientbound(protocol, "use_bed").DecodeFrame(frame);
        Assert.Equal(31, p.PlayerId);
        Assert.Equal(new BlockPos(8, 63, -8), p.Position);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    public void AddPainting_NameMotiveFrame_HasUuidAndNameMotive(int protocol)
    {
        byte[] frame = Cat(VarInt(64), Uuid(SampleUuid), Str("Kebab"), PackedBlockPosition(3, 64, -7), [2]);
        var p = (ClientboundAddPaintingPacket)Clientbound(protocol, "add_painting").DecodeFrame(frame);
        Assert.Equal(64, p.EntityId);
        Assert.Equal(SampleUuid, p.Uuid);
        Assert.Equal("Kebab", p.Title);
        Assert.Null(p.MotiveId);
        Assert.Equal(new BlockPos(3, 64, -7), p.Position);
        Assert.Equal(2, p.Facing);
        Assert.Equal(frame, Clientbound(protocol, "add_painting").Encode(p));
    }

    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    public void AddPainting_NumericMotiveFrame_HasVarIntMotive(int protocol)
    {
        byte[] frame = Cat(VarInt(64), Uuid(SampleUuid), VarInt(17), PackedBlockPosition(3, 64, -7), [2]);
        var p = (ClientboundAddPaintingPacket)Clientbound(protocol, "add_painting").DecodeFrame(frame);
        Assert.Equal(17, p.MotiveId);
        Assert.Equal(new BlockPos(3, 64, -7), p.Position);
        Assert.Equal(frame, Clientbound(protocol, "add_painting").Encode(p));
    }

    [Fact]
    public void TakeItemEntity_CountRequiredFrame_RejectsMissingCount()
    {
        byte[] noCount = Cat(VarInt(77), VarInt(88));
        byte[] withCount = Cat(VarInt(77), VarInt(88), VarInt(16));

        Rejects(Clientbound(315, "take_item_entity"), noCount, "1.11 expects the pickup count");
        Rejects(Clientbound(210, "take_item_entity"), withCount, "1.10 has no pickup count to read");
    }

    [Fact]
    public void AddPainting_AlternateWireShapes_AreRejected()
    {
        byte[] v1_9 = Cat(VarInt(64), Uuid(SampleUuid), Str("Kebab"), PackedBlockPosition(3, 64, -7), [2]);
        byte[] v1_13 = Cat(VarInt(64), Uuid(SampleUuid), VarInt(17), PackedBlockPosition(3, 64, -7), [2]);

        Rejects(Clientbound(393, "add_painting"), v1_9, "1.13 reads the motive as a VarInt id");
        Rejects(Clientbound(340, "add_painting"), v1_13, "1.12.2 reads the motive as a name string");

        // 393-404 vs 477: both block-pos packings are bijections over the same eight bytes, so a mis-binding here re-encodes identically and can only be caught on the decoded position.
        var pre = (ClientboundAddPaintingPacket)Clientbound(404, "add_painting").DecodeFrame(v1_13);
        var post = (ClientboundAddPaintingPacket)Clientbound(477, "add_painting").DecodeFrame(v1_13);
        Assert.Equal(new BlockPos(3, 64, -7), pre.Position);
        Assert.NotEqual(new BlockPos(3, 64, -7), post.Position);
    }

    [Fact]
    public void SetEntityLink_RejectsFrameWithLeashFlag()
    {
        byte[] withLeash = Cat(I32(11), I32(-1), [1]);
        Rejects(Clientbound(107, "set_entity_link"), withLeash, "1.9 dropped the leash bool");
    }

    [Fact]
    public void PlayerAbilities_Serverbound_RejectsFlagsOnlyFrame()
    {
        byte[] flagsOnly = [0x0D];
        Rejects(Serverbound(107, "player_abilities"), flagsOnly, "1.9-1.13.2 still carry the two speed floats");
    }
}
