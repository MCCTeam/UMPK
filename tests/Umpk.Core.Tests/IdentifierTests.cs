using Xunit;

namespace Umpk.Tests;

public class IdentifierTests
{
    [Theory]
    [InlineData("stone", "minecraft", "stone")]
    [InlineData("minecraft:stone", "minecraft", "stone")]
    [InlineData(":stone", "minecraft", "stone")]
    [InlineData("mymod:custom_block", "mymod", "custom_block")]
    [InlineData("minecraft:entity/zombie", "minecraft", "entity/zombie")]
    [InlineData("a-b.c_d:e/f-g.h_1", "a-b.c_d", "e/f-g.h_1")]
    public void Parse_AcceptsValidForms(string input, string expectedNamespace, string expectedPath)
    {
        var id = Identifier.Parse(input);
        Assert.Equal(expectedNamespace, id.Namespace);
        Assert.Equal(expectedPath, id.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData(":")]
    [InlineData("minecraft:")]
    [InlineData("Minecraft:stone")]
    [InlineData("minecraft:Stone")]
    [InlineData("minecraft:sto ne")]
    [InlineData("mine/craft:stone")]
    [InlineData("minecraft:sto:ne")]
    [InlineData("minecraft:stone!")]
    public void Parse_RejectsInvalidForms(string input)
    {
        Assert.Throws<FormatException>(() => Identifier.Parse(input));
        Assert.False(Identifier.TryParse(input, out _));
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        Assert.False(Identifier.TryParse(null, out _));
    }

    [Fact]
    public void Constructor_ValidatesParts()
    {
        Assert.Throws<FormatException>(() => new Identifier("UPPER", "path"));
        Assert.Throws<FormatException>(() => new Identifier("ns", "in valid"));
        Assert.Throws<FormatException>(() => new Identifier("ns/slash", "path"));
    }

    [Fact]
    public void ToString_IsNamespaceColonPath()
    {
        Assert.Equal("minecraft:stone", Identifier.Parse("stone").ToString());
        Assert.Equal("mymod:thing", new Identifier("mymod", "thing").ToString());
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        Assert.Equal(Identifier.Parse("stone"), Identifier.Parse("minecraft:stone"));
        Assert.True(Identifier.Parse("stone") == Identifier.Minecraft("stone"));
        Assert.NotEqual(Identifier.Parse("a:x"), Identifier.Parse("b:x"));
        Assert.Equal(
            Identifier.Parse("stone").GetHashCode(),
            Identifier.Parse("minecraft:stone").GetHashCode());
    }

    [Fact]
    public void IsMinecraft_TracksNamespace()
    {
        Assert.True(Identifier.Parse("stone").IsMinecraft);
        Assert.False(Identifier.Parse("forge:stone").IsMinecraft);
    }

    [Fact]
    public void DefaultInstance_HasMinecraftNamespaceAndEmptyPath()
    {
        Identifier id = default;
        Assert.Equal("minecraft", id.Namespace);
        Assert.Equal("", id.Path);
    }
}
