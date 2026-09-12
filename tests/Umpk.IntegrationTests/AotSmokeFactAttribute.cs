using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>A <see cref="FactAttribute"/> that self-skips unless <c>UMPK_AOT_BINARY</c> names a published native binary to drive.</summary>
/// <remarks>The publish is the CI job's step, not this test's: a native publish takes minutes, needs a runtime identifier and a C toolchain, and a failure there is a build failure that belongs in its own log rather than inside a test. The test drives whatever binary the job produced, so the default suite skips one leg instead of paying for an AOT compile on every run.</remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AotSmokeFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, skipping only when no published binary is requested.</summary>
    public AotSmokeFactAttribute()
    {
        string? binary = Environment.GetEnvironmentVariable("UMPK_AOT_BINARY");
        if (string.IsNullOrEmpty(binary))
        {
            Skip = "The default suite has no published native binary. The dedicated AOT publish smoke + size budget "
                + "job runs this test. To run it locally, publish samples/MinimalBot and set UMPK_AOT_BINARY.";
            return;
        }

    }
}
