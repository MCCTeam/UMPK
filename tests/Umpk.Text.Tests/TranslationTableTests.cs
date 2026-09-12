using Umpk.Text;
using Xunit;

namespace Umpk.Text.Tests;

public sealed class TranslationTableTests
{
    [Fact]
    public void FromEntries_ResolvesEveryEntry()
    {
        TranslationTable table = TranslationTable.FromEntries(
        [
            new("chat.type.text", "<%s> %s"),
            new("gameMode.changed", "Your game mode has been updated to %s"),
        ]);

        Assert.Equal(2, table.Count);
        Assert.True(table.TryResolve("chat.type.text", out string? a));
        Assert.Equal("<%s> %s", a);
        Assert.True(table.TryResolve("gameMode.changed", out string? b));
        Assert.Equal("Your game mode has been updated to %s", b);
    }

    [Fact]
    public void FromEntries_IsOrdinal_NotCultureSensitive()
    {
        // "SS" vs "ss": an ordinal comparer treats these as distinct keys. A culture-sensitive comparer (Turkish "I" casing rules, for one) can collapse or split keys that ordinal comparison keeps apart, which would silently merge or lose translation entries.
        TranslationTable table = TranslationTable.FromEntries(
        [
            new("key.SS", "upper"),
            new("key.ss", "lower"),
        ]);

        Assert.Equal(2, table.Count);
        Assert.True(table.TryResolve("key.SS", out string? upper));
        Assert.Equal("upper", upper);
        Assert.True(table.TryResolve("key.ss", out string? lower));
        Assert.Equal("lower", lower);
        Assert.False(table.TryResolve("key.Ss", out _));
    }

    [Fact]
    public void Empty_ResolvesNothing_AndCountIsZero()
    {
        Assert.Equal(0, TranslationTable.Empty.Count);
        Assert.False(TranslationTable.Empty.TryResolve("chat.type.text", out string? template));
        Assert.Null(template);
    }

    [Fact]
    public void FromEntries_LastDuplicateWins()
    {
        TranslationTable table = TranslationTable.FromEntries(
        [
            new("gameMode.changed", "first"),
            new("gameMode.changed", "second"),
            new("gameMode.changed", "third"),
        ]);

        Assert.Equal(1, table.Count);
        Assert.True(table.TryResolve("gameMode.changed", out string? template));
        Assert.Equal("third", template);
    }
}
