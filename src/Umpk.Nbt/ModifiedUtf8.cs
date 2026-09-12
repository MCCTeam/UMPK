using System.Buffers;
using System.Text;

namespace Umpk.Nbt;

/// <summary>Java modified UTF-8 (CESU-8 with a two-byte NUL) as produced and consumed by <c>java.io.DataOutputStream.writeUTF</c> / <c>DataInputStream.readUTF</c>, which every NBT string field uses. The encoding differs from standard UTF-8 in two ways: the codepoint U+0000 is written as <c>0xC0 0x80</c>, and codepoints above U+FFFF are written as a UTF-8-encoded surrogate pair (six bytes) rather than a four-byte sequence. A two-byte big-endian unsigned length prefix (in bytes, max 65535) precedes the data.</summary>
internal static class ModifiedUtf8
{
    /// <summary>The maximum byte length a single modified-UTF-8 string may encode to.</summary>
    public const int MaxByteLength = 65535;

    /// <summary>Computes the modified-UTF-8 byte length of <paramref name="value"/> (excluding the length prefix). Because .NET strings are UTF-16, each surrogate half is counted as its own three-byte unit, which reproduces Java's CESU-8 six-bytes-per-astral-codepoint behavior exactly.</summary>
    public static int GetByteLength(string value)
    {
        long length = 0;
        foreach (char c in value)
            if (c is >= '\u0001' and <= '\u007F')
                length += 1;

            else if (c > '\u07FF')
                length += 3;

            else
                length += 2;

        return length > int.MaxValue ? int.MaxValue : (int)length;
    }

    /// <summary>Encodes <paramref name="value"/> into a modified-UTF-8 payload prefixed by a two-byte big-endian length, appending to <paramref name="output"/>.</summary>
    /// <exception cref="NbtFormatException">The encoded form exceeds 65535 bytes.</exception>
    public static void Write(IBufferWriter<byte> output, string value)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(value);

        int byteLength = GetByteLength(value);
        if (byteLength > MaxByteLength)
            throw new NbtFormatException($"Encoded string too long: {byteLength} bytes (max {MaxByteLength})");

        Span<byte> span = output.GetSpan(byteLength + 2);
        span[0] = (byte)((byteLength >> 8) & 0xFF);
        span[1] = (byte)(byteLength & 0xFF);
        int pos = 2;
        foreach (char c in value)
            if (c is >= '\u0001' and <= '\u007F')
                span[pos++] = (byte)c;

            else if (c > '\u07FF')
            {
                span[pos++] = (byte)(0xE0 | ((c >> 12) & 0x0F));
                span[pos++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                span[pos++] = (byte)(0x80 | (c & 0x3F));
            }
            else
            {
                span[pos++] = (byte)(0xC0 | ((c >> 6) & 0x1F));
                span[pos++] = (byte)(0x80 | (c & 0x3F));
            }

        output.Advance(pos);
    }

    /// <summary>Decodes a modified-UTF-8 string from <paramref name="source"/> starting at <paramref name="offset"/>, expecting the two-byte length prefix. Advances <paramref name="offset"/> past the consumed bytes.</summary>
    /// <exception cref="NbtFormatException">Truncated input or an illegal byte sequence.</exception>
    public static string Read(ReadOnlySpan<byte> source, ref int offset)
    {
        if (offset + 2 > source.Length)
            throw new NbtFormatException("Truncated NBT string: missing length prefix");

        int utfLength = (source[offset] << 8) | source[offset + 1];
        offset += 2;
        if (offset + utfLength > source.Length)
            throw new NbtFormatException(
                $"Truncated NBT string: declared {utfLength} bytes, {source.Length - offset} available");

        ReadOnlySpan<byte> data = source.Slice(offset, utfLength);
        offset += utfLength;
        return Decode(data);
    }

    /// <summary>Decodes a modified-UTF-8 byte sequence with no length prefix. Follows the exact validation rules of <c>DataInputStream.readUTF</c>.</summary>
    /// <exception cref="NbtFormatException">An illegal or truncated byte sequence.</exception>
    public static string Decode(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder(data.Length);
        int i = 0;
        while (i < data.Length)
        {
            int b1 = data[i];
            if (b1 < 0x80)
            {
                // 0xxxxxxx (a genuine 0x00 byte is legal on decode; only encoders avoid it)
                builder.Append((char)b1);
                i += 1;
            }
            else if ((b1 & 0xE0) == 0xC0)
            {
                // 110xxxxx 10xxxxxx
                if (i + 1 >= data.Length)
                    throw new NbtFormatException("Malformed modified UTF-8: truncated 2-byte sequence");

                int b2 = data[i + 1];
                if ((b2 & 0xC0) != 0x80)
                    throw new NbtFormatException($"Malformed modified UTF-8: bad continuation byte at {i + 1}");

                builder.Append((char)(((b1 & 0x1F) << 6) | (b2 & 0x3F)));
                i += 2;
            }
            else if ((b1 & 0xF0) == 0xE0)
            {
                // 1110xxxx 10xxxxxx 10xxxxxx
                if (i + 2 >= data.Length)
                    throw new NbtFormatException("Malformed modified UTF-8: truncated 3-byte sequence");

                int b2 = data[i + 1];
                int b3 = data[i + 2];
                if ((b2 & 0xC0) != 0x80 || (b3 & 0xC0) != 0x80)
                    throw new NbtFormatException($"Malformed modified UTF-8: bad continuation byte near {i}");

                builder.Append((char)(((b1 & 0x0F) << 12) | ((b2 & 0x3F) << 6) | (b3 & 0x3F)));
                i += 3;
            }
            else
                throw new NbtFormatException($"Malformed modified UTF-8: illegal leading byte 0x{b1:X2} at {i}");

        }

        return builder.ToString();
    }
}
