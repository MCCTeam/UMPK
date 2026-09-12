using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Umpk.Text;
using Xunit;

namespace Umpk.Text.Tests;

public sealed class ComponentFlattenerTests
{
    private sealed class StubTranslations : ITranslationSource
    {
        private readonly Dictionary<string, string> _map;

        public StubTranslations(Dictionary<string, string> map) => _map = map;

        public bool TryResolve(string key, [NotNullWhen(true)] out string? template) =>
            _map.TryGetValue(key, out template);
    }

    // A struct sink wrapping a List<string>: the state that survives the walk lives in the list (a reference type), not in the struct's own fields, matching IStyledRunSink's reference-semantic requirement even though the struct itself is copied by value through the recursive walk.
    private readonly struct TextCollectingSink : IStyledRunSink
    {
        private readonly List<string> _texts;

        public TextCollectingSink(List<string> texts) => _texts = texts;

        public void Accept(in StyledRun run) => _texts.Add(run.Text);
    }

    [Fact]
    public void Flatten_LiteralText_YieldsOneRun_WithTheComponentStyle()
    {
        var component = new Component(new TextContent("hello"), new Style { Color = TextColor.Red, Bold = true });

        StyledRun run = Assert.Single(ComponentFlattener.Flatten(component));

        Assert.Equal("hello", run.Text);
        Assert.Equal(TextColor.Red, run.Color);
        Assert.True(run.Bold);
    }

    [Fact]
    public void Flatten_Children_InheritParentStyle()
    {
        var child = new Component(new TextContent("b"), Style.Empty);
        var parent = new Component(new TextContent("a"), new Style { Bold = true }, [child]);

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(parent);

        Assert.Equal(2, runs.Count);
        Assert.Equal("a", runs[0].Text);
        Assert.True(runs[0].Bold);
        Assert.Equal("b", runs[1].Text);
        Assert.True(runs[1].Bold);
    }

    [Fact]
    public void Flatten_ChildOwnStyle_WinsOverParent()
    {
        var child = new Component(new TextContent("b"), new Style { Color = TextColor.Blue });
        var parent = new Component(new TextContent("a"), new Style { Color = TextColor.Red }, [child]);

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(parent);

        Assert.Equal(TextColor.Red, runs[0].Color);
        Assert.Equal(TextColor.Blue, runs[1].Color);
    }

    [Fact]
    public void Flatten_EmptyText_YieldsNoRun()
    {
        Assert.Empty(ComponentFlattener.Flatten(Component.Empty));
    }

    [Fact]
    public void Flatten_Score_YieldsNoRun()
    {
        var component = new Component(new ScoreContent("Steve", "obj"));

        Assert.Empty(ComponentFlattener.Flatten(component));
    }

    [Fact]
    public void Flatten_Selector_YieldsThePattern()
    {
        var component = new Component(new SelectorContent("@a", null));

        StyledRun run = Assert.Single(ComponentFlattener.Flatten(component));

        Assert.Equal("@a", run.Text);
    }

    [Fact]
    public void Flatten_Keybind_YieldsTheKeybindName()
    {
        var component = new Component(new KeybindContent("key.jump"));

        StyledRun run = Assert.Single(ComponentFlattener.Flatten(component));

        Assert.Equal("key.jump", run.Text);
    }

    [Fact]
    public void Flatten_Nbt_YieldsTheNbtPath()
    {
        var component = new Component(new NbtContent("Items", false, null, NbtDataSource.Block, "0 0 0"));

        StyledRun run = Assert.Single(ComponentFlattener.Flatten(component));

        Assert.Equal("Items", run.Text);
    }

    [Fact]
    public void Flatten_Translatable_SubstitutesPositionalArguments()
    {
        var translations = new StubTranslations(new() { ["chat.type.text"] = "<%s> %s" });
        var component = new Component(new TranslatableContent(
            "chat.type.text", null, [Component.Text("Alice"), Component.Text("hi there")]));

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(component, translations);

        Assert.Equal("<Alice> hi there", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void Flatten_Translatable_SubstitutesIndexedArguments()
    {
        var translations = new StubTranslations(new() { ["k"] = "%2$s then %1$s" });
        var component = new Component(new TranslatableContent(
            "k", null, [Component.Text("first"), Component.Text("second")]));

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(component, translations);

        Assert.Equal("second then first", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void Flatten_Translatable_ArgumentKeepsItsOwnStyle_OverTheOuterStyle()
    {
        var translations = new StubTranslations(new() { ["test.one"] = "hi %s" });
        var arg = new Component(new TextContent("Steve"), new Style { Color = TextColor.Yellow });
        var component = new Component(
            new TranslatableContent("test.one", null, [arg]),
            new Style { Color = TextColor.Red });

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(component, translations);

        Assert.Equal(2, runs.Count);
        Assert.Equal("hi ", runs[0].Text);
        Assert.Equal(TextColor.Red, runs[0].Color);
        Assert.Equal("Steve", runs[1].Text);
        Assert.Equal(TextColor.Yellow, runs[1].Color);
    }

    [Fact]
    public void Flatten_Translatable_DoublePercent_EmitsOnePercent()
    {
        var translations = new StubTranslations(new() { ["k"] = "100%% done" });
        var component = new Component(new TranslatableContent("k", null, []));

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(component, translations);

        Assert.Equal("100% done", string.Concat(runs.Select(r => r.Text)));
    }

    [Theory]
    [InlineData("the fallback", "the fallback")]
    [InlineData(null, "unknown.key")]
    public void Flatten_Translatable_UnknownKey_UsesFallbackThenKey(string? fallback, string expected)
    {
        var component = new Component(new TranslatableContent("unknown.key", fallback, []));

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(component);

        Assert.Equal(expected, string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void Flatten_Translatable_UnsupportedConversion_EmitsItVerbatim_DivergingFromVanilla()
    {
        // Vanilla translation-template decomposition throws TranslatableFormatException on an unsupported conversion; we emit '%' plus the char verbatim. Real case: 1.8.9's commands.setworldspawn.success is "Set the world spawn point to (%d, %d, %d)".
        var translations = new StubTranslations(new() { ["k"] = "value: %d" });
        var component = new Component(new TranslatableContent("k", null, [Component.Text("5")]));

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(component, translations);

        Assert.Equal("value: %d", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void Flatten_Translatable_OutOfRangeIndex_EmitsNothingForThatSlot_DivergingFromVanilla()
    {
        // Vanilla getArgument throws when the index is out of range; we emit nothing for that slot.
        var translations = new StubTranslations(new() { ["k"] = "a%sb" });
        var component = new Component(new TranslatableContent("k", null, []));

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(component, translations);

        Assert.Equal("ab", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void Flatten_Translatable_TrailingPercent_EmitsIt()
    {
        // Vanilla throws on a stray trailing percent; we emit it verbatim.
        var translations = new StubTranslations(new() { ["k"] = "done%" });
        var component = new Component(new TranslatableContent("k", null, []));

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(component, translations);

        Assert.Equal("done%", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void Flatten_Translatable_NestedTranslatableArgument_Resolves()
    {
        var translations = new StubTranslations(new() { ["outer"] = "[%s]", ["inner"] = "IN" });
        var component = new Component(new TranslatableContent("outer", null, [Component.Translatable("inner")]));

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(component, translations);

        Assert.Equal("[IN]", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void Flatten_ClickAndHover_AreExposedOnTheRun_AndInherit()
    {
        var click = new ClickEvent(ClickEventAction.RunCommand, "/help");
        var hover = new HoverShowText(Component.Text("tip"));
        var child = new Component(new TextContent("b"), Style.Empty);
        var parent = new Component(
            new TextContent("a"),
            new Style { ClickEvent = click, HoverEvent = hover },
            [child]);

        IReadOnlyList<StyledRun> runs = ComponentFlattener.Flatten(parent);

        Assert.Equal(click, runs[0].ClickEvent);
        Assert.Equal(hover, runs[0].HoverEvent);
        Assert.Equal(click, runs[1].ClickEvent);
        Assert.Equal(hover, runs[1].HoverEvent);
    }

    [Fact]
    public void Flatten_ResolvedStyle_CarriesFontAndInsertion()
    {
        var component = new Component(new TextContent("x"), new Style { Font = "minecraft:alt", Insertion = "paste me" });

        StyledRun run = Assert.Single(ComponentFlattener.Flatten(component));

        Assert.Equal("minecraft:alt", run.Font);
        Assert.Equal("paste me", run.Insertion);
    }

    [Fact]
    public void Flatten_BaseStyleOption_SeedsTheChain()
    {
        var component = new Component(new TextContent("x"), Style.Empty);
        var options = ComponentFlattenOptions.Default with { BaseStyle = new Style { Color = TextColor.Aqua } };

        StyledRun run = Assert.Single(ComponentFlattener.Flatten(component, options));

        Assert.Equal(TextColor.Aqua, run.Color);
    }

    [Fact]
    public void Flatten_StructSink_ReceivesEveryRun()
    {
        var texts = new List<string>();
        var sink = new TextCollectingSink(texts);
        var child = new Component(new TextContent("b"), Style.Empty);
        var parent = new Component(new TextContent("a"), Style.Empty, [child]);

        ComponentFlattener.Flatten(parent, sink, ComponentFlattenOptions.Default);

        Assert.Equal(["a", "b"], texts);
    }

    [Fact]
    public void Flatten_ListOverload_MatchesTheSinkOverload()
    {
        var component = new Component(
            new TextContent("a"),
            new Style { Bold = true },
            [new Component(new TextContent("b"), Style.Empty)]);

        var texts = new List<string>();
        ComponentFlattener.Flatten(component, new TextCollectingSink(texts), ComponentFlattenOptions.Default);

        IReadOnlyList<StyledRun> viaList = ComponentFlattener.Flatten(component, ComponentFlattenOptions.Default);

        Assert.Equal(texts, viaList.Select(r => r.Text));
    }
}
