using System.Reflection;
using Xunit;

namespace Umpk.Core.Tests;

/// <summary><c>Umpk.Server</c> and <c>Umpk.Proxy</c> are still empty placeholders, and the moment either stops being one it must arrive with a real test suite.</summary>
/// <remarks>
/// <para>Neither assembly has a source file, so an empty test project would inflate the suite count without testing behavior. This guard fails when either assembly gains production types; that change must add a real test suite and remove the matching assertion here.</para>
/// </remarks>
public sealed class PlaceholderAssemblyTests
{
    [Theory]
    [InlineData("Umpk.Server")]
    [InlineData("Umpk.Proxy")]
    public void PlaceholderAssembly_HasNoTypes_OrItNeedsItsOwnSuite(string assemblyName)
    {
        Assembly assembly = Assembly.Load(assemblyName);

        // Compiler-generated attribute holders (GlobalUsings, AssemblyInfo) emit no types, so a truly empty assembly reports none at all. Anything here is production code that arrived untested.
        Type[] types = assembly.GetTypes();

        Assert.True(
            types.Length == 0,
            $"{assemblyName} is no longer an empty placeholder ({types.Length} type(s): "
            + $"{string.Join(", ", types.Take(5).Select(t => t.FullName))}). "
            + $"It needs its own test project again; U30 removed the empty one that was standing in for it.");
    }
}
