using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>Checks that every structured comment reference points to an available tree and optional class.</summary>
/// <remarks>
/// <para>The tests check only that each reference resolves. They do not validate the referenced content. The same resolver supports the repository's command-line lookup tool.</para>
/// </remarks>
public sealed class VanillaCitationTests
{
    [Fact]
    public void Resolve_UsesAPathOnlyInsideTheNamedTree()
    {
        string root = Directory.CreateTempSubdirectory("umpk-citation-oracle").FullName;
        try
        {
            string tree = Path.Combine(root, "1.99-decompiled");
            string expected = Path.Combine(tree, "net", "minecraft", "Proof.java");
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
            File.WriteAllText(expected, "class Proof {}");
            File.WriteAllText(Path.Combine(root, "Outside.java"), "class Outside {}");

            var index = new Dictionary<string, ILookup<string, string>>();
            Assert.Equal(expected, VanillaCitations.Resolve(root, new("Proof.cs", 1, "1.99-decompiled", "net/minecraft/Proof.java"), index));
            Assert.Null(VanillaCitations.Resolve(root, new("Proof.cs", 2, "1.99-decompiled", "../Outside.java"), index));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [LinuxFact]
    public void Resolve_DoesNotFollowDirectorySymlinkCycles()
    {
        string root = Directory.CreateTempSubdirectory("umpk-citation-oracle").FullName;
        string? cycle = null;
        try
        {
            string tree = Path.Combine(root, "1.99-decompiled");
            string expected = Path.Combine(tree, "net", "minecraft", "Proof.java");
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
            File.WriteAllText(expected, "class Proof {}");

            cycle = Path.Combine(tree, "cycle");
            CreateDirectorySymlink(cycle, tree);

            string? resolved = VanillaCitations.Resolve(
                root,
                new("Proof.cs", 1, "1.99-decompiled", "Proof.java"),
                new Dictionary<string, ILookup<string, string>>());

            Assert.Equal(expected, resolved);
        }
        finally
        {
            if (cycle is not null)
                DeleteLink(cycle);

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_CountsCommentCitationsButIgnoresStringLiterals()
    {
        string root = Directory.CreateTempSubdirectory("umpk-citation-scan").FullName;
        try
        {
            string sourceDirectory = Path.Combine(root, "src");
            Directory.CreateDirectory(sourceDirectory);
            File.WriteAllText(Path.Combine(sourceDirectory, "Evidence.cs"), """
                namespace Fake;

                public static class Evidence
                {
                    private const string Fake = "1.99-decompiled String.java:8";
                    // 1.99-decompiled Comment.java:7
                    // / <summary>1.99-decompiled Xml.java:9</summary>
                    public static void Run() { }
                }
                """);

            IReadOnlyList<VanillaCitations.Citation> citations = VanillaCitations.Scan(root);

            Assert.Equal(2, citations.Count);
            Assert.Contains(citations, citation => citation.File == "Comment.java" && citation.Line == 6);
            Assert.Contains(citations, citation => citation.File == "Xml.java" && citation.Line == 7);
            Assert.DoesNotContain(citations, citation => citation.File == "String.java");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AnEmptyArtifactDirectory_IsNotAUsableOracle()
    {
        string root = Directory.CreateTempSubdirectory("umpk-empty-oracle").FullName;
        try
        {
            Assert.False(VanillaCitations.ContainsDecompiledTree(root));

            Directory.CreateDirectory(Path.Combine(root, "downloads", "1.21.11"));
            Assert.False(VanillaCitations.ContainsDecompiledTree(root));

            Directory.CreateDirectory(Path.Combine(root, "1.21.11-decompiled"));
            Assert.True(VanillaCitations.ContainsDecompiledTree(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [OracleFact]
    public void EveryCitation_ResolvesInTheDecompiledTrees()
    {
        string oracleRoot = VanillaCitations.OracleRoot!;
        string repoRoot = TestKit.FixturePaths.RepoRoot();
        IReadOnlyList<VanillaCitations.Citation> citations = VanillaCitations.Scan(repoRoot);

        Dictionary<string, ILookup<string, string>> index = [];
        List<string> unresolved = [.. citations
            .Where(citation => VanillaCitations.Resolve(oracleRoot, citation, index) is null)
            .Select(citation => $"{citation.Source}:{citation.Line} cites {citation.Tree} {citation.File ?? "(tree only)"}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.True(
            unresolved.Count == 0,
            $"{unresolved.Count} of {citations.Count} vanilla citations do not resolve under {oracleRoot}:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, unresolved.Take(40)));

        // The count guard catches a scanner that returns no results and passes the resolution check vacuously.
        Assert.True(citations.Count(static citation => citation.File is not null) > 400);
    }

    private static void CreateDirectorySymlink(string link, string target)
        => Directory.CreateSymbolicLink(link, target);

    private static void DeleteLink(string link)
    {
        if (Directory.Exists(link))
            Directory.Delete(link);

    }
}

/// <summary>Runs the symlink regression where the test platform guarantees unprivileged symlink creation.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "The symlink-cycle regression runs on Linux; Windows discovery commonly lacks symlink privilege.";

    }
}
