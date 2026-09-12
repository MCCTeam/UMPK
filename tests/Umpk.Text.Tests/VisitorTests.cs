using Umpk.Text;
using Xunit;

namespace Umpk.Text.Tests;

public sealed class VisitorTests
{
    private sealed class TypeNameVisitor : IComponentContentVisitor<string>
    {
        public string VisitText(TextContent content) => "text:" + content.Text;

        public string VisitTranslatable(TranslatableContent content) => "translate:" + content.Key;

        public string VisitScore(ScoreContent content) => "score:" + content.Objective;

        public string VisitSelector(SelectorContent content) => "selector:" + content.Pattern;

        public string VisitKeybind(KeybindContent content) => "keybind:" + content.Keybind;

        public string VisitNbt(NbtContent content) => "nbt:" + content.NbtPath;
    }

    [Fact]
    public void VisitorDispatchesEachContentKind()
    {
        var visitor = new TypeNameVisitor();
        Assert.Equal("text:hi", new TextContent("hi").Accept(visitor));
        Assert.Equal("translate:k", new TranslatableContent("k", null, []).Accept(visitor));
        Assert.Equal("score:obj", new ScoreContent("n", "obj").Accept(visitor));
        Assert.Equal("selector:@a", new SelectorContent("@a", null).Accept(visitor));
        Assert.Equal("keybind:key.jump", new KeybindContent("key.jump").Accept(visitor));
        Assert.Equal("nbt:Path", new NbtContent("Path", false, null, NbtDataSource.Block, "0 0 0").Accept(visitor));
    }

    [Fact]
    public void VisitorNullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new TextContent("x").Accept<string>(null!));
    }

    [Fact]
    public void TypeNamesMatchVanilla()
    {
        Assert.Equal("text", new TextContent("x").TypeName);
        Assert.Equal("translatable", new TranslatableContent("k", null, []).TypeName);
        Assert.Equal("score", new ScoreContent("n", "o").TypeName);
        Assert.Equal("selector", new SelectorContent("@a", null).TypeName);
        Assert.Equal("keybind", new KeybindContent("k").TypeName);
        Assert.Equal("nbt", new NbtContent("p", false, null, NbtDataSource.Block, "0 0 0").TypeName);
    }
}
