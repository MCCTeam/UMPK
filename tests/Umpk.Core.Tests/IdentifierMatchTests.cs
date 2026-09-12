using Xunit;

namespace Umpk.Tests;

/// <summary>The shared identifier-matching contract. A bare needle is deliberately scoped to the <c>minecraft</c> namespace: "chest" must never match "mod:chest".</summary>
public sealed class IdentifierMatchTests
{
    [Fact]
    public void Matches_FullNamespacedForm_IsAccepted()
    {
        var id = Identifier.Minecraft("chest");
        Assert.True(IdentifierMatch.Matches(id, "minecraft:chest"));
    }

    [Fact]
    public void Matches_BarePath_MatchesInTheMinecraftNamespace()
    {
        var id = Identifier.Minecraft("chest");
        Assert.True(IdentifierMatch.Matches(id, "chest"));
    }

    [Fact]
    public void Matches_BarePath_IsRejectedAgainstAForeignNamespace()
    {
        var id = new Identifier("mod", "chest");
        Assert.False(IdentifierMatch.Matches(id, "chest"));
    }

    [Fact]
    public void Matches_ForeignNamespace_IsAcceptedWhenTypedInFull()
    {
        var id = new Identifier("mod", "chest");
        Assert.True(IdentifierMatch.Matches(id, "mod:chest"));
    }

    [Theory]
    [InlineData("Minecraft:Chest")]
    [InlineData("CHEST")]
    public void Matches_IsCaseInsensitive(string needle)
    {
        var id = Identifier.Minecraft("chest");
        Assert.True(IdentifierMatch.Matches(id, needle));
    }

    [Fact]
    public void Matches_TrimsTheNeedle()
    {
        var id = Identifier.Minecraft("chest");
        Assert.True(IdentifierMatch.Matches(id, "  chest  "));
        Assert.True(IdentifierMatch.Matches(id, "  minecraft:chest  "));
    }

    [Fact]
    public void BarePath_SplitsOnFirstColonOnly()
    {
        Assert.Equal("chest", IdentifierMatch.BarePath("chest"));
        Assert.Equal("chest", IdentifierMatch.BarePath("minecraft:chest"));
        Assert.Equal("entity:zombie", IdentifierMatch.BarePath("mod:entity:zombie"));
        Assert.Equal("chest", IdentifierMatch.BarePath("  chest  "));
    }

    [Fact]
    public void Matches_EmptyOrWhitespaceNeedle_IsRejected_NotThrown()
    {
        var id = Identifier.Minecraft("chest");
        Assert.False(IdentifierMatch.Matches(id, string.Empty));
        Assert.False(IdentifierMatch.Matches(id, "   "));
    }
}
