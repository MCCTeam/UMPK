using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Data.Java.Tests.Generated;

/// <summary>Every published Minecraft Java release name in the supported range must resolve to its wire protocol, including names that share a protocol with another release: 1.8.1-1.8.9 (all protocol 47, exactly like the accepted 1.8), 1.9.3, 1.10.1, 1.10.2 and 1.11.1.</summary>
/// <remarks>The (name, protocol) pairs below are verified against published protocol metadata and server status responses rather than inferred from neighboring releases.</remarks>
public class PublishedVersionNameTests
{
    /// <summary>Every published release name in the supported range, with its wire protocol.</summary>
    public static TheoryData<string, int> PublishedNames
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach ((string name, int protocol) in Names)
            {
                data.Add(name, protocol);
            }

            return data;
        }
    }

    private static readonly (string Name, int Protocol)[] Names =
    [
        ("1.8", 47), ("1.8.1", 47), ("1.8.2", 47), ("1.8.3", 47), ("1.8.4", 47),
        ("1.8.5", 47), ("1.8.6", 47), ("1.8.7", 47), ("1.8.8", 47), ("1.8.9", 47),
        ("1.9", 107), ("1.9.1", 108), ("1.9.2", 109), ("1.9.3", 110), ("1.9.4", 110),
        ("1.10", 210), ("1.10.1", 210), ("1.10.2", 210),
        ("1.11", 315), ("1.11.1", 316), ("1.11.2", 316),
        ("1.12", 335), ("1.12.1", 338), ("1.12.2", 340),
        ("1.13", 393), ("1.13.1", 401), ("1.13.2", 404),
        ("1.14", 477), ("1.14.1", 480), ("1.14.2", 485), ("1.14.3", 490), ("1.14.4", 498),
        ("1.15", 573), ("1.15.1", 575), ("1.15.2", 578),
        ("1.16", 735), ("1.16.1", 736), ("1.16.2", 751), ("1.16.3", 753), ("1.16.4", 754), ("1.16.5", 754),
        ("1.17", 755), ("1.17.1", 756),
        ("1.18", 757), ("1.18.1", 757), ("1.18.2", 758),
        ("1.19", 759), ("1.19.1", 760), ("1.19.2", 760), ("1.19.3", 761), ("1.19.4", 762),
        ("1.20", 763), ("1.20.1", 763), ("1.20.2", 764), ("1.20.3", 765), ("1.20.4", 765),
        ("1.20.5", 766), ("1.20.6", 766),
        ("1.21", 767), ("1.21.1", 767), ("1.21.2", 768), ("1.21.3", 768), ("1.21.4", 769),
        ("1.21.5", 770), ("1.21.6", 771), ("1.21.7", 772), ("1.21.8", 772),
        ("1.21.9", 773), ("1.21.10", 773), ("1.21.11", 774),
        ("26.1", 775), ("26.2", 776),
    ];

    [Theory]
    [MemberData(nameof(PublishedNames))]
    public void EveryPublishedName_ResolvesItsProtocol(string name, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(name, out JavaVersion version), $"{name} must resolve");
        Assert.Equal(protocol, version.Version.Protocol);
    }

    /// <summary>The name table and the protocol catalog cover the same set: every supported protocol has at least one published name, and no name names a protocol the catalog does not carry. That is what turns this from a list of thirteen special cases into a closed audit.</summary>
    [Fact]
    public void TheNameTableAndTheProtocolCatalog_CoverTheSameProtocols()
    {
        HashSet<int> named = [.. Names.Select(static n => n.Protocol)];
        HashSet<int> supported = [.. JavaVersions.All.Select(static v => v.Version.Protocol)];

        Assert.Equal(supported, named);
    }

    /// <summary>A version string that names no shipped release must stay unknown.</summary>
    [Theory]
    [InlineData("1.7.10")]
    [InlineData("1.8.10")]
    [InlineData("1.9.5")]
    [InlineData("1.10.3")]
    [InlineData("1.11.3")]
    [InlineData("1.21.12")]
    [InlineData("26.3")]
    [InlineData("")]
    public void AnUnpublishedName_DoesNotResolve(string name) =>
        Assert.False(JavaVersions.TryGetByName(name, out _), $"{name} is not a published release in range");

    /// <summary>Names sharing a protocol resolve the SAME descriptor instance, which is what makes 13 extra names free: they add table arms, not versions.</summary>
    [Fact]
    public void AliasNames_ResolveTheSameDescriptor()
    {
        Assert.True(JavaVersions.TryGetByName("1.8", out JavaVersion v18));
        Assert.True(JavaVersions.TryGetByName("1.8.9", out JavaVersion v189));
        Assert.Same(v18, v189);

        Assert.True(JavaVersions.TryGetByName("1.10", out JavaVersion v110));
        Assert.True(JavaVersions.TryGetByName("1.10.2", out JavaVersion v1102));
        Assert.Same(v110, v1102);
    }
}
