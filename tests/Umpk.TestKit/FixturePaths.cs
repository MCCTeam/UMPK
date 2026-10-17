namespace Umpk.TestKit;

/// <summary>Locates the committed fixtures relative to the consuming assembly at runtime.</summary>
public static class FixturePaths
{
    /// <summary>The repo-root-relative corpus root: <c>fixtures/corpus</c>.</summary>
    public static string CorpusRoot => Path.Combine(RepoRoot(), "fixtures", "corpus");

    /// <summary>Walks up from the test assembly location to the repository solution. Falls back to the current directory.</summary>
    public static string RepoRoot() => RepoRoot(AppContext.BaseDirectory);

    /// <summary>Finds the repository containing the specified directory.</summary>
    public static string RepoRoot(string startDirectory)
    {
        string? dir = startDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "UMPK.sln")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        return Directory.GetCurrentDirectory();
    }
}
