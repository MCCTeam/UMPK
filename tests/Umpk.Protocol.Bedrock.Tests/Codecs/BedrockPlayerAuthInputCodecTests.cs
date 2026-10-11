using System.Collections.Immutable;
using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockPlayerAuthInputCodecTests
{
    private static BedrockPlayerAuthInputPacket Idle(ulong tick, IReadOnlyList<int>? inputs = null) =>
        new(0f, 0f, new BedrockVector3(8.5f, 91.62001f, 8.5f), new BedrockVector2(0f, 0f), 0f, inputs, 1, 0, 2, new BedrockVector2(0f, 0f), tick, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));

    [Fact]
    public void MovementOnlyAbsentInputsRoundTrip()
    {
        BedrockPlayerAuthInputPacket packet = Idle(7UL, null);
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(packet);
        BedrockPlayerAuthInputPacket decoded = BedrockPlayerAuthInputCodecs.Decode(encoded);
        Assert.Equal(packet, decoded);
        Assert.Equal(encoded, BedrockPlayerAuthInputCodecs.Encode(decoded));
    }

    [Fact]
    public void MovementOnlyEmptyListRoundTrip()
    {
        BedrockPlayerAuthInputPacket packet = Idle(7UL, []);
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(packet);
        BedrockPlayerAuthInputPacket decoded = BedrockPlayerAuthInputCodecs.Decode(encoded);
        Assert.Equal(packet, decoded);
        Assert.NotNull(decoded.Inputs);
        Assert.Empty(decoded.Inputs!.Value);
        Assert.Equal(encoded, BedrockPlayerAuthInputCodecs.Encode(decoded));
    }

    [Fact]
    public void MovementOnlyForwardAndJumpInputsRoundTrip()
    {
        BedrockPlayerAuthInputPacket forward = Idle(8UL, [10]);
        Assert.Equal(forward, BedrockPlayerAuthInputCodecs.Decode(BedrockPlayerAuthInputCodecs.Encode(forward)));
        BedrockPlayerAuthInputPacket jump = Idle(9UL, [3, 61]);
        Assert.Equal(jump, BedrockPlayerAuthInputCodecs.Decode(BedrockPlayerAuthInputCodecs.Encode(jump)));
        Assert.Equal(BedrockPlayerAuthInputCodecs.Encode(forward), BedrockPlayerAuthInputCodecs.Encode(BedrockPlayerAuthInputCodecs.Decode(BedrockPlayerAuthInputCodecs.Encode(forward))));
    }

    [Fact]
    public void NonFinitePitchIsRejected()
    {
        var bad = new BedrockPlayerAuthInputPacket(float.NaN, 0f, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), 0f, null, 1, 0, 2, new BedrockVector2(0f, 0f), 1UL, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Encode(bad));
    }

    [Fact]
    public void UnknownInputOrdinalIsRejected()
    {
        var packet = new BedrockPlayerAuthInputPacket(0f, 0f, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), 0f, [66], 1, 0, 2, new BedrockVector2(0f, 0f), 1UL, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Encode(packet));
    }

    [Fact]
    public void DuplicateInputOrdinalIsRejected()
    {
        var packet = new BedrockPlayerAuthInputPacket(0f, 0f, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), 0f, [10, 10], 1, 0, 2, new BedrockVector2(0f, 0f), 1UL, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Encode(packet));
    }

    [Fact]
    public void DiscriminatorRangesAreEnforced()
    {
        BedrockPlayerAuthInputPacket Base() => Idle(1UL, null);
        var badInputMode = new BedrockPlayerAuthInputPacket(0f, 0f, Base().Position, Base().MoveVector, 0f, null, 5, 0, 2, Base().InteractRotation, 1UL, Base().Delta, Base().AnalogueMoveVector, Base().CameraOrientation, Base().RawMoveVector);
        var badPlayMode = new BedrockPlayerAuthInputPacket(0f, 0f, Base().Position, Base().MoveVector, 0f, null, 1, 10, 2, Base().InteractRotation, 1UL, Base().Delta, Base().AnalogueMoveVector, Base().CameraOrientation, Base().RawMoveVector);
        var badInteraction = new BedrockPlayerAuthInputPacket(0f, 0f, Base().Position, Base().MoveVector, 0f, null, 1, 0, 3, Base().InteractRotation, 1UL, Base().Delta, Base().AnalogueMoveVector, Base().CameraOrientation, Base().RawMoveVector);
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Encode(badInputMode));
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Encode(badPlayMode));
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Encode(badInteraction));
    }

    [Fact]
    public void StrictBooleanIsEnforced()
    {
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(Idle(1UL, null));
        encoded[32] = 2;
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Decode(encoded));
    }

    [Fact]
    public void NonCanonicalVarIntIsRejected()
    {
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(Idle(1UL, [10]));
        Assert.True(encoded.Length > 40);
        byte[] mutated = (byte[])encoded.Clone();
        int inputModeOffset = FindInputModeOffset(encoded);
        mutated[inputModeOffset] = 0x81;
        Array.Copy(new byte[] { 0x00 }, 0, mutated, inputModeOffset + 1, 1);
        byte[] extended = new byte[mutated.Length + 1];
        Array.Copy(mutated, 0, extended, 0, inputModeOffset);
        extended[inputModeOffset] = 0x81;
        extended[inputModeOffset + 1] = 0x00;
        Array.Copy(mutated, inputModeOffset + 1, extended, inputModeOffset + 2, mutated.Length - inputModeOffset - 1);
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Decode(extended));
    }

    [Fact]
    public void CollectionLimitIsEnforced()
    {
        var limits = new BedrockCodecLimits(MaxCollectionElements: 1);
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Decode(BedrockPlayerAuthInputCodecs.Encode(new BedrockPlayerAuthInputPacket(0f, 0f, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), 0f, [10, 11], 1, 0, 2, new BedrockVector2(0f, 0f), 1UL, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f))), limits));
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(Idle(1UL, [10]));
        int countOffset = FindInputCountOffset(encoded);
        byte[] inflated = new byte[encoded.Length + 1];
        Array.Copy(encoded, 0, inflated, 0, countOffset);
        inflated[countOffset] = 0xC8;
        inflated[countOffset + 1] = 0x01;
        Array.Copy(encoded, countOffset + 1, inflated, countOffset + 2, encoded.Length - countOffset - 1);
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Decode(inflated));
    }

    [Fact]
    public void TrailingBytesAreRejected()
    {
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(Idle(1UL, null));
        byte[] extended = [.. encoded, 0x00];
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Decode(extended));
    }

    [Fact]
    public void TruncatedPayloadIsRejected()
    {
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(Idle(1UL, null));
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Decode(encoded[..^1]));
    }

    [Fact]
    public void OptionalTailsAreRejected()
    {
        BedrockPlayerAuthInputPacket packet = Idle(1UL, [10]);
        var present = new BedrockPlayerAuthInputPacket(
            packet.Pitch, packet.Yaw, packet.Position, packet.MoveVector, packet.HeadYaw, packet.Inputs,
            packet.InputMode, packet.PlayMode, packet.InteractionModel, packet.InteractRotation, packet.Tick,
            packet.Delta, packet.AnalogueMoveVector, packet.CameraOrientation, packet.RawMoveVector,
            null, true, true, true, true, true);
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(present);
        int optionsOffset = FindFirstTailOptionalOffset(encoded);
        foreach (int offset in new[] { optionsOffset, optionsOffset + 2, optionsOffset + 4, optionsOffset + 6, optionsOffset + 8 })
        {
            byte[] mutated = (byte[])encoded.Clone();
            mutated[offset] = 1;
            Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Decode(mutated));
        }
    }

    [Fact]
    public void PresentButNullSectionsRoundTrip()
    {
        BedrockPlayerAuthInputPacket packet = Idle(11UL, [10]);
        var present = new BedrockPlayerAuthInputPacket(
            packet.Pitch, packet.Yaw, packet.Position, packet.MoveVector, packet.HeadYaw, packet.Inputs,
            packet.InputMode, packet.PlayMode, packet.InteractionModel, packet.InteractRotation, packet.Tick,
            packet.Delta, packet.AnalogueMoveVector, packet.CameraOrientation, packet.RawMoveVector,
            null, true, true, true, true, true);
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(present);
        BedrockPlayerAuthInputPacket decoded = BedrockPlayerAuthInputCodecs.Decode(encoded);
        Assert.Equal(present, decoded);
        Assert.Null(decoded.Transaction);
        Assert.True(decoded.TransactionPresent);
        Assert.True(decoded.ItemStackRequestPresent);
        Assert.True(decoded.BlockActionsPresent);
        Assert.True(decoded.VehicleRotationPresent);
        Assert.True(decoded.PredictedVehiclePresent);
        Assert.Equal(encoded, BedrockPlayerAuthInputCodecs.Encode(decoded));
        byte[] sections = encoded[FindFirstTailPresenceOffset(encoded)..(FindFirstTailPresenceOffset(encoded) + 10)];
        Assert.Equal(new byte[] { 1, 0, 1, 0, 1, 0, 1, 0, 1, 0 }, sections);
    }

    [Fact]
    public void TransactionTailRoundTripsWithAssertedValues()
    {
        byte[] payload = Convert.FromHexString("00000000000000000000000000000000000000000000000000000000000000000001000400000000000000000100000000000000000000000001010000010100000100000002060000000000000000000000000000000000000000000000000000000000000000000100010001000100010000000000000000000000000000000000000000000000000000000000");
        BedrockPlayerAuthInputPacket decoded = BedrockPlayerAuthInputCodecs.Decode(payload);
        Assert.NotNull(decoded.Transaction);
        Assert.Equal(0, decoded.Transaction!.Legacy.RequestId);
        Assert.Empty(decoded.Transaction.Legacy.Entries);
        Assert.Empty(decoded.Transaction.Actions);
        Assert.Equal(0, decoded.Transaction.Data.ActionType);
        Assert.Equal(1, decoded.Transaction.Data.TriggerType);
        Assert.Equal(0, decoded.Transaction.Data.BlockX);
        Assert.Equal(0, decoded.Transaction.Data.BlockY);
        Assert.Equal(0, decoded.Transaction.Data.BlockZ);
        Assert.Equal(2, decoded.Transaction.Data.Face);
        Assert.Equal(3, decoded.Transaction.Data.HotbarSlot);
        Assert.Equal(0, decoded.Transaction.Data.HeldItem.NetworkId);
        Assert.Equal(0u, decoded.Transaction.Data.BlockRuntimeId);
        Assert.Equal(1, decoded.Transaction.Data.ClientPrediction);
        Assert.Equal(0, decoded.Transaction.Data.ClientCooldownState);
        Assert.Equal(payload, BedrockPlayerAuthInputCodecs.Encode(decoded));
    }

    [Fact]
    public void TransactionTailWithLegacyEntryAndActionRoundTrips()
    {
        byte[] payload = Convert.FromHexString("00000000000000000000000000000000000000000000000000000000000000000001000400000000000000000900000000000000000000000001010a01010c02030401010100000000000700000000000000000100030000000100020201800103010000000000000000000000803f0000004000004040000000000000000000000000050001010001000100010000000000000000000000000000000000000000000000000000000000");
        BedrockPlayerAuthInputPacket decoded = BedrockPlayerAuthInputCodecs.Decode(payload);
        Assert.NotNull(decoded.Transaction);
        Assert.Equal(5, decoded.Transaction!.Legacy.RequestId);
        BedrockTransactionLegacyEntry entry = Assert.Single(decoded.Transaction.Legacy.Entries);
        Assert.Equal(12, entry.ContainerId);
        Assert.Equal(new byte[] { 3, 4 }, entry.ChangedSlots.ToArray());
        BedrockTransactionAction action = Assert.Single(decoded.Transaction.Actions);
        Assert.Equal(0u, action.SourceType);
        Assert.Equal(7u, action.Slot);
        Assert.Equal(0, action.OldItem.NetworkId);
        Assert.Equal(1, action.NewItem.NetworkId);
        Assert.Equal(3, action.NewItem.Count);
        Assert.Equal(1, decoded.Transaction.Data.ActionType);
        Assert.Equal(2, decoded.Transaction.Data.TriggerType);
        Assert.Equal(-1, decoded.Transaction.Data.BlockX);
        Assert.Equal(64, decoded.Transaction.Data.BlockY);
        Assert.Equal(-2, decoded.Transaction.Data.BlockZ);
        Assert.Equal(1f, decoded.Transaction.Data.PlayerPos.X);
        Assert.Equal(2f, decoded.Transaction.Data.PlayerPos.Y);
        Assert.Equal(3f, decoded.Transaction.Data.PlayerPos.Z);
        Assert.Equal(5u, decoded.Transaction.Data.BlockRuntimeId);
        Assert.Equal(0, decoded.Transaction.Data.ClientPrediction);
        Assert.Equal(1, decoded.Transaction.Data.ClientCooldownState);
        Assert.Equal(payload, BedrockPlayerAuthInputCodecs.Encode(decoded));
    }

    [Fact]
    public void TruncatedTransactionTailIsRejected()
    {
        byte[] payload = Convert.FromHexString("00000000000000000000000000000000000000000000000000000000000000000001000400000000000000000100000000000000000000000001010000010100000100000002060000000000000000000000000000000000000000000000000000000000000000000100010001000100010000000000000000000000000000000000000000000000000000000000");
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Decode(payload[..^10]));
    }

    [Fact]
    public void MovePredictionRoundTripWithAndWithoutAngularVelocity()
    {
        var without = new BedrockCorrectPlayerMovePredictionPacket(0, new BedrockVector3(1f, 2f, 3f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(10f, 20f), null, true, 12UL);
        Assert.Equal(without, BedrockPlayerAuthInputCodecs.DecodePrediction(BedrockPlayerAuthInputCodecs.Encode(without)));
        var with = new BedrockCorrectPlayerMovePredictionPacket(1, new BedrockVector3(1f, 2f, 3f), new BedrockVector3(0f, 1f, 0f), new BedrockVector2(10f, 20f), 0.5f, false, 13UL);
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(with);
        Assert.Equal(with, BedrockPlayerAuthInputCodecs.DecodePrediction(encoded));
        Assert.Equal(encoded, BedrockPlayerAuthInputCodecs.Encode(BedrockPlayerAuthInputCodecs.DecodePrediction(encoded)));
    }

    [Fact]
    public void MovePredictionRejectsUnknownTypeAndNonFiniteAndTrailing()
    {
        var badType = new BedrockCorrectPlayerMovePredictionPacket(2, new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), null, true, 1UL);
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Encode(badType));
        var badFloat = new BedrockCorrectPlayerMovePredictionPacket(0, new BedrockVector3(float.NaN, 0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), null, true, 1UL);
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.Encode(badFloat));
        byte[] encoded = BedrockPlayerAuthInputCodecs.Encode(new BedrockCorrectPlayerMovePredictionPacket(0, new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), null, true, 1UL));
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.DecodePrediction([.. encoded, 0x00]));
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.DecodePrediction(encoded[..^1]));
        byte[] malformed = (byte[])encoded.Clone();
        malformed[0] = 7;
        Assert.Throws<BedrockCodecException>(() => BedrockPlayerAuthInputCodecs.DecodePrediction(malformed));
    }

    private static int FindInputCountOffset(byte[] encoded)
    {
        var reader = new BedrockReader(encoded);
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadBoolean();
        return reader.Consumed;
    }

    private static int FindInputModeOffset(byte[] encoded)
    {
        var reader = new BedrockReader(encoded);
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadBoolean();
        return reader.Consumed;
    }

    private static int FindFirstTailOptionalOffset(byte[] encoded)
    {
        return FindFirstTailPresenceOffset(encoded) + 1;
    }

    private static int FindFirstTailPresenceOffset(byte[] encoded)
    {
        var reader = new BedrockReader(encoded);
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        if (reader.ReadBoolean())
        {
            uint count = reader.ReadUInt32VarInt();
            for (uint index = 0; index < count; index++)
                reader.ReadZigZag32();
        }

        reader.ReadUInt32VarInt();
        reader.ReadUInt32VarInt();
        reader.ReadZigZag32();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadUInt64VarInt();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        reader.ReadSingleLittleEndian();
        return reader.Consumed;
    }
}
