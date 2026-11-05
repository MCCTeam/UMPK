using Umpk.Client.Navigation;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ModernHardCollisionClassificationTests
{
    private static readonly string[] ShadowedNames = ["fireball", "guardian", "item", "item_frame", "minecart", "spider", "squid", "wind_charge"];

    public static IEnumerable<object[]> LaterMatches()
    {
        foreach (int protocol in new[] { 769, 770, 777 })
            foreach (string name in ShadowedNames)
                yield return [protocol, name];
    }

    [Theory]
    [MemberData(nameof(LaterMatches))]
    public void AnEarlierLongerNameDoesNotHideTheExactLaterTarget(int protocol, string name)
        => Assert.True(ModernHardCollisionProfile.HasNoHardPeerBox(Identifier.Minecraft(name), protocol));

    public static IEnumerable<object[]> PartialNames()
    {
        foreach (int protocol in new[] { 770, 777 })
            foreach (string name in ShadowedNames)
                foreach (string unknown in new[] { "prefix_" + name, name + "_suffix", name[1..], name[..^1] })
                    yield return [protocol, unknown];
    }

    [Theory]
    [MemberData(nameof(PartialNames))]
    public void ASharedSubstringDoesNotClassifyAnUnknownTarget(int protocol, string name)
        => Assert.False(ModernHardCollisionProfile.HasNoHardPeerBox(Identifier.Minecraft(name), protocol));

    public static IEnumerable<object[]> CustomNamespaces()
    {
        foreach (int protocol in new[] { 770, 777 })
            foreach (string name in ShadowedNames)
                yield return [protocol, name];
    }

    [Theory]
    [MemberData(nameof(CustomNamespaces))]
    public void ACustomNamespaceDoesNotInheritTheVanillaDisposition(int protocol, string name)
        => Assert.False(ModernHardCollisionProfile.HasNoHardPeerBox(new Identifier("custom", name), protocol));

    public static IEnumerable<object[]> ExceptionalBoundaries()
    {
        foreach ((string name, int first) in new (string, int)[]
        {
            ("copper_golem", 773),
            ("mannequin", 773),
            ("camel_husk", 774),
            ("nautilus", 774),
            ("parched", 774),
            ("zombie_nautilus", 774),
            ("sulfur_cube", 776),
            ("cushion", 777),
        })
        {
            yield return [first - 1, name, false];
            yield return [first, name, true];
        }
    }

    [Theory]
    [MemberData(nameof(ExceptionalBoundaries))]
    public void LaterTypesKeepTheirOriginalIntroductionBoundary(int protocol, string name, bool expected)
        => Assert.Equal(expected, ModernHardCollisionProfile.HasNoHardPeerBox(Identifier.Minecraft(name), protocol));

    [Theory]
    [InlineData(int.MinValue, "cow")]
    [InlineData(0, "cow")]
    [InlineData(int.MaxValue, "cow")]
    [InlineData(int.MinValue, "squid")]
    [InlineData(0, "squid")]
    [InlineData(int.MaxValue, "squid")]
    public void TheExistingCommonPredicateDoesNotAddACallerProtocolGate(int protocol, string name)
        => Assert.True(ModernHardCollisionProfile.HasNoHardPeerBox(Identifier.Minecraft(name), protocol));

    [Theory]
    [InlineData(769, false)]
    [InlineData(770, true)]
    [InlineData(777, true)]
    [InlineData(778, false)]
    public void TheOwningWorldAdapterKeepsItsExistingSupportedBand(int protocol, bool expected)
        => Assert.Equal(expected, ModernHardCollisionProfile.Supports(protocol));

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(777)]
    [InlineData(int.MaxValue)]
    public void ADefaultIdentifierNeverBecomesAnEmptyNameMatch(int protocol)
        => Assert.False(ModernHardCollisionProfile.HasNoHardPeerBox(default, protocol));

    [Theory]
    [InlineData(770)]
    [InlineData(777)]
    public void ParsedVanillaNamespaceUsesOrdinalContentEquality(int protocol)
    {
        Assert.True(Identifier.TryParse(new string("minecraft:item_frame".ToCharArray()), out Identifier type));
        Assert.True(ModernHardCollisionProfile.HasNoHardPeerBox(type, protocol));
    }

    [Theory]
    [InlineData(770, "oak_boat")]
    [InlineData(777, "oak_boat")]
    [InlineData(770, "bamboo_raft")]
    [InlineData(777, "bamboo_raft")]
    [InlineData(770, "shulker")]
    [InlineData(777, "shulker")]
    [InlineData(770, "happy_ghast")]
    [InlineData(777, "happy_ghast")]
    [InlineData(770, "poplar_boat")]
    [InlineData(777, "poplar_boat")]
    public void HardOrContextualTargetsRemainOutsideTheProvenFalseSet(int protocol, string name)
        => Assert.False(ModernHardCollisionProfile.HasNoHardPeerBox(Identifier.Minecraft(name), protocol));
}
