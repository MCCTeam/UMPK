using System.Text;

namespace Umpk.Nbt.Snbt;

/// <summary>A minimal cursor for quoted and unquoted string reading, whitespace skipping, and single-character expectations. It stays internal and dependency-free so the NBT package needs no parser dependency.</summary>
internal sealed class SnbtReader
{
    private const char Escape = '\\';
    private const char DoubleQuote = '"';
    private const char SingleQuote = '\'';

    private readonly string _input;

    public SnbtReader(string input) => _input = input;

    public int Cursor { get; set; }

    public bool CanRead(int length) => Cursor + length <= _input.Length;

    public bool CanRead() => CanRead(1);

    public char Peek() => _input[Cursor];

    public char Peek(int offset) => _input[Cursor + offset];

    public char Read() => _input[Cursor++];

    public void Skip() => Cursor++;

    public void SkipWhitespace()
    {
        while (CanRead() && char.IsWhiteSpace(Peek()))
            Skip();

    }

    public void Expect(char c)
    {
        if (!CanRead() || Peek() != c)
            throw new NbtFormatException($"Expected '{c}' at position {Cursor}");

        Skip();
    }

    public string ReadString()
    {
        if (!CanRead())
            return string.Empty;

        char next = Peek();
        if (IsQuotedStringStart(next))
        {
            Skip();
            return ReadStringUntil(next);
        }

        return ReadUnquotedString();
    }

    public string ReadUnquotedString()
    {
        int start = Cursor;
        while (CanRead() && IsAllowedInUnquotedString(Peek()))
            Skip();

        return _input[start..Cursor];
    }

    public string ReadQuotedString()
    {
        if (!CanRead())
            return string.Empty;

        char next = Peek();
        if (!IsQuotedStringStart(next))
            throw new NbtFormatException($"Expected a quote at position {Cursor}");

        Skip();
        return ReadStringUntil(next);
    }

    private string ReadStringUntil(char terminator)
    {
        var result = new StringBuilder();
        bool escaped = false;
        while (CanRead())
        {
            char c = Read();
            if (escaped)
                if (c == terminator || c == Escape)
                {
                    result.Append(c);
                    escaped = false;
                }
                else
                {
                    Cursor--;
                    throw new NbtFormatException($"Invalid escape sequence '\\{c}' at position {Cursor}");
                }

            else if (c == Escape)
                escaped = true;

            else if (c == terminator)
                return result.ToString();

            else
                result.Append(c);

        }

        throw new NbtFormatException("Unterminated quoted SNBT string");
    }

    private static bool IsQuotedStringStart(char c) => c is DoubleQuote or SingleQuote;

    private static bool IsAllowedInUnquotedString(char c) =>
        c is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z')
            or '_' or '-' or '.' or '+';
}
