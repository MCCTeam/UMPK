using System.Collections.Frozen;
using System.Globalization;

namespace Umpk.Text;

/// <summary>A text color: either one of the sixteen named wire colors or an arbitrary 24-bit RGB value. Named colors serialize by name; custom colors serialize as <c>#rrggbb</c> (lowercase-insensitive on parse, six-digit uppercase on write, using the canonical six-digit uppercase form).</summary>
public readonly struct TextColor : IEquatable<TextColor>
{
    private readonly int _value;
    private readonly string? _name;

    private TextColor(int value, string? name)
    {
        _value = value & 0xFFFFFF;
        _name = name;
    }

    /// <summary>The 24-bit RGB value (0xRRGGBB).</summary>
    public int Rgb => _value;

    /// <summary>The vanilla color name if this is a named color, otherwise null.</summary>
    public string? Name => _name;

    /// <summary>True when this color is one of the sixteen vanilla named colors.</summary>
    public bool IsNamed => _name is not null;

    /// <summary>Creates a custom RGB color (named-color detection is not applied).</summary>
    public static TextColor FromRgb(int rgb) => new(rgb, null);

    /// <summary>Gets a named color by its vanilla name (for example <c>red</c>), or null if the name is unknown.</summary>
    public static TextColor? FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return NamedColors.TryGetValue(name, out TextColor color) ? color : null;
    }

    /// <summary>The serialized form: the color name for named colors, otherwise <c>#RRGGBB</c>.</summary>
    public string Serialize() =>
        _name ?? string.Create(CultureInfo.InvariantCulture, $"#{_value:X6}");

    /// <summary>Parses a color string: a vanilla color name, or <c>#rrggbb</c> hex. Returns null when the string is neither a known name nor a valid six-digit hex color, matching vanilla <c>color parsing</c> semantics (which yields an error result).</summary>
    public static TextColor? Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.StartsWith('#'))
        {
            if (value.Length == 7
                && int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
                && rgb is >= 0 and <= 0xFFFFFF)
                return FromRgb(rgb);

            return null;
        }

        return NamedColors.TryGetValue(value, out TextColor named) ? named : null;
    }

    /// <inheritdoc/>
    public bool Equals(TextColor other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TextColor other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value;

    /// <inheritdoc/>
    public override string ToString() => Serialize();

    /// <summary>Value equality on the RGB value (matching vanilla, which ignores the name).</summary>
    public static bool operator ==(TextColor left, TextColor right) => left.Equals(right);

    /// <summary>Value inequality on the RGB value.</summary>
    public static bool operator !=(TextColor left, TextColor right) => !left.Equals(right);

    // The sixteen vanilla named colors, in ChatFormatting order, with their RGB values. Named-color constructor arguments.
    private static readonly (string Name, int Rgb)[] NamedTable =
    [
        ("black", 0x000000),
        ("dark_blue", 0x0000AA),
        ("dark_green", 0x00AA00),
        ("dark_aqua", 0x00AAAA),
        ("dark_red", 0xAA0000),
        ("dark_purple", 0xAA00AA),
        ("gold", 0xFFAA00),
        ("gray", 0xAAAAAA),
        ("dark_gray", 0x555555),
        ("blue", 0x5555FF),
        ("green", 0x55FF55),
        ("aqua", 0x55FFFF),
        ("red", 0xFF5555),
        ("light_purple", 0xFF55FF),
        ("yellow", 0xFFFF55),
        ("white", 0xFFFFFF),
    ];

    private static readonly FrozenDictionary<string, TextColor> NamedColors =
        NamedTable.ToFrozenDictionary(
            t => t.Name,
            t => new TextColor(t.Rgb, t.Name),
            StringComparer.Ordinal);

    // Named colors are exposed as statics for ergonomic construction.

    /// <summary>The vanilla <c>black</c> color.</summary>
    public static TextColor Black => NamedColors["black"];

    /// <summary>The vanilla <c>dark_blue</c> color.</summary>
    public static TextColor DarkBlue => NamedColors["dark_blue"];

    /// <summary>The vanilla <c>dark_green</c> color.</summary>
    public static TextColor DarkGreen => NamedColors["dark_green"];

    /// <summary>The vanilla <c>dark_aqua</c> color.</summary>
    public static TextColor DarkAqua => NamedColors["dark_aqua"];

    /// <summary>The vanilla <c>dark_red</c> color.</summary>
    public static TextColor DarkRed => NamedColors["dark_red"];

    /// <summary>The vanilla <c>dark_purple</c> color.</summary>
    public static TextColor DarkPurple => NamedColors["dark_purple"];

    /// <summary>The vanilla <c>gold</c> color.</summary>
    public static TextColor Gold => NamedColors["gold"];

    /// <summary>The vanilla <c>gray</c> color.</summary>
    public static TextColor Gray => NamedColors["gray"];

    /// <summary>The vanilla <c>dark_gray</c> color.</summary>
    public static TextColor DarkGray => NamedColors["dark_gray"];

    /// <summary>The vanilla <c>blue</c> color.</summary>
    public static TextColor Blue => NamedColors["blue"];

    /// <summary>The vanilla <c>green</c> color.</summary>
    public static TextColor Green => NamedColors["green"];

    /// <summary>The vanilla <c>aqua</c> color.</summary>
    public static TextColor Aqua => NamedColors["aqua"];

    /// <summary>The vanilla <c>red</c> color.</summary>
    public static TextColor Red => NamedColors["red"];

    /// <summary>The vanilla <c>light_purple</c> color.</summary>
    public static TextColor LightPurple => NamedColors["light_purple"];

    /// <summary>The vanilla <c>yellow</c> color.</summary>
    public static TextColor Yellow => NamedColors["yellow"];

    /// <summary>The vanilla <c>white</c> color.</summary>
    public static TextColor White => NamedColors["white"];
}
