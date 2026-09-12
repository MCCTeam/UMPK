using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>A <see cref="FactAttribute"/> that self-skips unless the <c>UMPK_NIGHTLY</c> environment variable is set (to <c>1</c>/<c>true</c>). Live-server legs are heavy and shared-state, so they are excluded from the default <c>dotnet test</c> run and enabled only by the nightly workflow. It also carries a <c>Category=Nightly</c> trait for filter-based selection.</summary>
/// <remarks>When a <paramref name="version"/> is supplied the attribute also skips (with a clear message) if the server directory for that version is absent. The nightly provisioning step is still a stub, so on a remote where no jar has been provisioned the leg must skip cleanly rather than throw <see cref="System.IO.DirectoryNotFoundException"/> from the harness and fail. The skip decision is made here at construction (xUnit v2 has no runtime <c>Assert.Skip</c>), which needs the version name, so each live leg passes its version to the attribute.</remarks>
[System.AttributeUsage(System.AttributeTargets.Method)]
[Trait("Category", "Nightly")]
public sealed class NightlyFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute. Skips when nightly is disabled, or (when <paramref name="version"/> is given) when that version's server directory is absent.</summary>
    /// <param name="version">The Minecraft version name the leg needs a provisioned server for (e.g. "1.21.5"). Omit for legs that do not require a specific server directory.</param>
    public NightlyFactAttribute(string? version = null)
    {
        if (!IntegrationConfig.NightlyEnabled)
        {
            Skip = "Nightly-only live-server test. Set UMPK_NIGHTLY=1 to run.";
            return;
        }

        if (version is not null)
        {
            string serverDir = IntegrationConfig.ServerDir(version);
            if (!Directory.Exists(serverDir))
                Skip = $"Server directory for {version} not provisioned ({serverDir}); skipping live leg. " +
                    "Provision the server jar/root (or set UMPK_SERVER_ROOT) to run it.";

        }
    }
}
