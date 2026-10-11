using System.Buffers;
using Umpk.Nbt;

namespace Umpk.Protocol.Bedrock;

internal sealed record BedrockItemExtraData(
    NbtTag? Tag,
    byte NbtVersion,
    IReadOnlyList<string> CanPlaceOn,
    IReadOnlyList<string> CanDestroy,
    long? BlockingTick);

internal static class BedrockItemExtraDataCodec
{
    internal static byte[] Encode(BedrockItemExtraData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(value.CanPlaceOn);
        ArgumentNullException.ThrowIfNull(value.CanDestroy);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        if (value.Tag is null)
            writer.WriteUInt16LittleEndian(0);
        else
        {
            writer.WriteUInt16LittleEndian(ushort.MaxValue);
            writer.WriteByte(value.NbtVersion);
            writer.WriteBytes(BedrockNbtCodec.Encode(value.Tag, BedrockNbtWireFormat.LittleEndian));
        }

        WriteStringList(writer, value.CanPlaceOn);
        WriteStringList(writer, value.CanDestroy);
        if (value.BlockingTick.HasValue)
            writer.WriteInt64LittleEndian(value.BlockingTick.Value);

        return output.WrittenSpan.ToArray();
    }

    internal static BedrockItemExtraData Decode(ReadOnlySpan<byte> payload, bool hasBlockingTick, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        ushort hasNbt = reader.ReadUInt16LittleEndian();
        NbtTag? tag;
        byte version;
        if (hasNbt == 0)
        {
            tag = null;
            version = 0;
        }
        else if (hasNbt == ushort.MaxValue)
        {
            version = reader.ReadByte();
            tag = BedrockNbtCodec.DecodePrefix(ref reader, BedrockNbtWireFormat.LittleEndian, actualLimits);
        }
        else
            throw new BedrockCodecException($"Invalid item NBT marker 0x{hasNbt:x4}.");

        IReadOnlyList<string> canPlaceOn = ReadStringList(ref reader, actualLimits);
        IReadOnlyList<string> canDestroy = ReadStringList(ref reader, actualLimits);
        long? blockingTick = hasBlockingTick ? reader.ReadInt64LittleEndian() : null;
        if (!reader.End)
            throw new BedrockCodecException("The item extra data has unread bytes.");

        return new BedrockItemExtraData(tag, version, canPlaceOn, canDestroy, blockingTick);
    }

    private static void WriteStringList(BedrockWriter writer, IReadOnlyList<string> values)
    {
        writer.WriteInt32LittleEndian(values.Count);
        foreach (string value in values)
            writer.WriteShortString(value);
    }

    private static IReadOnlyList<string> ReadStringList(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        int count = reader.ReadInt32LittleEndian();
        if (count < 0 || count > limits.MaxCollectionElements)
            throw new BedrockCodecException($"Invalid item string list count {count}.");

        var values = new string[count];
        for (int index = 0; index < values.Length; index++)
            values[index] = reader.ReadShortString();

        return values;
    }
}
