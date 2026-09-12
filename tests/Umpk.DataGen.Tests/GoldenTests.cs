using System.Reflection;
using Xunit;

namespace Umpk.DataGen.Tests;

/// <summary>Golden-text emission tests. The expected.cs text lives under Fixtures/golden/ and is compared byte-for-byte against emitter output for the baseline dataset. Regenerate the golden files with the UMPK_UPDATE_GOLDEN environment variable set, then compare the resulting output.</summary>
public sealed class GoldenTests
{
    [Theory]
    [InlineData("JavaVersions.g.cs")]
    [InlineData("V770/Descriptor.g.cs")]
    [InlineData("V47/Descriptor.g.cs")]
    public void Emission_matches_golden(string generatedPath)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        string actual = Normalize(emitter.Emit()[generatedPath].Text);

        string goldenPath = GoldenPath(generatedPath);
        if (Environment.GetEnvironmentVariable("UMPK_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, actual);
        }

        Assert.True(File.Exists(goldenPath), $"golden fixture missing: {goldenPath} (set UMPK_UPDATE_GOLDEN=1 to create)");
        string expected = Normalize(File.ReadAllText(goldenPath));
        Assert.Equal(expected, actual);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string GoldenPath(string generatedPath)
    {
        // Fixtures/ is copied next to the test assembly (see csproj). When updating goldens, also write back to the source tree so the fixture can be updated.
        string outDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string relative = Path.Combine("Fixtures", "golden", generatedPath.Replace('/', Path.DirectorySeparatorChar));
        if (Environment.GetEnvironmentVariable("UMPK_UPDATE_GOLDEN") == "1")
        {
            // Prefer writing to the source project dir when it can be located.
            DirectoryInfo? cursor = new(outDir);
            while (cursor is not null && !File.Exists(Path.Combine(cursor.FullName, "Umpk.DataGen.Tests.csproj")))
                cursor = cursor.Parent;

            if (cursor is not null)
                return Path.Combine(cursor.FullName, relative);

        }
        return Path.Combine(outDir, relative);
    }
}
