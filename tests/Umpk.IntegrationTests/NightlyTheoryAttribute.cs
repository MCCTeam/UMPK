using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>A <see cref="TheoryAttribute"/> that self-skips unless <c>UMPK_NIGHTLY</c> is set (to <c>1</c>/ <c>true</c>), mirroring <see cref="NightlyFactAttribute"/> for data-driven live legs. Per-datum provisioning (a missing server directory) is handled inside the leg body, which fails loudly for a representative whose jar is absent rather than skipping silently.</summary>
[System.AttributeUsage(System.AttributeTargets.Method)]
[Trait("Category", "Nightly")]
public sealed class NightlyTheoryAttribute : TheoryAttribute
{
    /// <summary>Creates the attribute, skipping the whole theory when nightly is disabled.</summary>
    public NightlyTheoryAttribute()
    {
        if (!IntegrationConfig.NightlyEnabled)
            Skip = "Nightly-only live-server test. Set UMPK_NIGHTLY=1 to run.";

    }
}
