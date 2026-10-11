using System.Buffers;

namespace Umpk.Protocol.Bedrock;

internal sealed record PlayerAuthInputListSlice(IReadOnlyList<int>? Inputs, ulong Tick);

internal sealed class ItemV4WireValue : IEquatable<ItemV4WireValue>
{
    internal ItemV4WireValue(short networkId, ushort count, uint metadata, int? stackId, uint blockRuntimeId, byte[] extraData)
    {
        ArgumentNullException.ThrowIfNull(extraData);
        NetworkId = networkId;
        Count = count;
        Metadata = metadata;
        StackId = stackId;
        BlockRuntimeId = blockRuntimeId;
        ExtraData = (byte[])extraData.Clone();
    }

    internal short NetworkId { get; }

    internal ushort Count { get; }

    internal uint Metadata { get; }

    internal int? StackId { get; }

    internal uint BlockRuntimeId { get; }

    internal byte[] ExtraData { get; }

    public bool Equals(ItemV4WireValue? other) =>
        other is not null
        && NetworkId == other.NetworkId
        && Count == other.Count
        && Metadata == other.Metadata
        && StackId == other.StackId
        && BlockRuntimeId == other.BlockRuntimeId
        && ExtraData.AsSpan().SequenceEqual(other.ExtraData);

    public override bool Equals(object? obj) => Equals(obj as ItemV4WireValue);

    public override int GetHashCode() => HashCode.Combine(NetworkId, Count, Metadata, StackId, BlockRuntimeId);
}

internal readonly record struct StackRequestActionHeader(uint TypeId, byte LegacyTypeId);

internal readonly record struct StackRequestSlotWireValue(byte SlotType, byte Slot, int StackNetworkId);

internal static class BedrockVerifiedFieldCodecs
{
    internal static byte[] Encode(PlayerAuthInputListSlice value) => Encode(writer =>
    {
        writer.WriteBoolean(value.Inputs is not null);
        if (value.Inputs is not null)
        {
            writer.WriteUInt32VarInt(checked((uint)value.Inputs.Count));
            foreach (int input in value.Inputs)
                writer.WriteZigZag32(input);
        }

        writer.WriteUInt64VarInt(value.Tick);
    });

    internal static PlayerAuthInputListSlice DecodePlayerAuthInputListSlice(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        IReadOnlyList<int>? inputs = null;
        if (reader.ReadBoolean())
        {
            uint count = reader.ReadUInt32VarInt();
            if (count > BedrockCodecLimits.Default.MaxCollectionElements)
                throw new BedrockCodecException("The player input list exceeds its element limit.");

            var values = new int[count];
            for (int index = 0; index < values.Length; index++)
                values[index] = reader.ReadZigZag32();

            inputs = values;
        }

        ulong tick = reader.ReadUInt64VarInt();
        EnsureEnd(reader);
        return new PlayerAuthInputListSlice(inputs, tick);
    }

    internal static byte[] Encode(ItemV4WireValue value) => Encode(writer =>
    {
        writer.WriteInt16LittleEndian(value.NetworkId);
        writer.WriteUInt16LittleEndian(value.Count);
        writer.WriteUInt32VarInt(value.Metadata);
        writer.WriteBoolean(value.StackId.HasValue);
        if (value.StackId.HasValue)
            writer.WriteZigZag32(value.StackId.Value);

        writer.WriteUInt32VarInt(value.BlockRuntimeId);
        writer.WriteByteArray(value.ExtraData);
    });

    internal static ItemV4WireValue DecodeItemV4(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        short networkId = reader.ReadInt16LittleEndian();
        ushort count = reader.ReadUInt16LittleEndian();
        uint metadata = reader.ReadUInt32VarInt();
        int? stackId = reader.ReadBoolean() ? reader.ReadZigZag32() : null;
        uint blockRuntimeId = reader.ReadUInt32VarInt();
        byte[] extra = reader.ReadByteArray();
        EnsureEnd(reader);
        return new ItemV4WireValue(networkId, count, metadata, stackId, blockRuntimeId, extra);
    }

    internal static byte[] Encode(StackRequestActionHeader value) => Encode(writer =>
    {
        writer.WriteUInt32VarInt(value.TypeId);
        writer.WriteByte(value.LegacyTypeId);
    });

    internal static StackRequestActionHeader DecodeStackRequestActionHeader(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        var value = new StackRequestActionHeader(reader.ReadUInt32VarInt(), reader.ReadByte());
        EnsureEnd(reader);
        return value;
    }

    internal static byte[] Encode(StackRequestSlotWireValue value) => Encode(writer =>
    {
        writer.WriteByte(value.SlotType);
        writer.WriteByte(value.Slot);
        writer.WriteInt32LittleEndian(value.StackNetworkId);
    });

    internal static StackRequestSlotWireValue DecodeStackRequestSlot(ReadOnlySpan<byte> payload)
    {
        var reader = new BedrockReader(payload);
        var value = new StackRequestSlotWireValue(reader.ReadByte(), reader.ReadByte(), reader.ReadInt32LittleEndian());
        EnsureEnd(reader);
        return value;
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
            throw new BedrockCodecException($"The field slice has {reader.Remaining} unread bytes.");
    }
}
