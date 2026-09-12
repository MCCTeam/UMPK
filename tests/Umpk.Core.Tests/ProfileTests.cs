using Xunit;

namespace Umpk.Tests;

public class ProfileTests
{
    [Fact]
    public void ProfileCredentials_ToString_RedactsAccessToken()
    {
        var profile = new GameProfile(Guid.NewGuid(), "Steve");
        var credentials = new ProfileCredentials(profile, "super-secret-token");
        string printed = credentials.ToString();
        Assert.DoesNotContain("super-secret-token", printed, StringComparison.Ordinal);
        Assert.Contains("Steve", printed, StringComparison.Ordinal);
        Assert.Contains("<redacted>", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void GameProfile_Properties_DefaultEmpty()
    {
        var profile = new GameProfile(Guid.Empty, "Alex");
        Assert.Empty(profile.Properties);
    }

    [Fact]
    public void GameVersion_CarriesEdition()
    {
        var version = new GameVersion(GameEdition.Java, "1.21.5", 770);
        Assert.Equal(GameEdition.Java, version.Edition);
        Assert.Contains("1.21.5", version.ToString(), StringComparison.Ordinal);
    }
}
