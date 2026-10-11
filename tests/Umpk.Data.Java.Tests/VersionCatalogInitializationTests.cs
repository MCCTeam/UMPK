using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class VersionCatalogInitializationTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void LookingUpOneVersionDoesNotInitializeOtherVersions(bool byName, bool found)
    {
        // A fresh catalog avoids depending on which versions other tests have already requested.
        var loadContext = new AssemblyLoadContext("IsolatedVersionCatalog", isCollectible: true);
        loadContext.Resolving += static (_, name) => AssemblyLoadContext.Default.LoadFromAssemblyName(name);
        try
        {
            Assembly assembly = loadContext.LoadFromAssemblyPath(typeof(JavaVersions).Assembly.Location);
            Type catalog = assembly.GetType(typeof(JavaVersions).FullName!)!;
            MethodInfo lookup = catalog.GetMethod(byName ? "TryGetByName" : "TryGetByProtocol")!;
            object?[] arguments = [byName ? (found ? "26.3" : "unknown") : (found ? 777 : -1), null];

            Assert.Equal(found, lookup.Invoke(null, arguments));
            Assert.Equal(found, arguments[1] is not null);
            FieldInfo[] fields = catalog.GetFields(BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Null(fields.Single(static f => f.Name == "s_all").GetValue(null));
            string[] initialized = fields.Where(static f => f.Name.StartsWith("s_v", StringComparison.Ordinal) && f.GetValue(null) is not null)
                .Select(static f => f.Name).ToArray();
            Assert.Equal(found ? ["s_v777"] : Array.Empty<string>(), initialized);
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
