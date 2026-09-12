using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Round-trip tests for the serverbound player movement and interaction family.</summary>
public class EntityServerboundTests
{
    [Fact]
    public void MovePlayer_Legacy_UsesOnGroundByte()
    {
        var status = new ServerboundMovePlayerStatusPacket(true);
        Assert.Equal(status, CodecRoundTrip.Cycle(EntityServerboundCodecs.MovePlayerStatusV1_8, status));

        var pos = new ServerboundMovePlayerPosPacket(1.0, 2.0, 3.0, true, false);
        var d = CodecRoundTrip.Cycle(EntityServerboundCodecs.MovePlayerPosV1_8, pos);
        Assert.Equal(1.0, d.X);
        Assert.True(d.OnGround);
        Assert.False(d.HorizontalCollision); // legacy has no collision bit
    }

    [Fact]
    public void MovePlayer_Modern_PacksFlagsByte()
    {
        var pos = new ServerboundMovePlayerPosPacket(1.0, 2.0, 3.0, true, true);
        var d = CodecRoundTrip.Cycle(EntityServerboundCodecs.MovePlayerPosV1_14, pos);
        Assert.Equal(pos, d);

        var posRot = new ServerboundMovePlayerPosRotPacket(1, 2, 3, 45f, 90f, false, true);
        Assert.Equal(posRot, CodecRoundTrip.Cycle(EntityServerboundCodecs.MovePlayerPosRotV1_14, posRot));

        var rot = new ServerboundMovePlayerRotPacket(10f, -10f, true, false);
        Assert.Equal(rot, CodecRoundTrip.Cycle(EntityServerboundCodecs.MovePlayerRotV1_14, rot));

        var statusOnly = new ServerboundMovePlayerStatusOnlyPacket(true, true);
        Assert.Equal(statusOnly, CodecRoundTrip.Cycle(EntityServerboundCodecs.MovePlayerStatusOnly, statusOnly));
    }

    [Fact]
    public void MovePlayerStatusOnly_FlagBitsAreCorrect()
    {
        byte[] both = CodecRoundTrip.Encode(EntityServerboundCodecs.MovePlayerStatusOnly, new ServerboundMovePlayerStatusOnlyPacket(true, true));
        Assert.Equal([(byte)0x03], both);
        byte[] onGround = CodecRoundTrip.Encode(EntityServerboundCodecs.MovePlayerStatusOnly, new ServerboundMovePlayerStatusOnlyPacket(true, false));
        Assert.Equal([(byte)0x01], onGround);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    public void PlayerInput_AllBitsRoundTrip(int seed)
    {
        var rng = new Random(seed);
        var p = new ServerboundPlayerInputPacket(rng.Next(2) == 0, rng.Next(2) == 0, rng.Next(2) == 0, rng.Next(2) == 0, rng.Next(2) == 0, rng.Next(2) == 0, rng.Next(2) == 0);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityServerboundCodecs.PlayerInput, p));
    }

    [Fact]
    public void PlayerInput_BitLayout()
    {
        byte[] sprint = CodecRoundTrip.Encode(EntityServerboundCodecs.PlayerInput, new ServerboundPlayerInputPacket(false, false, false, false, false, false, true));
        Assert.Equal([(byte)0x40], sprint);
        byte[] fwdJump = CodecRoundTrip.Encode(EntityServerboundCodecs.PlayerInput, new ServerboundPlayerInputPacket(true, false, false, false, true, false, false));
        Assert.Equal([(byte)0x11], fwdJump);
    }

    [Fact]
    public void Vehicle_MoveAndPaddle_RoundTrip()
    {
        var move = new ServerboundMoveVehiclePacket(1, 2, 3, 45f, 90f, true);
        Assert.Equal(move, CodecRoundTrip.Cycle(EntityServerboundCodecs.MoveVehicle, move));

        var paddle = new ServerboundPaddleBoatPacket(true, false);
        Assert.Equal(paddle, CodecRoundTrip.Cycle(EntityServerboundCodecs.PaddleBoat, paddle));

        var steer = new ServerboundSteerVehiclePacket(-1.0f, 1.0f, 0x02);
        Assert.Equal(steer, CodecRoundTrip.Cycle(EntityServerboundCodecs.SteerVehicleV1_8, steer));
    }

    [Fact]
    public void PlayerAction_LegacyAndModern_RoundTrip()
    {
        var legacy = new ServerboundPlayerActionPacket(0, new BlockPos(1, 64, 2), 1, null);
        var dl = CodecRoundTrip.Cycle(EntityServerboundCodecs.PlayerActionV1_8, legacy);
        Assert.Equal(legacy.Position, dl.Position);
        Assert.Null(dl.Sequence);

        var modern = new ServerboundPlayerActionPacket(2, new BlockPos(-5, 10, 300), 5, 42);
        var dm = CodecRoundTrip.Cycle(EntityServerboundCodecs.PlayerActionV1_19, modern);
        Assert.Equal(modern.Position, dm.Position);
        Assert.Equal(42, dm.Sequence);
    }

    [Fact]
    public void PlayerCommand_RoundTrips()
    {
        var p = new ServerboundPlayerCommandPacket(7, 3, 0);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityServerboundCodecs.PlayerCommand, p));
    }

    [Fact]
    public void Interact_AllActionShapes_RoundTrip()
    {
        // Legacy (no hand, no secondary): attack has no extra fields.
        var legacyAttack = new ServerboundInteractPacket(5, 1, null, null, null);
        Assert.Equal(legacyAttack, CodecRoundTrip.Cycle(EntityServerboundCodecs.InteractV1_8, legacyAttack));

        var legacyInteractAt = new ServerboundInteractPacket(5, 2, null, new Vec3d(0.1, 0.2, 0.3), null);
        var dla = CodecRoundTrip.Cycle(EntityServerboundCodecs.InteractV1_8, legacyInteractAt);
        Assert.Equal(2, dla.Action);
        Assert.NotNull(dla.InteractAt);

        // Modern interact: hand + secondary.
        var modernInteract = new ServerboundInteractPacket(5, 0, 1, null, true);
        Assert.Equal(modernInteract, CodecRoundTrip.Cycle(EntityServerboundCodecs.InteractV1_16, modernInteract));

        // Modern attack: no hand, secondary present.
        var modernAttack = new ServerboundInteractPacket(5, 1, null, null, false);
        Assert.Equal(modernAttack, CodecRoundTrip.Cycle(EntityServerboundCodecs.InteractV1_16, modernAttack));

        // Modern interact-at: floats + hand + secondary.
        var modernAt = new ServerboundInteractPacket(5, 2, 0, new Vec3d(1, 2, 3), true);
        Assert.Equal(modernAt, CodecRoundTrip.Cycle(EntityServerboundCodecs.InteractV1_16, modernAt));
    }

    [Fact]
    public void Attack_2601_RoundTrips()
    {
        var p = new ServerboundAttackPacket(12345);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityServerboundCodecs.Attack, p));
    }

    [Fact]
    public void SetCarriedItem_And_Swing_RoundTrip()
    {
        var carried = new ServerboundSetCarriedItemPacket(4);
        Assert.Equal(carried, CodecRoundTrip.Cycle(EntityServerboundCodecs.SetCarriedItem, carried));

        var legacySwing = new ServerboundSwingPacket(0, false);
        Assert.Empty(CodecRoundTrip.Encode(EntityServerboundCodecs.SwingV1_8, legacySwing)); // 1.8 has no body
        Assert.Equal(legacySwing, CodecRoundTrip.Cycle(EntityServerboundCodecs.SwingV1_8, legacySwing));

        var modernSwing = new ServerboundSwingPacket(1, true);
        Assert.Equal(modernSwing, CodecRoundTrip.Cycle(EntityServerboundCodecs.SwingV1_9, modernSwing));
    }

    [Fact]
    public void AcceptTeleportation_RoundTrips()
    {
        var p = new ServerboundAcceptTeleportationPacket(987);
        Assert.Equal(p, CodecRoundTrip.Cycle(EntityServerboundCodecs.AcceptTeleportation, p));
    }
}
