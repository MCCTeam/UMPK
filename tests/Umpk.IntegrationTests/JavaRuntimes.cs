namespace Umpk.IntegrationTests;

/// <summary>Selects a compatible JVM for each server era. Protocols through 754 use Java 11; newer protocols use Java 25.</summary>
/// <remarks><c>UMPK_JAVA11</c> and <c>UMPK_JAVA25</c> override their respective tiers. <c>UMPK_JAVA</c> overrides both for a single-runtime environment.</remarks>
public static class JavaRuntimes
{
    /// <summary>Default Java 11 executable for the 1.8 - 1.16.5 servers (protocols 47 - 754).</summary>
    private const string DefaultJava11 = "java";

    /// <summary>Default Java 25 executable for the 1.17+ servers (protocols 755+).</summary>
    private const string DefaultJava25 = "java";

    /// <summary>The highest protocol that still boots on the legacy (Java 11) runtime: 1.16.5 is 754.</summary>
    private const int LastLegacyProtocol = 754;

    /// <summary>Resolves the JVM executable for a server whose client protocol is <paramref name="protocol"/>.</summary>
    public static string ForProtocol(int protocol)
    {
        string? global = Environment.GetEnvironmentVariable("UMPK_JAVA");
        if (!string.IsNullOrEmpty(global))
            return global;

        return protocol <= LastLegacyProtocol ? Java11 : Java25;
    }

    /// <summary>The resolved Java 11 executable (env <c>UMPK_JAVA11</c> or <c>java</c> on PATH).</summary>
    public static string Java11 =>
        Environment.GetEnvironmentVariable("UMPK_JAVA11") is { Length: > 0 } j ? j : DefaultJava11;

    /// <summary>The resolved Java 25 executable (env <c>UMPK_JAVA25</c> or <c>java</c> on PATH).</summary>
    public static string Java25 =>
        Environment.GetEnvironmentVariable("UMPK_JAVA25") is { Length: > 0 } j ? j : DefaultJava25;

    /// <summary>A short human tier label ("java11"/"java25") for the result matrix.</summary>
    public static string TierLabel(int protocol) =>
        (Environment.GetEnvironmentVariable("UMPK_JAVA") is { Length: > 0 })
            ? "java(env)"
            : protocol <= LastLegacyProtocol ? "java11" : "java25";
}
