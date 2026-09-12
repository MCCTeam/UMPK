using Umpk.TestKit;

namespace Umpk.IntegrationTests;

/// <summary>Environment-driven configuration for the live-server integration harness.</summary>
public static class IntegrationConfig
{
    /// <summary>True when nightly (live-server) tests are enabled via <c>UMPK_NIGHTLY</c>.</summary>
    public static bool NightlyEnabled => IsTrue(Environment.GetEnvironmentVariable("UMPK_NIGHTLY"));

    /// <summary>The flag-parsing rule behind <see cref="NightlyEnabled"/>, exposed so tests can assert the actual gate behavior against known inputs instead of mirroring the implementation. A value is truthy iff it is <c>1</c> or <c>true</c> (case-insensitive); anything else, including null, is false.</summary>
    internal static bool IsTruthyFlag(string? value) => IsTrue(value);

    /// <summary>The root under which per-version server directories live (<c>&lt;root&gt;/1.21.5</c> etc.). Override with <c>UMPK_SERVER_ROOT</c>; otherwise the first existing candidate root is used. When none exists, the final candidate produces a clear "not provisioned" result.</summary>
    public static string ServerRoot
    {
        get
        {
            string? env = Environment.GetEnvironmentVariable("UMPK_SERVER_ROOT");
            if (!string.IsNullOrEmpty(env))
                return env;

            return Path.Combine(FixturePaths.RepoRoot(), "MinecraftOfficial", "downloads");
        }
    }

    /// <summary>Resolves the server directory for a Minecraft version name.</summary>
    public static string ServerDir(string versionName) => Path.Combine(ServerRoot, versionName);

    private static bool IsTrue(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
