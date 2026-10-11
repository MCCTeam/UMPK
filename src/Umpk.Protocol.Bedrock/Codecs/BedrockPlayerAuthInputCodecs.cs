using System.Buffers;
using System.Collections.Immutable;

namespace Umpk.Protocol.Bedrock;

internal readonly record struct BedrockVector2(float X, float Z);

internal sealed class BedrockPlayerAuthInputTransaction : IEquatable<BedrockPlayerAuthInputTransaction>
{
    internal BedrockPlayerAuthInputTransaction(BedrockTransactionLegacy legacy, IReadOnlyList<BedrockTransactionAction> actions, BedrockItemUseTransactionData data)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(data);
        Legacy = legacy;
        Actions = [.. actions];
        Data = data;
    }

    internal BedrockTransactionLegacy Legacy { get; }

    internal ImmutableArray<BedrockTransactionAction> Actions { get; }

    internal BedrockItemUseTransactionData Data { get; }

    public bool Equals(BedrockPlayerAuthInputTransaction? other) =>
        other is not null && Legacy.Equals(other.Legacy) && Actions.SequenceEqual(other.Actions) && Data.Equals(other.Data);

    public override bool Equals(object? obj) => Equals(obj as BedrockPlayerAuthInputTransaction);

    public override int GetHashCode() => HashCode.Combine(Legacy, Actions.Length, Data);
}

/// <summary>Movement-only protocol-2169 player-auth-input payload with unhandled sections carried as presence markers rather than content.</summary>
/// <remarks>Presence markers without content decode and encode, while present-but-content tails are rejected, so this type never carries item-stack, block-action, vehicle-rotation, or predicted-vehicle data.</remarks>
internal sealed class BedrockPlayerAuthInputPacket : IEquatable<BedrockPlayerAuthInputPacket>
{
    internal BedrockPlayerAuthInputPacket(
        float pitch,
        float yaw,
        BedrockVector3 position,
        BedrockVector2 moveVector,
        float headYaw,
        IReadOnlyList<int>? inputs,
        byte inputMode,
        byte playMode,
        int interactionModel,
        BedrockVector2 interactRotation,
        ulong tick,
        BedrockVector3 delta,
        BedrockVector2 analogueMoveVector,
        BedrockVector3 cameraOrientation,
        BedrockVector2 rawMoveVector,
        BedrockPlayerAuthInputTransaction? transaction = null,
        bool transactionPresent = false,
        bool itemStackRequestPresent = false,
        bool blockActionsPresent = false,
        bool vehicleRotationPresent = false,
        bool predictedVehiclePresent = false)
    {
        Pitch = pitch;
        Yaw = yaw;
        Position = position;
        MoveVector = moveVector;
        HeadYaw = headYaw;
        Inputs = inputs is null ? null : ImmutableArray.CreateRange(inputs);
        InputMode = inputMode;
        PlayMode = playMode;
        InteractionModel = interactionModel;
        InteractRotation = interactRotation;
        Tick = tick;
        Delta = delta;
        AnalogueMoveVector = analogueMoveVector;
        CameraOrientation = cameraOrientation;
        RawMoveVector = rawMoveVector;
        Transaction = transaction;
        TransactionPresent = transactionPresent;
        ItemStackRequestPresent = itemStackRequestPresent;
        BlockActionsPresent = blockActionsPresent;
        VehicleRotationPresent = vehicleRotationPresent;
        PredictedVehiclePresent = predictedVehiclePresent;
    }

    internal float Pitch { get; }

    internal float Yaw { get; }

    internal BedrockVector3 Position { get; }

    internal BedrockVector2 MoveVector { get; }

    internal float HeadYaw { get; }

    internal ImmutableArray<int>? Inputs { get; }

    internal byte InputMode { get; }

    internal byte PlayMode { get; }

    internal int InteractionModel { get; }

    internal BedrockVector2 InteractRotation { get; }

    /// <summary>Gets the caller-owned client tick that must increase strictly across sends with an explicit wrap from ulong.MaxValue to zero.</summary>
    internal ulong Tick { get; }

    /// <summary>Gets the position change carried beside the absolute position as a protocol-2169 wire value the codec never interprets.</summary>
    internal BedrockVector3 Delta { get; }

    internal BedrockVector2 AnalogueMoveVector { get; }

    internal BedrockVector3 CameraOrientation { get; }

    internal BedrockVector2 RawMoveVector { get; }

    internal BedrockPlayerAuthInputTransaction? Transaction { get; }

    /// <summary>Gets whether the transaction section marker is set, independent of whether transaction content follows.</summary>
    internal bool TransactionPresent { get; }

    /// <summary>Gets whether the item-stack-request tail marker is set; tails with content are rejected rather than carried.</summary>
    internal bool ItemStackRequestPresent { get; }

    /// <summary>Gets whether the block-action tail marker is set; tails with content are rejected rather than carried.</summary>
    internal bool BlockActionsPresent { get; }

    /// <summary>Gets whether the vehicle-rotation tail marker is set; tails with content are rejected rather than carried.</summary>
    internal bool VehicleRotationPresent { get; }

    /// <summary>Gets whether the predicted-vehicle tail marker is set; tails with content are rejected rather than carried.</summary>
    internal bool PredictedVehiclePresent { get; }

    public bool Equals(BedrockPlayerAuthInputPacket? other) =>
        other is not null
        && Pitch.Equals(other.Pitch)
        && Yaw.Equals(other.Yaw)
        && Position.Equals(other.Position)
        && MoveVector.Equals(other.MoveVector)
        && HeadYaw.Equals(other.HeadYaw)
        && NullableSequenceEqual(Inputs, other.Inputs)
        && InputMode == other.InputMode
        && PlayMode == other.PlayMode
        && InteractionModel == other.InteractionModel
        && InteractRotation.Equals(other.InteractRotation)
        && Tick == other.Tick
        && Delta.Equals(other.Delta)
        && AnalogueMoveVector.Equals(other.AnalogueMoveVector)
        && CameraOrientation.Equals(other.CameraOrientation)
        && RawMoveVector.Equals(other.RawMoveVector)
        && Equals(Transaction, other.Transaction)
        && TransactionPresent == other.TransactionPresent
        && ItemStackRequestPresent == other.ItemStackRequestPresent
        && BlockActionsPresent == other.BlockActionsPresent
        && VehicleRotationPresent == other.VehicleRotationPresent
        && PredictedVehiclePresent == other.PredictedVehiclePresent;

    public override bool Equals(object? obj) => Equals(obj as BedrockPlayerAuthInputPacket);

    public override int GetHashCode() => HashCode.Combine(Pitch, Yaw, Tick, InputMode, PlayMode);

    private static bool NullableSequenceEqual(ImmutableArray<int>? left, ImmutableArray<int>? right)
    {
        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;
        return left.Value.SequenceEqual(right.Value);
    }
}

/// <summary>Protocol-2169 move-correction payload the client observes and counts without reconciling actor state or the simulation.</summary>
/// <remarks>Decoding proves the wire shape only; nothing here moves an actor or seeds physics.</remarks>
internal sealed class BedrockCorrectPlayerMovePredictionPacket : IEquatable<BedrockCorrectPlayerMovePredictionPacket>
{
    internal BedrockCorrectPlayerMovePredictionPacket(
        byte predictionType,
        BedrockVector3 position,
        BedrockVector3 delta,
        BedrockVector2 rotation,
        float? angularVelocity,
        bool onGround,
        ulong tick)
    {
        PredictionType = predictionType;
        Position = position;
        Delta = delta;
        Rotation = rotation;
        AngularVelocity = angularVelocity;
        OnGround = onGround;
        Tick = tick;
    }

    /// <summary>Gets the uninterpreted protocol-2169 prediction type, accepted as 0 or 1 with no gameplay meaning assigned.</summary>
    internal byte PredictionType { get; }

    internal BedrockVector3 Position { get; }

    internal BedrockVector3 Delta { get; }

    internal BedrockVector2 Rotation { get; }

    internal float? AngularVelocity { get; }

    internal bool OnGround { get; }

    internal ulong Tick { get; }

    public bool Equals(BedrockCorrectPlayerMovePredictionPacket? other) =>
        other is not null
        && PredictionType == other.PredictionType
        && Position.Equals(other.Position)
        && Delta.Equals(other.Delta)
        && Rotation.Equals(other.Rotation)
        && AngularVelocity.Equals(other.AngularVelocity)
        && OnGround == other.OnGround
        && Tick == other.Tick;

    public override bool Equals(object? obj) => Equals(obj as BedrockCorrectPlayerMovePredictionPacket);

    public override int GetHashCode() => HashCode.Combine(PredictionType, Position, Tick);
}

internal static class BedrockPlayerAuthInputCodecs
{
    /// <summary>Caps the input-id list at 128 entries per packet.</summary>
    /// <remarks>Each id must fall in 0 through 65 with no duplicates, and larger or repeated sets are rejected.</remarks>
    internal const int MaxInputsPerPacket = 128;

    /// <summary>Encodes one movement-only player-auth-input packet and rejects unsupported tails and out-of-range wire values.</summary>
    internal static byte[] Encode(BedrockPlayerAuthInputPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        Validate(packet);
        return Encode(writer => WritePacket(writer, packet));
    }

    /// <summary>Decodes one protocol-2169 player-auth-input payload and rejects present-but-content tails, unknown modes, and trailing bytes.</summary>
    internal static BedrockPlayerAuthInputPacket Decode(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        float pitch = ReadFiniteSingle(ref reader, "PlayerAuthInput pitch");
        float yaw = ReadFiniteSingle(ref reader, "PlayerAuthInput yaw");
        BedrockVector3 position = ReadVector3(ref reader, "PlayerAuthInput position");
        BedrockVector2 moveVector = ReadVector2(ref reader, "PlayerAuthInput move vector");
        float headYaw = ReadFiniteSingle(ref reader, "PlayerAuthInput head yaw");
        ImmutableArray<int>? inputs = ReadInputs(ref reader, actualLimits);
        byte inputMode = ReadInputMode(ref reader);
        byte playMode = ReadPlayMode(ref reader);
        int interactionModel = ReadInteractionModel(ref reader);
        BedrockVector2 interactRotation = ReadVector2(ref reader, "PlayerAuthInput interact rotation");
        ulong tick = reader.ReadUInt64VarInt();
        BedrockVector3 delta = ReadVector3(ref reader, "PlayerAuthInput delta");
        (bool transactionPresent, BedrockPlayerAuthInputTransaction? transaction) = ReadTransactionSection(ref reader, actualLimits);
        bool itemStackRequestPresent = ReadNullSection(ref reader, "item stack request");
        bool blockActionsPresent = ReadNullSection(ref reader, "block action");
        bool vehicleRotationPresent = ReadNullSection(ref reader, "vehicle rotation");
        bool predictedVehiclePresent = ReadNullSection(ref reader, "predicted vehicle");
        BedrockVector2 analogue = ReadVector2(ref reader, "PlayerAuthInput analogue move vector");
        BedrockVector3 camera = ReadVector3(ref reader, "PlayerAuthInput camera orientation");
        BedrockVector2 raw = ReadVector2(ref reader, "PlayerAuthInput raw move vector");
        EnsureEnd(reader);
        return new BedrockPlayerAuthInputPacket(pitch, yaw, position, moveVector, headYaw, inputs, inputMode, playMode, interactionModel, interactRotation, tick, delta, analogue, camera, raw, transaction, transactionPresent, itemStackRequestPresent, blockActionsPresent, vehicleRotationPresent, predictedVehiclePresent);
    }

    /// <summary>Encodes one move-correction packet and rejects prediction types outside 0 and 1.</summary>
    internal static byte[] Encode(BedrockCorrectPlayerMovePredictionPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        Validate(packet);
        return Encode(writer => WritePrediction(writer, packet));
    }

    /// <summary>Decodes one protocol-2169 move-correction payload and rejects unknown prediction types and trailing bytes.</summary>
    internal static BedrockCorrectPlayerMovePredictionPacket DecodePrediction(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        byte predictionType = reader.ReadByte();
        if (predictionType > 1)
            throw new BedrockCodecException($"Unknown move prediction type {predictionType}.");
        BedrockVector3 position = ReadVector3(ref reader, "MovePrediction position");
        BedrockVector3 delta = ReadVector3(ref reader, "MovePrediction delta");
        BedrockVector2 rotation = ReadVector2(ref reader, "MovePrediction rotation");
        float? angularVelocity = null;
        if (reader.ReadBoolean())
            angularVelocity = ReadFiniteSingle(ref reader, "MovePrediction angular velocity");
        bool onGround = reader.ReadBoolean();
        ulong tick = reader.ReadUInt64VarInt();
        EnsureEnd(reader);
        return new BedrockCorrectPlayerMovePredictionPacket(predictionType, position, delta, rotation, angularVelocity, onGround, tick);
    }

    private static void Validate(BedrockPlayerAuthInputPacket packet)
    {
        RequireFinite(packet.Pitch, "pitch");
        RequireFinite(packet.Yaw, "yaw");
        RequireFinite(packet.Position, "position");
        RequireFinite(packet.MoveVector, "move vector");
        RequireFinite(packet.HeadYaw, "head yaw");
        if (packet.Inputs is not null)
            ValidateInputs(packet.Inputs.Value);
        if (packet.InputMode > 4)
            throw new BedrockCodecException($"Unknown PlayerAuthInput input mode {packet.InputMode}.");
        if (packet.PlayMode > 9)
            throw new BedrockCodecException($"Unknown PlayerAuthInput play mode {packet.PlayMode}.");
        if (packet.InteractionModel is < 0 or > 2)
            throw new BedrockCodecException($"Unknown PlayerAuthInput interaction model {packet.InteractionModel}.");
        RequireFinite(packet.InteractRotation, "interact rotation");
        RequireFinite(packet.Delta, "delta");
        RequireFinite(packet.AnalogueMoveVector, "analogue move vector");
        RequireFinite(packet.CameraOrientation, "camera orientation");
        RequireFinite(packet.RawMoveVector, "raw move vector");
        if (packet.Transaction is not null)
        {
            if (!packet.TransactionPresent)
                throw new BedrockCodecException("The player transaction content requires its presence marker.");
            if (packet.Transaction.Actions.Length > BedrockInventoryTransactionCodecs.MaxActionsPerTransaction)
                throw new BedrockCodecException($"The player transaction action count {packet.Transaction.Actions.Length} exceeds its element limit.");
            RequireFinite(packet.Transaction.Data.PlayerPos, "transaction player position");
            RequireFinite(packet.Transaction.Data.ClickPos, "transaction click position");
        }
    }

    private static void Validate(BedrockCorrectPlayerMovePredictionPacket packet)
    {
        if (packet.PredictionType > 1)
            throw new BedrockCodecException($"Unknown move prediction type {packet.PredictionType}.");
        RequireFinite(packet.Position, "position");
        RequireFinite(packet.Delta, "delta");
        RequireFinite(packet.Rotation, "rotation");
        if (packet.AngularVelocity.HasValue)
            RequireFinite(packet.AngularVelocity.Value, "angular velocity");
    }

    private static void WritePacket(BedrockWriter writer, BedrockPlayerAuthInputPacket packet)
    {
        writer.WriteSingleLittleEndian(packet.Pitch);
        writer.WriteSingleLittleEndian(packet.Yaw);
        WriteVector3(writer, packet.Position);
        WriteVector2(writer, packet.MoveVector);
        writer.WriteSingleLittleEndian(packet.HeadYaw);
        writer.WriteBoolean(packet.Inputs.HasValue);
        if (packet.Inputs.HasValue)
        {
            writer.WriteUInt32VarInt(checked((uint)packet.Inputs.Value.Length));
            foreach (int input in packet.Inputs.Value)
                writer.WriteZigZag32(input);
        }

        writer.WriteUInt32VarInt(packet.InputMode);
        writer.WriteUInt32VarInt(packet.PlayMode);
        writer.WriteZigZag32(packet.InteractionModel);
        WriteVector2(writer, packet.InteractRotation);
        writer.WriteUInt64VarInt(packet.Tick);
        WriteVector3(writer, packet.Delta);
        WriteTransactionSection(writer, packet.TransactionPresent, packet.Transaction);
        WriteNullSection(writer, packet.ItemStackRequestPresent);
        WriteNullSection(writer, packet.BlockActionsPresent);
        WriteNullSection(writer, packet.VehicleRotationPresent);
        WriteNullSection(writer, packet.PredictedVehiclePresent);
        WriteVector2(writer, packet.AnalogueMoveVector);
        WriteVector3(writer, packet.CameraOrientation);
        WriteVector2(writer, packet.RawMoveVector);
    }

    private static void WritePrediction(BedrockWriter writer, BedrockCorrectPlayerMovePredictionPacket packet)
    {
        writer.WriteByte(packet.PredictionType);
        WriteVector3(writer, packet.Position);
        WriteVector3(writer, packet.Delta);
        WriteVector2(writer, packet.Rotation);
        writer.WriteBoolean(packet.AngularVelocity.HasValue);
        if (packet.AngularVelocity.HasValue)
            writer.WriteSingleLittleEndian(packet.AngularVelocity.Value);
        writer.WriteBoolean(packet.OnGround);
        writer.WriteUInt64VarInt(packet.Tick);
    }

    private static ImmutableArray<int>? ReadInputs(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        if (!reader.ReadBoolean())
            return null;
        uint count = reader.ReadUInt32VarInt();
        if (count > MaxInputsPerPacket || count > limits.MaxCollectionElements)
            throw new BedrockCodecException($"The player input count {count} exceeds its element limit.");
        var values = new int[count];
        for (int index = 0; index < values.Length; index++)
            values[index] = reader.ReadZigZag32();
        ValidateInputs(values);
        return ImmutableArray.CreateRange(values);
    }

    private static void ValidateInputs(IReadOnlyList<int> values)
    {
        if (values.Count > MaxInputsPerPacket)
            throw new BedrockCodecException($"The player input count {values.Count} exceeds its element limit.");
        var seen = new HashSet<int>();
        foreach (int value in values)
        {
            if (value is < 0 or > 65)
                throw new BedrockCodecException($"Unknown PlayerAuthInput input {value}.");
            if (!seen.Add(value))
                throw new BedrockCodecException($"Duplicate PlayerAuthInput input {value}.");
        }
    }

    private static byte ReadInputMode(ref BedrockReader reader)
    {
        uint value = reader.ReadUInt32VarInt();
        if (value > 4)
            throw new BedrockCodecException($"Unknown PlayerAuthInput input mode {value}.");
        return (byte)value;
    }

    private static byte ReadPlayMode(ref BedrockReader reader)
    {
        uint value = reader.ReadUInt32VarInt();
        if (value > 9)
            throw new BedrockCodecException($"Unknown PlayerAuthInput play mode {value}.");
        return (byte)value;
    }

    private static int ReadInteractionModel(ref BedrockReader reader)
    {
        int value = reader.ReadZigZag32();
        if (value is < 0 or > 2)
            throw new BedrockCodecException($"Unknown PlayerAuthInput interaction model {value}.");
        return value;
    }

    private static (bool Present, BedrockPlayerAuthInputTransaction? Transaction) ReadTransactionSection(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        if (!reader.ReadBoolean())
            return (false, null);
        if (!reader.ReadBoolean())
            return (true, null);
        int requestId = reader.ReadZigZag32();
        ImmutableArray<BedrockTransactionLegacyEntry> legacyEntries = BedrockInventoryTransactionCodecs.ReadLegacyEntries(ref reader, limits);
        var legacy = new BedrockTransactionLegacy(requestId, legacyEntries);
        ImmutableArray<BedrockTransactionAction> actions = ReadTransactionActions(ref reader, limits);
        BedrockItemUseTransactionData data = BedrockInventoryTransactionCodecs.ReadUseItemData(ref reader, limits);
        return (true, new BedrockPlayerAuthInputTransaction(legacy, actions, data));
    }

    private static ImmutableArray<BedrockTransactionAction> ReadTransactionActions(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        if (!reader.ReadBoolean())
            return [];
        if (!reader.ReadBoolean())
            return [];
        uint count = reader.ReadUInt32VarInt();
        if (count > BedrockInventoryTransactionCodecs.MaxActionsPerTransaction || count > (uint)limits.MaxCollectionElements)
            throw new BedrockCodecException($"The player transaction action count {count} exceeds its element limit.");
        var actions = new List<BedrockTransactionAction>(checked((int)Math.Min(count, 8)));
        for (uint index = 0; index < count; index++)
            actions.Add(BedrockInventoryTransactionCodecs.ReadAction(ref reader, limits));
        return [.. actions];
    }

    private static void WriteTransactionSection(BedrockWriter writer, bool present, BedrockPlayerAuthInputTransaction? transaction)
    {
        writer.WriteBoolean(present);
        if (!present)
            return;
        writer.WriteBoolean(transaction is not null);
        if (transaction is null)
            return;
        BedrockInventoryTransactionCodecs.WriteLegacyEntries(writer, transaction.Legacy);
        writer.WriteBoolean(true);
        writer.WriteBoolean(true);
        writer.WriteUInt32VarInt(checked((uint)transaction.Actions.Length));
        foreach (BedrockTransactionAction action in transaction.Actions)
            BedrockInventoryTransactionCodecs.WriteAction(writer, action);
        BedrockInventoryTransactionCodecs.WriteUseItemData(writer, transaction.Data);
    }

    private static bool ReadNullSection(ref BedrockReader reader, string kind)
    {
        if (!reader.ReadBoolean())
            return false;
        if (!reader.ReadBoolean())
            return true;
        throw new BedrockCodecException($"The PlayerAuthInput {kind} tail is not supported in this slice.");
    }

    private static void WriteNullSection(BedrockWriter writer, bool present)
    {
        writer.WriteBoolean(present);
        if (present)
            writer.WriteBoolean(false);
    }

    private static void WriteVector3(BedrockWriter writer, BedrockVector3 value)
    {
        writer.WriteSingleLittleEndian(value.X);
        writer.WriteSingleLittleEndian(value.Y);
        writer.WriteSingleLittleEndian(value.Z);
    }

    private static void WriteVector2(BedrockWriter writer, BedrockVector2 value)
    {
        writer.WriteSingleLittleEndian(value.X);
        writer.WriteSingleLittleEndian(value.Z);
    }

    private static BedrockVector3 ReadVector3(ref BedrockReader reader, string kind)
    {
        float x = reader.ReadSingleLittleEndian();
        float y = reader.ReadSingleLittleEndian();
        float z = reader.ReadSingleLittleEndian();
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            throw new BedrockCodecException($"The {kind} contains a non-finite component.");
        return new BedrockVector3(x, y, z);
    }

    private static BedrockVector2 ReadVector2(ref BedrockReader reader, string kind)
    {
        float x = reader.ReadSingleLittleEndian();
        float z = reader.ReadSingleLittleEndian();
        if (!float.IsFinite(x) || !float.IsFinite(z))
            throw new BedrockCodecException($"The {kind} contains a non-finite component.");
        return new BedrockVector2(x, z);
    }

    private static float ReadFiniteSingle(ref BedrockReader reader, string kind)
    {
        float value = reader.ReadSingleLittleEndian();
        if (!float.IsFinite(value))
            throw new BedrockCodecException($"The {kind} is non-finite.");
        return value;
    }

    private static void RequireFinite(float value, string kind)
    {
        if (!float.IsFinite(value))
            throw new BedrockCodecException($"The PlayerAuthInput {kind} is non-finite.");
    }

    private static void RequireFinite(BedrockVector3 value, string kind)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new BedrockCodecException($"The PlayerAuthInput {kind} contains a non-finite component.");
    }

    private static void RequireFinite(BedrockVector2 value, string kind)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Z))
            throw new BedrockCodecException($"The PlayerAuthInput {kind} contains a non-finite component.");
    }

    private static byte[] Encode(Action<BedrockWriter> write)
    {
        var output = new ArrayBufferWriter<byte>();
        write(new BedrockWriter(output));
        return output.WrittenSpan.ToArray();
    }

    private static void EnsureEnd(BedrockReader reader)
    {
        if (!reader.End)
            throw new BedrockCodecException($"The player auth input payload has {reader.Remaining} unread bytes.");
    }
}
