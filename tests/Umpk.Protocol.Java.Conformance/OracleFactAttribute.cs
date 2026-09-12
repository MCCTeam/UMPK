using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>A <see cref="FactAttribute"/> that skips when the external reference trees are unavailable.</summary>
/// <remarks>Decompiled trees are local artifacts and are not redistributable, so a CI runner that checks out only UMPK has nothing to resolve against. Skipping there is the honest outcome. The constructor makes the decision because xUnit v2 has no runtime skip.</remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OracleFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, skipping when no oracle root resolves.</summary>
    public OracleFactAttribute()
    {
        if (VanillaCitations.OracleRoot is null)
            Skip = "No decompiled trees on this machine. Populate the repository-local "
                + "MinecraftOfficial directory or set UMPK_ORACLE_ROOT to run it.";

    }
}
