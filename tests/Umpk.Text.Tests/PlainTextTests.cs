using System.Diagnostics.CodeAnalysis;
using Umpk.Text;
using Xunit;

namespace Umpk.Text.Tests;

public sealed class PlainTextTests
{
    private sealed class StubTranslations : ITranslationSource
    {
        private readonly Dictionary<string, string> _map;

        public StubTranslations(Dictionary<string, string> map) => _map = map;

        public bool TryResolve(string key, [NotNullWhen(true)] out string? template) =>
            _map.TryGetValue(key, out template);
    }

    [Fact]
    public void PlainText_FlattensChildren()
    {
        var component = new Component(
            new TextContent("Hello, "),
            Style.Empty,
            [Component.Text("world"), Component.Text("!")]);
        Assert.Equal("Hello, world!", component.ToPlainText());
    }

    [Fact]
    public void PlainText_NullSourceUsesKey()
    {
        Component c = Component.Translatable("some.key");
        Assert.Equal("some.key", c.ToPlainText());
    }

    [Fact]
    public void PlainText_NullSourceUsesFallback()
    {
        var c = new Component(new TranslatableContent("unknown.key", "the fallback", []));
        Assert.Equal("the fallback", c.ToPlainText());
    }

    [Fact]
    public void PlainText_NullTranslationSourceReturnsKey()
    {
        Component c = Component.Translatable("x.y");
        Assert.Equal("x.y", c.ToPlainText(NullTranslationSource.Instance));
    }

    [Fact]
    public void PlainText_ResolvesAutoPositionalArgs()
    {
        var translations = new StubTranslations(new() { ["chat.type.text"] = "<%s> %s" });
        var c = new Component(new TranslatableContent(
            "chat.type.text",
            null,
            [Component.Text("Alice"), Component.Text("hi there")]));
        Assert.Equal("<Alice> hi there", c.ToPlainText(translations));
    }

    [Fact]
    public void PlainText_ResolvesExplicitPositionalArgs()
    {
        var translations = new StubTranslations(new() { ["k"] = "%2$s then %1$s" });
        var c = new Component(new TranslatableContent(
            "k",
            null,
            [Component.Text("first"), Component.Text("second")]));
        Assert.Equal("second then first", c.ToPlainText(translations));
    }

    [Fact]
    public void PlainText_HandlesLiteralPercent()
    {
        var translations = new StubTranslations(new() { ["k"] = "100%% done: %s" });
        var c = new Component(new TranslatableContent("k", null, [Component.Text("ok")]));
        Assert.Equal("100% done: ok", c.ToPlainText(translations));
    }

    [Fact]
    public void PlainText_MissingArgYieldsEmpty()
    {
        var translations = new StubTranslations(new() { ["k"] = "a%sb" });
        var c = new Component(new TranslatableContent("k", null, []));
        Assert.Equal("ab", c.ToPlainText(translations));
    }

    [Fact]
    public void PlainText_NestedTranslatableArgResolves()
    {
        var translations = new StubTranslations(new()
        {
            ["outer"] = "[%s]",
            ["inner"] = "IN",
        });
        var c = new Component(new TranslatableContent(
            "outer",
            null,
            [Component.Translatable("inner")]));
        Assert.Equal("[IN]", c.ToPlainText(translations));
    }

    [Fact]
    public void PlainText_SelectorKeybindNbtUseRawText()
    {
        Assert.Equal("@a", new Component(new SelectorContent("@a", null)).ToPlainText());
        Assert.Equal("key.jump", new Component(new KeybindContent("key.jump")).ToPlainText());
        Assert.Equal(
            "Items",
            new Component(new NbtContent("Items", false, null, NbtDataSource.Block, "0 0 0")).ToPlainText());
    }

    [Fact]
    public void ToPlainText_DoesNotDecodeLegacyCodes_UnlikeTheFlattener()
    {
        // ToPlainText is Flatten with DecodeLegacyCodes = false, so section-sign codes stay verbatim in the output rather than being decoded (or stripped) the way ComponentFlattener's default options would.
        Component component = Component.Text("§cred");
        Assert.Equal("§cred", component.ToPlainText());
    }
}
