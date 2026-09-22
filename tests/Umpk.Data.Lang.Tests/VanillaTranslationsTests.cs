using Umpk.Text;
using Xunit;

namespace Umpk.Data.Lang.Tests;

/// <summary>Pins <see cref="VanillaTranslations"/> to representative protocol-specific values. The rows cover wording, placeholder, and key-shape changes that prevent one shared translation table.</summary>
public sealed class VanillaTranslationsTests
{
    public static TheoryData<int, string, string> LiteralRows => new()
    {
        // 1.9 added an argument to gameMode.changed that 1.8 never had.
        { 47, "gameMode.changed", "Your game mode has been updated" },
        { 110, "gameMode.changed", "Your game mode has been updated to %s" },
        { 47, "tile.bed.noSleep", "You can only sleep at night" },
        { 340, "tile.bed.tooFarAway", "You may not rest now, the bed is too far away" },
        // 26.2 added a dimension argument (and brackets) that 1.13 never had.
        { 393, "commands.spawnpoint.success.single", "Set spawn point to %s, %s, %s for %s" },
        { 776, "commands.spawnpoint.success.single", "Set spawn point to %s, %s, %s [%s] in %s for %s" },
        // 1.14.4 added a "%s%%" progress suffix that 1.13 never had.
        { 393, "menu.preparingSpawn", "Preparing spawn area" },
        { 498, "menu.preparingSpawn", "Preparing spawn area: %s%%" },
        // %d (not %s), which the format-parse check must accept without requiring the s conversion.
        { 47, "commands.setworldspawn.success", "Set the world spawn point to (%d, %d, %d)" },
        { 766, "chat.type.text", "<%s> %s" },
        { 47, "death.attack.player", "%1$s was slain by %2$s" },
    };

    [Theory]
    [MemberData(nameof(LiteralRows))]
    public void ForProtocol_ResolvesTheWireLayoutOwnTemplate(int protocol, string key, string expected)
    {
        ITranslationSource table = VanillaTranslations.ForProtocol(protocol);
        Assert.True(table.TryResolve(key, out string? actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ForProtocol_UnknownProtocol_ReturnsEmpty()
    {
        ITranslationSource source = VanillaTranslations.ForProtocol(-1);
        Assert.False(source.TryResolve("chat.type.text", out string? template));
        Assert.Null(template);
    }

    [Fact]
    public void Protocols_CoversEveryCatalogProtocol()
    {
        int[] expected =
        [
            47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404, 477, 480, 485, 490,
            498, 573, 575, 578, 735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763,
            764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777,
        ];
        Assert.Equal(expected, VanillaTranslations.Protocols.ToArray());
    }

    [Fact]
    public void Latest_IsTheNewestCatalogProtocol()
    {
        Assert.Same(VanillaTranslations.ForProtocol(777), VanillaTranslations.Latest);
    }

    public static TheoryData<int, int> CountRows => new()
    {
        { 47, 2553 },
        { 340, 3303 },
        { 393, 3907 },
        { 776, 8123 },
        { 777, 8559 },
    };

    [Theory]
    [MemberData(nameof(CountRows))]
    public void CountFor_MatchesTheShippedTableSize(int protocol, int expected)
    {
        Assert.Equal(expected, VanillaTranslations.CountFor(protocol));
    }

    [Fact]
    public void ForProtocol_IsCached()
    {
        ITranslationSource a = VanillaTranslations.ForProtocol(770);
        ITranslationSource b = VanillaTranslations.ForProtocol(770);
        Assert.Same(a, b);
    }

    [Fact]
    public void LegacyProtocol_DoesNotResolveAModernKey()
    {
        // block.minecraft.stone is the 1.13-flattening block-name key shape; 1.8's own table names blocks under tile.*, not block.minecraft.*, so this key does not exist on it at all.
        ITranslationSource legacy = VanillaTranslations.ForProtocol(47);
        Assert.False(legacy.TryResolve("block.minecraft.stone", out string? template));
        Assert.Null(template);
    }

    [Fact]
    public void ModernProtocol_DoesNotResolveALegacyKey()
    {
        // item.diamond.name is the pre-flattening item-name key shape; it was replaced by item.minecraft.diamond at 1.13 and dropped from the table entirely afterward.
        ITranslationSource modern = VanillaTranslations.ForProtocol(776);
        Assert.False(modern.TryResolve("item.diamond.name", out string? template));
        Assert.Null(template);
    }
}
