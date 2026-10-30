using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class FixturePathsTests
{
    [Theory]
    [InlineData("fixtures")]
    [InlineData("Fixtures")]
    public void CopiedFixturesDoNotHideTheRepositoryRoot(string fixtureDirectory)
    {
        string root = Directory.CreateTempSubdirectory("umpk-fixture-paths-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "UMPK.sln"), "");
            string output = Path.Combine(root, "tests", "Example.Tests", "bin", "Release", "net10.0");
            Directory.CreateDirectory(Path.Combine(output, fixtureDirectory));

            Assert.Equal(root, FixturePaths.RepoRoot(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
