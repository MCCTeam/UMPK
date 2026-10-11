namespace Umpk.Protocol.Bedrock;

internal sealed class BedrockLargeBitSet : IEquatable<BedrockLargeBitSet>
{
    private readonly byte[] _bytes;

    private BedrockLargeBitSet(int logicalBitCount, byte[] bytes)
    {
        LogicalBitCount = logicalBitCount;
        _bytes = bytes;
    }

    internal int LogicalBitCount { get; }

    internal bool this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= LogicalBitCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            return (_bytes[index / 8] & (1 << (index % 8))) != 0;
        }
    }

    internal static BedrockLargeBitSet Empty(int logicalBitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalBitCount);
        return new BedrockLargeBitSet(logicalBitCount, new byte[(logicalBitCount + 7) / 8]);
    }

    internal BedrockLargeBitSet WithBit(int index)
    {
        _ = this[index];
        byte[] bytes = (byte[])_bytes.Clone();
        bytes[index / 8] |= (byte)(1 << (index % 8));
        return new BedrockLargeBitSet(LogicalBitCount, bytes);
    }

    internal static BedrockLargeBitSet FromSevenBitGroups(ReadOnlySpan<byte> groups, int logicalBitCount)
    {
        BedrockLargeBitSet value = Empty(logicalBitCount);
        for (int group = 0; group < groups.Length; group++)
        {
            byte bits = (byte)(groups[group] & 0x7f);
            for (int bit = 0; bit < 7; bit++)
            {
                int index = group * 7 + bit;
                if (index >= logicalBitCount)
                {
                    if ((bits & (1 << bit)) != 0)
                        throw new BedrockCodecException("The large bit set contains bits outside its logical width.");

                    continue;
                }

                if ((bits & (1 << bit)) != 0)
                    value._bytes[index / 8] |= (byte)(1 << (index % 8));
            }
        }

        return value;
    }

    internal byte GetSevenBitGroup(int group)
    {
        byte result = 0;
        for (int bit = 0; bit < 7; bit++)
        {
            int index = group * 7 + bit;
            if (index < LogicalBitCount && this[index])
                result |= (byte)(1 << bit);
        }

        return result;
    }

    public bool Equals(BedrockLargeBitSet? other) =>
        other is not null
        && LogicalBitCount == other.LogicalBitCount
        && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => Equals(obj as BedrockLargeBitSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LogicalBitCount);
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }
}
