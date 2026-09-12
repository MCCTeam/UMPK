using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>Finds structured references in repository comments and resolves them against external trees.</summary>
/// <remarks>A reference names a versioned tree and can name a class. The scanner counts tree-only references and resolves references that include a class.</remarks>
internal static partial class VanillaCitations
{
    /// <summary>Gets the external tree root, which <c>UMPK_ORACLE_ROOT</c> can override.</summary>
    /// <remarks>The default is the repository-local untracked artifact directory. Null when neither it nor the override exists.</remarks>
    internal static string? OracleRoot
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("UMPK_ORACLE_ROOT");
            if (!string.IsNullOrEmpty(configured))
                return ContainsDecompiledTree(configured) ? configured : null;

            string fallback = Path.Combine(TestKit.FixturePaths.RepoRoot(), "MinecraftOfficial");
            return ContainsDecompiledTree(fallback) ? fallback : null;
        }
    }

    /// <summary>Whether an artifact root contains at least one source tree that citations can use.</summary>
    internal static bool ContainsDecompiledTree(string root) =>
        Directory.Exists(root)
        && Directory.EnumerateDirectories(root, "*-decompiled", SearchOption.TopDirectoryOnly).Any();

    /// <summary>One citation, located well enough to name it in a failure.</summary>
    /// <param name="Source">The citing file, relative to the repository root.</param>
    /// <param name="Line">The 1-based line the citation is written on.</param>
    /// <param name="Tree">The versioned tree directory name.</param>
    /// <param name="File">The class the citation names, or null when it names only the tree.</param>
    internal sealed record Citation(string Source, int Line, string Tree, string? File);

    /// <summary>Every citation in maintained source and test code.</summary>
    internal static IReadOnlyList<Citation> Scan(string repoRoot)
    {
        List<Citation> citations = [];
        foreach (string path in SourceFiles(repoRoot))
        {
            string relative = Path.GetRelativePath(repoRoot, path);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);
            foreach (SyntaxTrivia trivia in syntaxTree.GetRoot().DescendantTrivia(descendIntoTrivia: true))
            {
                if (!IsCommentTrivia(trivia.Kind()))
                    continue;

                int firstLine = syntaxTree.GetLineSpan(trivia.Span).StartLinePosition.Line;
                string[] lines = trivia.ToFullString().Split('\n');
                for (int i = 0; i < lines.Length; i++)
                    foreach (Match match in CitationPattern().Matches(lines[i]))
                    {
                        string tree = $"{match.Groups["version"].Value}-{match.Groups["client"].Value}decompiled";
                        Group file = match.Groups["file"];
                        citations.Add(new Citation(relative, firstLine + i + 1, tree, file.Success ? file.Value : null));
                    }

            }
        }

        return citations;
    }

    /// <summary>The file a citation points at, or null when the tree or the class is not there.</summary>
    /// <remarks>A reference with no class resolves to its tree directory. Other references first use their relative path, then use a basename index when the path is incomplete.</remarks>
    internal static string? Resolve(string oracleRoot, Citation citation, Dictionary<string, ILookup<string, string>> index)
    {
        string tree = Path.Combine(oracleRoot, citation.Tree);
        if (!Directory.Exists(tree))
            return null;

        if (citation.File is not string file)
            return tree;

        string direct = Path.GetFullPath(Path.Combine(tree, file.Replace('/', Path.DirectorySeparatorChar)));
        if (IsInside(tree, direct) && File.Exists(direct))
            return direct;

        if (!index.TryGetValue(citation.Tree, out ILookup<string, string>? byName))
        {
            byName = EnumerateFilesWithoutFollowingDirectoryLinks(tree, "*.java")
                .ToLookup(static path => Path.GetFileName(path), StringComparer.Ordinal);
            index[citation.Tree] = byName;
        }

        return byName[Path.GetFileName(file)].FirstOrDefault();
    }

    private static bool IsInside(string root, string path) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static IEnumerable<string> SourceFiles(string repoRoot) =>
        new[] { "src", "tests" }
            .Select(dir => Path.Combine(repoRoot, dir))
            .Where(Directory.Exists)
            .SelectMany(dir => EnumerateFilesWithoutFollowingDirectoryLinks(dir, "*.cs"))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    /// <summary>Enumerates files without descending through child directory links.</summary>
    private static IEnumerable<string> EnumerateFilesWithoutFollowingDirectoryLinks(string root, string pattern)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(path);

                else if (FileNameMatches(path, pattern))
                    yield return path;

            }
        }
    }

    private static bool FileNameMatches(string path, string pattern) =>
        pattern.StartsWith("*.", StringComparison.Ordinal)
            ? Path.GetFileName(path).EndsWith(pattern[1..], StringComparison.Ordinal)
            : string.Equals(Path.GetFileName(path), pattern, StringComparison.Ordinal);

    private static bool IsCommentTrivia(SyntaxKind kind) =>
        kind is SyntaxKind.SingleLineCommentTrivia
            or SyntaxKind.MultiLineCommentTrivia
            or SyntaxKind.SingleLineDocumentationCommentTrivia
            or SyntaxKind.MultiLineDocumentationCommentTrivia;

    // Require the optional class name on the same line as its tree marker. This prevents cross-line matches.
    [GeneratedRegex(@"(?<version>[0-9]+(?:\.[0-9]+)*)-(?<client>client-)?decompiled(?:[ \t]*[:/]?[ \t]*(?:<c>|`)?(?<file>[A-Za-z0-9_$./]*[A-Za-z0-9_$]\.java))?")]
    private static partial Regex CitationPattern();
}
