using Xunit;

namespace Umpk.Realms.Tests;

/// <summary>Pins <see cref="RealmWorld.Match"/>: an id pass over the whole list, then a case-insensitive name pass, with a numeric selector only taking the id path when it parses under <c>NumberStyles.None</c> (bare digits, no sign or surrounding whitespace). Keeping the rule in a value-level helper makes it testable without a resolver or fake client.</summary>
public sealed class RealmWorldMatchTests
{
    private static RealmWorld NewWorld(long id, string name) => new(
        id, name, Motd: "", Owner: "", OwnerUuid: null, RealmState.Open, WorldType: "NORMAL",
        Expired: false, ExpiredTrial: false, DaysLeft: 0, MaxPlayers: 10, ActiveSlot: null, Member: false);

    [Fact]
    public void ByExactId()
    {
        RealmWorld alpha = NewWorld(1234, "Alpha");
        RealmWorld beta = NewWorld(5678, "Beta");

        RealmWorld? match = RealmWorld.Match([alpha, beta], "1234");

        Assert.Same(alpha, match);
    }

    [Fact]
    public void ByName_CaseInsensitive()
    {
        RealmWorld world = NewWorld(1, "MyRealm");

        RealmWorld? match = RealmWorld.Match([world], "MYREALM");

        Assert.Same(world, match);
    }

    [Fact]
    public void Match_PrefersIdOverName()
    {
        // A world named "5678" must lose to a different world whose actual Id is 5678: the id pass runs over the whole list before the name pass ever starts.
        RealmWorld namedLikeAnId = NewWorld(1111, "5678");
        RealmWorld actualId = NewWorld(5678, "Other");

        RealmWorld? match = RealmWorld.Match([namedLikeAnId, actualId], "5678");

        Assert.Same(actualId, match);
    }

    [Theory]
    [InlineData(" 1234", 1234L)]
    [InlineData("-1", -1L)]
    [InlineData("+1234", 1234L)]
    public void NonCanonicalNumericSelector_FallsBackToName(string selector, long decoyId)
    {
        // decoyId is the value the selector would parse to if leading whitespace/sign were tolerated. NumberStyles.None tolerates neither, so the id pass must not match the decoy, and the selector must instead resolve through the name pass to the world literally named after it.
        RealmWorld decoy = NewWorld(decoyId, "Decoy");
        RealmWorld target = NewWorld(decoyId + 1_000_000, selector);

        RealmWorld? match = RealmWorld.Match([decoy, target], selector);

        Assert.Same(target, match);
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        RealmWorld world = NewWorld(1, "Alpha");

        RealmWorld? match = RealmWorld.Match([world], "nonexistent");

        Assert.Null(match);
    }

    [Fact]
    public void EmptySelector_Throws()
    {
        RealmWorld world = NewWorld(1, "Alpha");

        Assert.Throws<ArgumentException>(() => RealmWorld.Match([world], ""));
    }

    [Fact]
    public void NullWorlds_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RealmWorld.Match(null!, "Alpha"));
    }
}
