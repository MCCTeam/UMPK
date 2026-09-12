using System.Reflection;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Pins <see cref="UmpkVersion"/> against the assembly attribute it reads and against the literal version the build states. The expectation is written out here rather than derived from the same attribute the code reads, so a build that loses the <c>Version</c> property fails this rather than agreeing with itself about <c>0.0.0</c>.</summary>
public sealed class UmpkVersionTests
{
    [Fact]
    public void Current_IsTheVersionTheBuildStates()
    {
        Assert.Equal("0.9.0", UmpkVersion.Current);
        Assert.Equal(0, UmpkVersion.Major);
        Assert.Equal(9, UmpkVersion.Minor);
        Assert.Equal(0, UmpkVersion.Patch);
    }

    [Fact]
    public void Informational_ComesFromTheAssemblyAttribute()
    {
        string? attribute = typeof(UmpkClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.NotNull(attribute);
        Assert.Equal(attribute, UmpkVersion.Informational);
    }

    /// <summary>The core triple is a PREFIX of the informational version: identical on a plain build, and the part before the "+sha" on a build that stamps a source revision. Written as a prefix assertion so the test passes under both without asserting which one produced it.</summary>
    [Fact]
    public void Current_IsTheCoreOfInformational()
    {
        Assert.StartsWith(UmpkVersion.Current, UmpkVersion.Informational, StringComparison.Ordinal);
        Assert.DoesNotContain('+', UmpkVersion.Current);
        Assert.DoesNotContain('-', UmpkVersion.Current);
    }
}
