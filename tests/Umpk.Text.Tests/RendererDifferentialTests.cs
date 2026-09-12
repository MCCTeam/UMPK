using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using Umpk.Text;
using Xunit;

namespace Umpk.Text.Tests;

/// <summary>A 12-row corpus covering ANSI and plain-text rendering. It proves <see cref="ComponentFlattener"/> is the shared traversal: every row's SGR-rendered concatenation matches the ANSI contract, and every row's plain-text concatenation matches <see cref="Component.ToPlainText(ITranslationSource?)"/>.</summary>
public sealed class RendererDifferentialTests
{
    private const string Esc = "\u001b";

    private sealed class StubTranslations : ITranslationSource
    {
        private readonly Dictionary<string, string> _map;

        public StubTranslations(Dictionary<string, string> map) => _map = map;

        public bool TryResolve(string key, [NotNullWhen(true)] out string? template) =>
            _map.TryGetValue(key, out template);
    }

    // A styled run is "\e[{codes}m{text}\e[0m"; an unstyled run is plain text; codes are "38;2;r;g;b" then "1" (bold), "3" (italic), "4" (underline), "9" (strikethrough), joined with ';'.
    private sealed class AnsiSink : IStyledRunSink
    {
        private readonly StringBuilder _sb = new();

        public void Accept(in StyledRun run)
        {
            if (run.Text.Length == 0)
                return;

            string codes = BuildSgr(run.Style);
            if (codes.Length == 0)
            {
                _sb.Append(run.Text);
                return;
            }

            _sb.Append(Esc).Append('[').Append(codes).Append('m').Append(run.Text).Append(Esc).Append("[0m");
        }

        public override string ToString() => _sb.ToString();

        private static string BuildSgr(Style style)
        {
            var parts = new List<string>(6);
            if (style.Color is TextColor color)
            {
                int rgb = color.Rgb;
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"38;2;{(rgb >> 16) & 0xFF};{(rgb >> 8) & 0xFF};{rgb & 0xFF}"));
            }

            if (style.Bold == true)
                parts.Add("1");

            if (style.Italic == true)
                parts.Add("3");

            if (style.Underlined == true)
                parts.Add("4");

            if (style.Strikethrough == true)
                parts.Add("9");

            return string.Join(';', parts);
        }
    }

    private sealed record Row(string Name, Component Component, ITranslationSource? Translations, string ExpectedAnsi);

    private static readonly IReadOnlyList<Row> AllRows = BuildRows();

    private static readonly IReadOnlyDictionary<string, Row> RowsByName =
        AllRows.ToDictionary(r => r.Name, StringComparer.Ordinal);

    public static TheoryData<string> RowNames => [.. RowsByName.Keys];

    private static List<Row> BuildRows()
    {
        var rows = new List<Row>();

        // 1: named color Red -> 24-bit foreground. AnsiComponentRendererTests.NamedColor_MapsTo24BitForeground.
        rows.Add(new Row(
            "named_color_red",
            new Component(new TextContent("hi"), new Style { Color = TextColor.Red }),
            null,
            $"{Esc}[38;2;255;85;85mhi{Esc}[0m"));

        // 2: hex color + bold + underline combine into one SGR sequence. AnsiComponentRendererTests.HexColorAndDecorations_MapToCombinedSgr.
        rows.Add(new Row(
            "hex_bold_underline",
            new Component(
                new TextContent("x"),
                new Style { Color = TextColor.FromRgb(0x1E90FF), Bold = true, Underlined = true }),
            null,
            $"{Esc}[38;2;30;144;255;1;4mx{Esc}[0m"));

        // 3: empty style emits no escapes. AnsiComponentRendererTests.PlainText_HasNoEscapes_WhenStyleEmpty.
        rows.Add(new Row("empty_style_no_escapes", Component.Text("plain"), null, "plain"));

        // 4: a legacy color code under a BOLD PARENT still clears bold, proving Decompose does not simply inherit through Style.ApplyTo. Diverges from what LegacyText.Parse + ApplyTo would produce (bold would leak through).
        rows.Add(new Row(
            "legacy_color_code_clears_bold_parent",
            new Component(new TextContent("§cred"), new Style { Bold = true }),
            null,
            $"{Esc}[38;2;255;85;85mred{Esc}[0m"));

        // 5: a legacy reset under a BOLD PARENT restores the base style (bold), not an empty one,
        // while the color run before it still has bold cleared.
        rows.Add(new Row(
            "legacy_reset_restores_bold_parent",
            new Component(new TextContent("§agreen§rtail"), new Style { Bold = true }),
            null,
            $"{Esc}[38;2;85;255;85mgreen{Esc}[0m{Esc}[1mtail{Esc}[0m"));

        // 6: children inherit bold. AnsiComponentRendererTests.ChildrenInheritParentStyle.
        rows.Add(new Row(
            "children_inherit_bold",
            new Component(
                new TextContent("a"),
                new Style { Bold = true },
                [new Component(new TextContent("b"), Style.Empty)]),
            null,
            $"{Esc}[1ma{Esc}[0m{Esc}[1mb{Esc}[0m"));

        // 7: translatable "<%s> %s" with unstyled args. AnsiComponentRendererTests.TranslatableKey_ResolvesWithStyledArgs.
        rows.Add(new Row(
            "translatable_positional_args",
            new Component(new TranslatableContent(
                "test.chat", null, [Component.Text("Steve"), Component.Text("hello")])),
            new StubTranslations(new() { ["test.chat"] = "<%s> %s" }),
            "<Steve> hello"));

        // 8: unknown translation key uses the fallback. AnsiComponentRendererTests.TranslatableKey_UnknownUsesFallback.
        rows.Add(new Row(
            "translatable_unknown_key_fallback",
            new Component(new TranslatableContent("missing.key", "fallback %s", [Component.Text("arg")])),
            null,
            "fallback arg"));

        // 9: a translatable argument keeps its own color. AnsiComponentRendererTests.TranslatableArg_KeepsItsOwnColor.
        rows.Add(new Row(
            "translatable_arg_keeps_color",
            new Component(new TranslatableContent(
                "test.one", null, [new Component(new TextContent("Steve"), new Style { Color = TextColor.Yellow })])),
            new StubTranslations(new() { ["test.one"] = "hi %s" }),
            $"hi {Esc}[38;2;255;255;85mSteve{Esc}[0m"));

        // 10: three-deep nested styles accumulate down the chain (color from the grandparent, bold from the parent, italic from the leaf's own style).
        rows.Add(new Row(
            "three_deep_nested_styles",
            new Component(
                new TextContent("top"),
                new Style { Color = TextColor.Blue },
                [
                    new Component(
                        new TextContent("mid"),
                        new Style { Bold = true },
                        [new Component(new TextContent("leaf"), new Style { Italic = true })]),
                ]),
            null,
            $"{Esc}[38;2;85;85;255mtop{Esc}[0m{Esc}[38;2;85;85;255;1mmid{Esc}[0m{Esc}[38;2;85;85;255;1;3mleaf{Esc}[0m"));

        // 11: legacy codes inside a resolved translation template decode, and the substituted argument keeps the OUTER (undecorated) style rather than inheriting the decoded color.
        rows.Add(new Row(
            "legacy_codes_in_translated_template",
            new Component(new TranslatableContent("k", null, [Component.Text("mid")])),
            new StubTranslations(new() { ["k"] = "§cRED %s TAIL" }),
            $"{Esc}[38;2;255;85;85mRED {Esc}[0mmid TAIL"));

        // 12: %% collapses to one literal percent. Mirrors PlainTextTests.PlainText_HandlesLiteralPercent.
        rows.Add(new Row(
            "translatable_double_percent",
            new Component(new TranslatableContent("k", null, [])),
            new StubTranslations(new() { ["k"] = "100%% done" }),
            "100% done"));

        return rows;
    }

    [Theory]
    [MemberData(nameof(RowNames))]
    public void Flatten_ReproducesTheAnsiRendererOutput(string name)
    {
        Row row = RowsByName[name];
        var sink = new AnsiSink();

        ComponentFlattener.Flatten(row.Component, sink, ComponentFlattenOptions.For(row.Translations));

        Assert.Equal(row.ExpectedAnsi, sink.ToString());
    }

    [Theory]
    [MemberData(nameof(RowNames))]
    public void Flatten_ConcatMatchesToPlainText(string name)
    {
        Row row = RowsByName[name];
        var options = ComponentFlattenOptions.For(row.Translations) with { DecodeLegacyCodes = false };

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(row.Component, options);
        string concatenated = string.Concat(runs.Select(r => r.Text));

        Assert.Equal(row.Component.ToPlainText(row.Translations), concatenated);
    }
}
