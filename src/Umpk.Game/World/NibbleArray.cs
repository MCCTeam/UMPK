namespace Umpk.Game.World;

/// <summary>A 4-bit-per-cell array over 4096 cells (2048 bytes), the exact wire shape of a section's sky or block light array. Indexing follows the section's YZX linear order. Light is written on the session loop; per-byte reads are naturally atomic on the CLR memory model.</summary>
public sealed class NibbleArray
{
    /// <summary>The number of cells (16x16x16).</summary>
    public const int CellCount = 4096;

    /// <summary>The packed byte length (two nibbles per byte).</summary>
    public const int ByteLength = CellCount / 2;

    private readonly byte[] _data;

    /// <summary>Creates a zero-filled (fully dark) nibble array.</summary>
    public NibbleArray()
    {
        _data = new byte[ByteLength];
    }

    /// <summary>Wraps a decoded 2048-byte light array as delivered by the wire.</summary>
    /// <exception cref="ArgumentException"><paramref name="data"/> is not <see cref="ByteLength"/> bytes.</exception>
    public NibbleArray(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != ByteLength)
            throw new ArgumentException($"Light array must be {ByteLength} bytes; got {data.Length}.", nameof(data));

        _data = data;
    }

    /// <summary>Reads the 0-15 value at a linear cell index.</summary>
    public byte Get(int index)
    {
        byte packed = _data[index >> 1];
        return (byte)((index & 1) == 0 ? packed & 0x0F : (packed >> 4) & 0x0F);
    }

    /// <summary>Writes the 0-15 value at a linear cell index.</summary>
    public void Set(int index, byte value)
    {
        int byteIndex = index >> 1;
        byte packed = _data[byteIndex];
        _data[byteIndex] = (index & 1) == 0
            ? (byte)((packed & 0xF0) | (value & 0x0F))
            : (byte)((packed & 0x0F) | ((value & 0x0F) << 4));
    }
}
