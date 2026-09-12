using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Umpk.Nbt.Snbt;

/// <summary>Parses stringified NBT (SNBT): quoted and unquoted strings, numeric type suffixes (b/s/l/f/d, case-insensitive), the typed arrays <c>[B;]</c> / <c>[I;]</c> / <c>[L;]</c>, homogeneous lists, and compounds. All failures raise <see cref="NbtFormatException"/>.</summary>
public static partial class SnbtParser
{
    /// <summary>Parses a single compound (the top-level SNBT form). Trailing data is an error.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="NbtFormatException">The text is not a single valid compound.</exception>
    public static NbtCompound ParseCompound(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new SnbtReader(text);
        NbtCompound result = ReadStruct(reader);
        reader.SkipWhitespace();
        if (reader.CanRead())
            throw new NbtFormatException($"Trailing SNBT data at position {reader.Cursor}");

        return result;
    }

    /// <summary>Parses a single value of any tag type. Trailing data is an error.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="NbtFormatException">The text is not a single valid tag.</exception>
    public static NbtTag ParseValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new SnbtReader(text);
        NbtTag result = ReadValue(reader);
        reader.SkipWhitespace();
        if (reader.CanRead())
            throw new NbtFormatException($"Trailing SNBT data at position {reader.Cursor}");

        return result;
    }

    private static NbtTag ReadValue(SnbtReader reader)
    {
        reader.SkipWhitespace();
        if (!reader.CanRead())
            throw new NbtFormatException("Expected an SNBT value");

        char c = reader.Peek();
        return c switch
        {
            '{' => ReadStruct(reader),
            '[' => ReadList(reader),
            _ => ReadTypedValue(reader),
        };
    }

    private static NbtCompound ReadStruct(SnbtReader reader)
    {
        Expect(reader, '{');
        var compound = new NbtCompound();
        reader.SkipWhitespace();
        while (reader.CanRead() && reader.Peek() != '}')
        {
            string key = ReadKey(reader);
            if (key.Length == 0)
                throw new NbtFormatException($"Expected a key at position {reader.Cursor}");

            Expect(reader, ':');
            compound.Put(key, ReadValue(reader));
            if (!HasElementSeparator(reader))
                break;

            if (!reader.CanRead())
                throw new NbtFormatException("Expected another key after ','");

        }

        Expect(reader, '}');
        return compound;
    }

    private static NbtTag ReadList(SnbtReader reader)
    {
        // [B; / [I; / [L; is a typed array; otherwise it is an ordinary list.
        if (reader.CanRead(3) && !IsQuotedStringStart(reader.Peek(1)) && reader.Peek(2) == ';')
            return ReadArray(reader);

        return ReadListTag(reader);
    }

    private static NbtList ReadListTag(SnbtReader reader)
    {
        Expect(reader, '[');
        reader.SkipWhitespace();
        if (!reader.CanRead())
            throw new NbtFormatException("Expected an SNBT list element");

        var list = new NbtList();
        NbtTagType? elementType = null;
        while (reader.Peek() != ']')
        {
            NbtTag element = ReadValue(reader);
            if (elementType is null)
                elementType = element.Type;

            else if (element.Type != elementType)
                throw new NbtFormatException(
                    $"Cannot insert {element.Type} into a list of {elementType}");

            list.Add(element);
            if (!HasElementSeparator(reader))
                break;

            if (!reader.CanRead())
                throw new NbtFormatException("Expected another SNBT list element");

        }

        Expect(reader, ']');
        return list;
    }

    private static NbtTag ReadArray(SnbtReader reader)
    {
        Expect(reader, '[');
        char kind = reader.Read();
        reader.Read(); // consume ';'
        reader.SkipWhitespace();
        if (!reader.CanRead())
            throw new NbtFormatException("Expected an SNBT array element");

        return kind switch
        {
            'B' => new NbtByteArray(ReadNumberArrayBytes(reader)),
            'I' => new NbtIntArray(ReadNumberArrayInts(reader)),
            'L' => new NbtLongArray(ReadNumberArrayLongs(reader)),
            _ => throw new NbtFormatException($"Invalid array type '{kind}'"),
        };
    }

    private static sbyte[] ReadNumberArrayBytes(SnbtReader reader)
    {
        var values = new List<sbyte>();
        while (reader.Peek() != ']')
        {
            NbtTag element = ReadValue(reader);
            if (element is not NbtByte b)
                throw new NbtFormatException($"Cannot insert {element.Type} into a byte array");

            values.Add(b.Value);
            if (!HasElementSeparator(reader))
                break;

            if (!reader.CanRead())
                throw new NbtFormatException("Expected another byte-array element");

        }

        Expect(reader, ']');
        return [.. values];
    }

    private static int[] ReadNumberArrayInts(SnbtReader reader)
    {
        var values = new List<int>();
        while (reader.Peek() != ']')
        {
            NbtTag element = ReadValue(reader);
            if (element is not NbtInt v)
                throw new NbtFormatException($"Cannot insert {element.Type} into an int array");

            values.Add(v.Value);
            if (!HasElementSeparator(reader))
                break;

            if (!reader.CanRead())
                throw new NbtFormatException("Expected another int-array element");

        }

        Expect(reader, ']');
        return [.. values];
    }

    private static long[] ReadNumberArrayLongs(SnbtReader reader)
    {
        var values = new List<long>();
        while (reader.Peek() != ']')
        {
            NbtTag element = ReadValue(reader);
            if (element is not NbtLong v)
                throw new NbtFormatException($"Cannot insert {element.Type} into a long array");

            values.Add(v.Value);
            if (!HasElementSeparator(reader))
                break;

            if (!reader.CanRead())
                throw new NbtFormatException("Expected another long-array element");

        }

        Expect(reader, ']');
        return [.. values];
    }

    private static string ReadKey(SnbtReader reader)
    {
        reader.SkipWhitespace();
        if (!reader.CanRead())
            throw new NbtFormatException("Expected an SNBT key");

        return reader.ReadString();
    }

    private static NbtTag ReadTypedValue(SnbtReader reader)
    {
        reader.SkipWhitespace();
        int start = reader.Cursor;
        if (IsQuotedStringStart(reader.Peek()))
            return new NbtString(reader.ReadQuotedString());

        string raw = reader.ReadUnquotedString();
        if (raw.Length == 0)
        {
            reader.Cursor = start;
            throw new NbtFormatException($"Expected a value at position {start}");
        }

        return TypeValue(raw);
    }

    private static NbtTag TypeValue(string raw)
    {
        if (FloatPattern().IsMatch(raw)
            && float.TryParse(raw.AsSpan(0, raw.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float fv))
            return new NbtFloat(fv);

        if (BytePattern().IsMatch(raw)
            && sbyte.TryParse(raw.AsSpan(0, raw.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte bv))
            return new NbtByte(bv);

        if (LongPattern().IsMatch(raw)
            && long.TryParse(raw.AsSpan(0, raw.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long lv))
            return new NbtLong(lv);

        if (ShortPattern().IsMatch(raw)
            && short.TryParse(raw.AsSpan(0, raw.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out short sv))
            return new NbtShort(sv);

        if (IntPattern().IsMatch(raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
            return new NbtInt(iv);

        if (DoublePattern().IsMatch(raw)
            && double.TryParse(raw.AsSpan(0, raw.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double dv))
            return new NbtDouble(dv);

        if (DoublePatternNoSuffix().IsMatch(raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv2))
            return new NbtDouble(dv2);

        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
            return new NbtByte(true);

        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            return new NbtByte(false);

        return new NbtString(raw);
    }

    private static bool HasElementSeparator(SnbtReader reader)
    {
        reader.SkipWhitespace();
        if (reader.CanRead() && reader.Peek() == ',')
        {
            reader.Skip();
            reader.SkipWhitespace();
            return true;
        }

        return false;
    }

    private static void Expect(SnbtReader reader, char c)
    {
        reader.SkipWhitespace();
        reader.Expect(c);
    }

    private static bool IsQuotedStringStart(char c) => c is '"' or '\'';

    [GeneratedRegex("^(?:[-+]?(?:[0-9]+[.]?|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?f)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FloatPattern();

    [GeneratedRegex("^(?:[-+]?(?:[0-9]+[.]?|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?d)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DoublePattern();

    [GeneratedRegex("^(?:[-+]?(?:[0-9]+[.]|[0-9]*[.][0-9]+)(?:e[-+]?[0-9]+)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DoublePatternNoSuffix();

    [GeneratedRegex("^(?:[-+]?(?:0|[1-9][0-9]*)b)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BytePattern();

    [GeneratedRegex("^(?:[-+]?(?:0|[1-9][0-9]*)l)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LongPattern();

    [GeneratedRegex("^(?:[-+]?(?:0|[1-9][0-9]*)s)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShortPattern();

    [GeneratedRegex("^(?:[-+]?(?:0|[1-9][0-9]*))$", RegexOptions.CultureInvariant)]
    private static partial Regex IntPattern();
}
