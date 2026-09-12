using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Text.Tests;

public sealed class TextDecompositionTests
{
    private sealed class StubTranslations : ITranslationSource
    {
        private readonly Dictionary<string, string> _map;

        public StubTranslations(Dictionary<string, string> map) => _map = map;

        public bool TryResolve(string key, [NotNullWhen(true)] out string? template) =>
            _map.TryGetValue(key, out template);
    }

    private sealed class ListSink : IStyledRunSink
    {
        public List<StyledRun> Runs { get; } = [];

        public void Accept(in StyledRun run) => Runs.Add(run);
    }

    private static List<StyledRun> Decompose(string text, Style baseStyle)
    {
        var sink = new ListSink();
        LegacyText.Decompose(text, baseStyle, sink);
        return sink.Runs;
    }

    // Recursively flattens a Component tree produced by LegacyText.Parse, resolving style inheritance the same way ComponentFlattener does. Used only by the agreement test below, kept local so this file does not depend on ComponentFlattener's own correctness to prove Decompose's.
    private static void FlattenParsed(Component component, Style inherited, List<StyledRun> into)
    {
        Style effective = component.Style.ApplyTo(inherited);
        if (component.Content is TextContent text && text.Text.Length > 0)
            into.Add(new StyledRun(text.Text, effective));

        foreach (Component child in component.Children)
            FlattenParsed(child, effective, into);

    }

    [Fact]
    public void Decompose_ColorCode_ClearsInheritedDecorations_PerVanilla()
    {
        // A legacy color code clears all five decoration flags even when a bold ancestor is in scope.
        var baseStyle = new Style { Bold = true };

        List<StyledRun> runs = Decompose("§cred", baseStyle);

        StyledRun run = Assert.Single(runs);
        Assert.Equal("red", run.Text);
        Assert.Equal(TextColor.Red, run.Style.Color);
        Assert.False(run.Bold);
        Assert.Equal((bool?)false, run.Style.Bold);
    }

    [Fact]
    public void Decompose_Reset_RestoresTheBaseStyle_NotEmpty()
    {
        // RESET restores the supplied base style, not an empty style.
        var baseStyle = new Style { Italic = true, Font = "minecraft:alt" };

        List<StyledRun> runs = Decompose("§cstyled§rplain", baseStyle);

        Assert.Equal(2, runs.Count);
        StyledRun plain = runs[1];
        Assert.Equal("plain", plain.Text);
        Assert.True(plain.Italic);
        Assert.Equal("minecraft:alt", plain.Font);
        Assert.Null(plain.Color);
    }

    [Fact]
    public void Decompose_DecorationCode_AccumulatesOnTheBaseStyle()
    {
        var baseStyle = new Style { Color = TextColor.Blue };

        List<StyledRun> runs = Decompose("§lbold", baseStyle);

        StyledRun run = Assert.Single(runs);
        Assert.True(run.Bold);
        Assert.Equal(TextColor.Blue, run.Color);
    }

    [Fact]
    public void Decompose_ColorCode_PreservesClickHoverInsertionFont()
    {
        var click = new ClickEvent(ClickEventAction.RunCommand, "/x");
        var hover = new HoverShowText(Component.Text("tip"));
        var baseStyle = new Style { ClickEvent = click, HoverEvent = hover, Insertion = "ins", Font = "minecraft:alt" };

        List<StyledRun> runs = Decompose("§ccolored", baseStyle);

        StyledRun run = Assert.Single(runs);
        Assert.Equal(click, run.ClickEvent);
        Assert.Equal(hover, run.HoverEvent);
        Assert.Equal("ins", run.Insertion);
        Assert.Equal("minecraft:alt", run.Font);
    }

    [Theory]
    [InlineData("§Cred", true, false)]
    [InlineData("§Lbold", false, true)]
    public void Decompose_CodesAreCaseInsensitive(string text, bool expectColor, bool expectBold)
    {
        List<StyledRun> runs = Decompose(text, Style.Empty);

        StyledRun run = Assert.Single(runs);
        Assert.Equal(expectColor, run.Color is not null);
        Assert.Equal(expectBold, run.Bold);
    }

    [Fact]
    public void Decompose_UnknownCode_SkipsBothCharacters_PerVanilla()
    {
        // An unrecognized pair is consumed and emits nothing. LegacyText.Parse keeps it verbatim; this is the intentional divergence between Decompose and Parse.
        List<StyledRun> runs = Decompose("§znot a code", Style.Empty);

        StyledRun run = Assert.Single(runs);
        Assert.Equal("not a code", run.Text);
    }

    [Fact]
    public void Decompose_HexExtension_ProducesATruecolorRun()
    {
        List<StyledRun> runs = Decompose("§#1e90ffsky", Style.Empty);

        StyledRun run = Assert.Single(runs);
        Assert.Equal("sky", run.Text);
        Assert.Equal(TextColor.FromRgb(0x1E90FF), run.Color);
    }

    [Fact]
    public void Decompose_TrailingSectionSign_IsDropped_PerVanilla()
    {
        List<StyledRun> runs = Decompose("tail§", Style.Empty);

        StyledRun run = Assert.Single(runs);
        Assert.Equal("tail", run.Text);
    }

    [Theory]
    [InlineData("§cred")]
    [InlineData("§lbold text")]
    [InlineData("§a§lgreen bold")]
    [InlineData("§c§lstyled§rplain")]
    public void Decompose_AgreesWithLegacyTextParse_UnderAnEmptyBaseStyle(string text)
    {
        // Under an EMPTY base style the two paths agree: Parse's per-run style and Decompose's accumulator both start from nothing, so a color code's decoration-clearing and a reset's restore-to-base collapse onto the same observable shape (StyledRun's own Color/Bold/Italic getters treat "null" and "explicit false" identically). This is the only condition under which the difference between the two implementations is unobservable.
        Component parsed = LegacyText.Parse(text);
        var parsedRuns = new List<StyledRun>();
        FlattenParsed(parsed, Style.Empty, parsedRuns);

        List<StyledRun> decomposedRuns = Decompose(text, Style.Empty);

        Assert.Equal(parsedRuns.Select(r => r.Text), decomposedRuns.Select(r => r.Text));
        Assert.Equal(parsedRuns.Select(r => r.Color), decomposedRuns.Select(r => r.Color));
        Assert.Equal(parsedRuns.Select(r => r.Bold), decomposedRuns.Select(r => r.Bold));
        Assert.Equal(parsedRuns.Select(r => r.Italic), decomposedRuns.Select(r => r.Italic));
        Assert.Equal(parsedRuns.Select(r => r.Underlined), decomposedRuns.Select(r => r.Underlined));
        Assert.Equal(parsedRuns.Select(r => r.Strikethrough), decomposedRuns.Select(r => r.Strikethrough));
        Assert.Equal(parsedRuns.Select(r => r.Obfuscated), decomposedRuns.Select(r => r.Obfuscated));
    }

    [Fact]
    public void Flatten_LegacyCodesInTranslatedTemplate_AreDecoded()
    {
        var translations = new StubTranslations(new() { ["k"] = "§cRED %s" });
        var component = new Component(new TranslatableContent("k", null, [Component.Text("tail")]));

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(component, ComponentFlattenOptions.For(translations));

        Assert.Equal(2, runs.Count);
        Assert.Equal("RED ", runs[0].Text);
        Assert.Equal(TextColor.Red, runs[0].Color);
        Assert.Equal("tail", runs[1].Text);
        Assert.Null(runs[1].Color);
    }

    [Fact]
    public void Flatten_DecodeLegacyCodesFalse_LeavesThemVerbatim()
    {
        Component component = Component.Text("§cred");
        var options = ComponentFlattenOptions.Default with { DecodeLegacyCodes = false };

        StyledRun run = Assert.Single(ComponentFlattener.Flatten(component, options));

        Assert.Equal("§cred", run.Text);
        Assert.Null(run.Color);
    }
}
