namespace Umpk.BedrockDataGen.Schema;

internal sealed class LargeBitSetEncodingPolicy
{
    public LargeBitSetEncodingPolicy(int logicalBitCount, int maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalBitCount);
        LogicalBitCount = logicalBitCount;
        EncodedByteCount = checked((logicalBitCount + 6) / 7);
        if (maxBytes < EncodedByteCount || maxBytes > 64)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        MaxBytes = maxBytes;
    }

    public int LogicalBitCount { get; }

    public int EncodedByteCount { get; }

    public int MaxBytes { get; }

    public byte[] CreateVector(IReadOnlyList<int> setBits)
    {
        ArgumentNullException.ThrowIfNull(setBits);
        var encoded = new byte[EncodedByteCount];
        for (int index = 0; index < encoded.Length - 1; index++)
            encoded[index] = 0x80;
        foreach (int bit in setBits)
        {
            if ((uint)bit >= (uint)LogicalBitCount)
                throw new ArgumentOutOfRangeException(nameof(setBits), bit, "Bit must fit the logical profile width.");
            int byteIndex = bit / 7;
            int bitIndex = bit % 7;
            encoded[byteIndex] |= (byte)(1 << bitIndex);
        }
        return encoded;
    }
}
