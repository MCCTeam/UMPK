using System.Globalization;
using System.Text;

namespace Umpk.Nbt.Snbt;

/// <summary>Renders a tag as canonical stringified NBT (SNBT): type suffixes b/s/L/f/d, typed array prefixes [B;] / [I;] / [L;], unquoted keys where they match <c>[A-Za-z0-9._+-]+</c> and quoted-with-escapes otherwise. Compound members are emitted in the order the compound holds them (the model preserves insertion order); vanilla sorts keys for display only, and that ordering is not load-bearing for any consumer.</summary>
public static class SnbtPrinter
{
    /// <summary>Returns the SNBT rendering of <paramref name="tag"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is null.</exception>
    public static string Print(NbtTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var builder = new StringBuilder();
        Append(builder, tag);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, NbtTag tag)
    {
        switch (tag)
        {
            case NbtEnd:
                builder.Append("END");
                break;

            case NbtByte b:
                builder.Append(b.Value.ToString(CultureInfo.InvariantCulture)).Append('b');
                break;

            case NbtShort s:
                builder.Append(s.Value.ToString(CultureInfo.InvariantCulture)).Append('s');
                break;

            case NbtInt i:
                builder.Append(i.Value.ToString(CultureInfo.InvariantCulture));
                break;

            case NbtLong l:
                builder.Append(l.Value.ToString(CultureInfo.InvariantCulture)).Append('L');
                break;

            case NbtFloat f:
                builder.Append(JavaFloatString(f.Value)).Append('f');
                break;

            case NbtDouble d:
                builder.Append(JavaDoubleString(d.Value)).Append('d');
                break;

            case NbtString str:
                builder.Append(QuoteAndEscape(str.Value));
                break;

            case NbtByteArray ba:
                builder.Append("[B;");
                for (int j = 0; j < ba.Value.Length; j++)
                {
                    if (j != 0)
                        builder.Append(',');

                    builder.Append(ba.Value[j].ToString(CultureInfo.InvariantCulture)).Append('B');
                }

                builder.Append(']');
                break;

            case NbtIntArray ia:
                builder.Append("[I;");
                for (int j = 0; j < ia.Value.Length; j++)
                {
                    if (j != 0)
                        builder.Append(',');

                    builder.Append(ia.Value[j].ToString(CultureInfo.InvariantCulture));
                }

                builder.Append(']');
                break;

            case NbtLongArray la:
                builder.Append("[L;");
                for (int j = 0; j < la.Value.Length; j++)
                {
                    if (j != 0)
                        builder.Append(',');

                    builder.Append(la.Value[j].ToString(CultureInfo.InvariantCulture)).Append('L');
                }

                builder.Append(']');
                break;

            case NbtList list:
                builder.Append('[');
                for (int j = 0; j < list.Count; j++)
                {
                    if (j != 0)
                        builder.Append(',');

                    Append(builder, list[j]);
                }

                builder.Append(']');
                break;

            case NbtCompound compound:
                builder.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, NbtTag> member in compound)
                {
                    if (!first)
                        builder.Append(',');

                    first = false;
                    builder.Append(HandleKeyEscape(member.Key)).Append(':');
                    Append(builder, member.Value);
                }

                builder.Append('}');
                break;

            default:
                throw new NbtFormatException($"Cannot print NBT tag of runtime type {tag.GetType().Name}");
        }
    }

    /// <summary>Quotes and escapes a string, preferring <c>"</c> and switching to <c>'</c> when that avoids an escape. Backslashes and the active quote character are escaped.</summary>
    internal static string QuoteAndEscape(string value)
    {
        var builder = new StringBuilder(" ");
        char quote = '\0';
        foreach (char c in value)
        {
            if (c == '\\')
                builder.Append('\\');

            else if (c is '"' or '\'')
            {
                if (quote == '\0')
                    quote = c == '"' ? '\'' : '"';

                if (quote == c)
                    builder.Append('\\');

            }

            builder.Append(c);
        }

        if (quote == '\0')
            quote = '"';

        builder[0] = quote;
        builder.Append(quote);
        return builder.ToString();
    }

    private static string HandleKeyEscape(string key) =>
        IsSimpleValue(key) ? key : QuoteAndEscape(key);

    // Simple keys contain only ASCII letters, digits, dot, underscore, plus, and minus.
    private static bool IsSimpleValue(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (char c in value)
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '.' or '_' or '+' or '-'))
                return false;

        return true;
    }

    // SNBT floats require a decimal point or exponent to remain distinguishable from integers. Append ".0" when the shortest round-trip form has neither and is finite.
    private static string JavaFloatString(float value)
    {
        if (float.IsNaN(value))
            return "NaN";

        if (float.IsPositiveInfinity(value))
            return "Infinity";

        if (float.IsNegativeInfinity(value))
            return "-Infinity";

        string s = value.ToString("R", CultureInfo.InvariantCulture);
        return EnsureDecimalPoint(s);
    }

    private static string JavaDoubleString(double value)
    {
        if (double.IsNaN(value))
            return "NaN";

        if (double.IsPositiveInfinity(value))
            return "Infinity";

        if (double.IsNegativeInfinity(value))
            return "-Infinity";

        string s = value.ToString("R", CultureInfo.InvariantCulture);
        return EnsureDecimalPoint(s);
    }

    private static string EnsureDecimalPoint(string s)
    {
        foreach (char c in s)
            if (c is '.' or 'e' or 'E')
                return s;

        return s + ".0";
    }
}
