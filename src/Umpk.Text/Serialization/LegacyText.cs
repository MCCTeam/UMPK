using System.Globalization;
using System.Text;

namespace Umpk.Text.Serialization;

/// <summary>Decodes and encodes the legacy section-sign (<c>§</c>) formatting codes. Handles the sixteen vanilla color codes (<c>§0</c>..<c>§f</c>), the five decoration codes (<c>§k</c>..<c>§o</c>), <c>§r</c> reset, and the modern <c>§#rrggbb</c> hex-color extension that servers and plugins emit as well. Encoding is lossy: click/hover events, insertions, fonts, and translatable/score/selector/nbt/keybind content cannot be represented and are flattened to their plain text.</summary>
public static class LegacyText
{
    /// <summary>The section-sign prefix character (<c>U+00A7</c>).</summary>
    public const char Prefix = '§';

    /// <summary>Parses a legacy-coded string into a component whose children are the styled runs. Runs are split at each formatting code; a color or reset code starts a fresh style, decoration codes accumulate.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static Component Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var runs = new List<Component>();
        var current = new StringBuilder();
        var state = LegacyState.Reset;

        void Flush()
        {
            if (current.Length > 0)
            {
                runs.Add(new Component(new TextContent(current.ToString()), state.ToStyle()));
                current.Clear();
            }
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c != Prefix || i + 1 >= text.Length)
            {
                current.Append(c);
                i++;
                continue;
            }

            char code = text[i + 1];
            if (code == '#' && i + 7 < text.Length
                && TryParseHex(text.AsSpan(i + 2, 6), out int rgb))
            {
                Flush();
                state = state.WithColor(TextColor.FromRgb(rgb));
                i += 8;
                continue;
            }

            LegacyCode? parsed = ClassifyCode(code);
            if (parsed is null)
            {
                // Not a recognized code; keep the two characters verbatim.
                current.Append(c);
                i++;
                continue;
            }

            Flush();
            state = Apply(state, parsed.Value);
            i += 2;
        }

        Flush();

        if (runs.Count == 0)
            return Component.Empty;

        if (runs.Count == 1)
            return runs[0];

        // Wrap the runs as siblings under an empty base so no run's style leaks to the others.
        return new Component(TextContent.Empty, Style.Empty, runs);
    }

    /// <summary>Decomposes legacy-coded <paramref name="text"/> into styled runs pushed to <paramref name="sink"/>, following the wire formatting rules rather than <see cref="Parse"/>'s component-construction rules. The accumulator starts at <paramref name="baseStyle"/>; each decoration code (<c>k</c>/<c>l</c>/<c>m</c>/<c>n</c>/<c>o</c>) sets that decoration true on the accumulator; a color code (including the <c>#rrggbb</c> hex extension, checked first as <see cref="Parse"/> does) replaces the accumulator with the new color and the five decorations explicitly false while preserving click, hover, insertion, and font; <c>§r</c> resets the accumulator to <paramref name="baseStyle"/>, not to an empty style. Codes are case-insensitive; an unrecognized code consumes both characters and emits nothing; a trailing lone section sign is dropped.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="baseStyle"/> is null.</exception>
    public static void Decompose<TSink>(string text, Style baseStyle, TSink sink)
        where TSink : IStyledRunSink
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(baseStyle);

        var current = new StringBuilder();
        Style accumulator = baseStyle;

        void Flush()
        {
            if (current.Length > 0)
            {
                sink.Accept(new StyledRun(current.ToString(), accumulator));
                current.Clear();
            }
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c != Prefix)
            {
                current.Append(c);
                i++;
                continue;
            }

            if (i + 1 >= text.Length)
            {
                // A trailing lone section sign is dropped.
                break;
            }

            char code = text[i + 1];
            if (code == '#' && i + 7 < text.Length
                && TryParseHex(text.AsSpan(i + 2, 6), out int rgb))
            {
                Flush();
                accumulator = ApplyColor(accumulator, TextColor.FromRgb(rgb));
                i += 8;
                continue;
            }

            LegacyCode? parsed = ClassifyCode(code);
            if (parsed is null)
            {
                // An unrecognized code consumes both characters and emits nothing.
                i += 2;
                continue;
            }

            switch (parsed.Value.Kind)
            {
                case LegacyCodeKind.Color:
                    Flush();
                    accumulator = ApplyColor(accumulator, parsed.Value.Color);
                    break;

                case LegacyCodeKind.Reset:
                    Flush();
                    accumulator = baseStyle;
                    break;

                case LegacyCodeKind.Bold:
                    accumulator = accumulator with { Bold = true };
                    break;

                case LegacyCodeKind.Strikethrough:
                    accumulator = accumulator with { Strikethrough = true };
                    break;

                case LegacyCodeKind.Underline:
                    accumulator = accumulator with { Underlined = true };
                    break;

                case LegacyCodeKind.Italic:
                    accumulator = accumulator with { Italic = true };
                    break;

                case LegacyCodeKind.Obfuscated:
                    accumulator = accumulator with { Obfuscated = true };
                    break;
            }

            i += 2;
        }

        Flush();
    }

    // A colour code (including the hex extension) clears the five decorations to explicit false while preserving click/hover/insertion/font from the current accumulator ( 316-324 applyLegacyFormat's default arm constructs `new Style(textColor, null, null, null, null, null, this.clickEvent, this.hoverEvent, this.insertion, this.font)`; the decorations are explicit false here rather than null so a StyledRun built from this accumulator observably drops an inherited decoration instead of leaving it to inherit further).
    private static Style ApplyColor(Style accumulator, TextColor color) => new()
    {
        Color = color,
        Bold = false,
        Italic = false,
        Underlined = false,
        Strikethrough = false,
        Obfuscated = false,
        ClickEvent = accumulator.ClickEvent,
        HoverEvent = accumulator.HoverEvent,
        Insertion = accumulator.Insertion,
        Font = accumulator.Font,
    };

    /// <summary>Encodes a component to a legacy-coded string (lossy). Each run emits its color and decoration codes followed by its plain text; unrepresentable style and non-text content is flattened.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public static string Encode(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var sb = new StringBuilder();
        Encode(component, Style.Empty, sb);
        return sb.ToString();
    }

    private static void Encode(Component component, Style inherited, StringBuilder sb)
    {
        Style effective = component.Style.ApplyTo(inherited);
        AppendCodes(effective, sb);
        AppendContent(component.Content, sb);
        foreach (Component child in component.Children)
            Encode(child, effective, sb);

    }

    private static void AppendContent(ComponentContent content, StringBuilder sb)
    {
        switch (content)
        {
            case TextContent text:
                sb.Append(text.Text);
                break;
            case TranslatableContent translatable:
                sb.Append(translatable.Fallback ?? translatable.Key);
                break;
            case SelectorContent selector:
                sb.Append(selector.Pattern);
                break;
            case KeybindContent keybind:
                sb.Append(keybind.Keybind);
                break;
            case NbtContent nbt:
                sb.Append(nbt.NbtPath);
                break;
            case ScoreContent:
                break;
        }
    }

    private static void AppendCodes(Style style, StringBuilder sb)
    {
        if (style.Color is TextColor color)
            if (color.IsNamed && NamedCode(color.Name!) is char nameCode)
                sb.Append(Prefix).Append(nameCode);

            else
                sb.Append(Prefix).Append('#').Append(color.Rgb.ToString("x6", CultureInfo.InvariantCulture));

        if (style.Bold == true)
            sb.Append(Prefix).Append('l');

        if (style.Strikethrough == true)
            sb.Append(Prefix).Append('m');

        if (style.Underlined == true)
            sb.Append(Prefix).Append('n');

        if (style.Italic == true)
            sb.Append(Prefix).Append('o');

        if (style.Obfuscated == true)
            sb.Append(Prefix).Append('k');

    }

    private static LegacyState Apply(LegacyState state, LegacyCode code) => code.Kind switch
    {
        // A color code resets decorations and sets the color.
        LegacyCodeKind.Color => LegacyState.Reset.WithColor(code.Color),
        LegacyCodeKind.Reset => LegacyState.Reset,
        LegacyCodeKind.Bold => state with { Bold = true },
        LegacyCodeKind.Strikethrough => state with { Strikethrough = true },
        LegacyCodeKind.Underline => state with { Underlined = true },
        LegacyCodeKind.Italic => state with { Italic = true },
        LegacyCodeKind.Obfuscated => state with { Obfuscated = true },
        _ => state,
    };

    private static LegacyCode? ClassifyCode(char code)
    {
        char lower = char.ToLowerInvariant(code);
        return lower switch
        {
            'k' => new LegacyCode(LegacyCodeKind.Obfuscated, default),
            'l' => new LegacyCode(LegacyCodeKind.Bold, default),
            'm' => new LegacyCode(LegacyCodeKind.Strikethrough, default),
            'n' => new LegacyCode(LegacyCodeKind.Underline, default),
            'o' => new LegacyCode(LegacyCodeKind.Italic, default),
            'r' => new LegacyCode(LegacyCodeKind.Reset, default),
            _ => CodeColor(lower) is TextColor color ? new LegacyCode(LegacyCodeKind.Color, color) : null,
        };
    }

    private static TextColor? CodeColor(char code) => code switch
    {
        '0' => TextColor.Black,
        '1' => TextColor.DarkBlue,
        '2' => TextColor.DarkGreen,
        '3' => TextColor.DarkAqua,
        '4' => TextColor.DarkRed,
        '5' => TextColor.DarkPurple,
        '6' => TextColor.Gold,
        '7' => TextColor.Gray,
        '8' => TextColor.DarkGray,
        '9' => TextColor.Blue,
        'a' => TextColor.Green,
        'b' => TextColor.Aqua,
        'c' => TextColor.Red,
        'd' => TextColor.LightPurple,
        'e' => TextColor.Yellow,
        'f' => TextColor.White,
        _ => null,
    };

    private static char? NamedCode(string name) => name switch
    {
        "black" => '0',
        "dark_blue" => '1',
        "dark_green" => '2',
        "dark_aqua" => '3',
        "dark_red" => '4',
        "dark_purple" => '5',
        "gold" => '6',
        "gray" => '7',
        "dark_gray" => '8',
        "blue" => '9',
        "green" => 'a',
        "aqua" => 'b',
        "red" => 'c',
        "light_purple" => 'd',
        "yellow" => 'e',
        "white" => 'f',
        _ => null,
    };

    private static bool TryParseHex(ReadOnlySpan<char> hex, out int rgb) =>
        int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);

    private enum LegacyCodeKind
    {
        Color,
        Reset,
        Bold,
        Strikethrough,
        Underline,
        Italic,
        Obfuscated,
    }

    private readonly record struct LegacyCode(LegacyCodeKind Kind, TextColor Color);

    private readonly record struct LegacyState(
        TextColor? Color, bool Bold, bool Italic, bool Underlined, bool Strikethrough, bool Obfuscated)
    {
        public static LegacyState Reset => new(null, false, false, false, false, false);

        public LegacyState WithColor(TextColor color) => this with { Color = color };

        public Style ToStyle() => new()
        {
            Color = Color,
            Bold = Bold ? true : null,
            Italic = Italic ? true : null,
            Underlined = Underlined ? true : null,
            Strikethrough = Strikethrough ? true : null,
            Obfuscated = Obfuscated ? true : null,
        };
    }
}
