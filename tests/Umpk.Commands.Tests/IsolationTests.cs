using System.Reflection;
using Xunit;

namespace Umpk.Commands.Tests;

/// <summary>Enforces the isolation rule: Brigadier.NET types never appear on the public surface of <c>Umpk.Commands</c>. The consumer depends on UMPK-owned types only, so a future swap of the wrapped dispatcher (a stable Brigadier 2.x release or a vendored copy) cannot break them.</summary>
public sealed class IsolationTests
{
    private static readonly Assembly CommandsAssembly = typeof(CommandService<>).Assembly;

    [Fact]
    public void No_public_member_signature_exposes_a_brigadier_type()
    {
        var offenders = new List<string>();

        foreach (var type in CommandsAssembly.GetExportedTypes())
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance |
                                                   BindingFlags.Static | BindingFlags.DeclaredOnly))
                foreach (var referenced in ReferencedTypes(member))
                    if (IsBrigadier(referenced))
                        offenders.Add($"{type.FullName}.{member.Name} -> {referenced.FullName}");

        Assert.True(offenders.Count == 0,
            "Brigadier types leaked onto the public surface: " + string.Join(", ", offenders));
    }

    [Fact]
    public void PublicApi_unshipped_text_names_no_brigadier_type()
    {
        var path = LocatePublicApiFile();
        Assert.True(File.Exists(path), $"PublicAPI.Unshipped.txt not found at {path}");

        var lines = File.ReadAllLines(path);
        var offenders = lines.Where(l => l.Contains("Brigadier", StringComparison.Ordinal)).ToList();

        Assert.True(offenders.Count == 0,
            "Brigadier types listed in PublicAPI.Unshipped.txt: " + string.Join(" | ", offenders));
    }

    private static IEnumerable<Type> ReferencedTypes(MemberInfo member)
    {
        switch (member)
        {
            case MethodInfo method:
                yield return method.ReturnType;
                foreach (var p in method.GetParameters())
                    yield return p.ParameterType;

                foreach (var g in method.GetGenericArguments())
                    yield return g;

                break;
            case ConstructorInfo ctor:
                foreach (var p in ctor.GetParameters())
                    yield return p.ParameterType;

                break;
            case PropertyInfo prop:
                yield return prop.PropertyType;
                break;
            case FieldInfo field:
                yield return field.FieldType;
                break;
            case EventInfo ev when ev.EventHandlerType is not null:
                yield return ev.EventHandlerType;
                break;
        }
    }

    private static bool IsBrigadier(Type type)
    {
        if (type.Namespace?.StartsWith("Brigadier", StringComparison.Ordinal) == true)
            return true;

        return type.IsGenericType &&
               type.GetGenericArguments().Any(IsBrigadier);
    }

    private static string LocatePublicApiFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Umpk.Commands", "PublicAPI.Unshipped.txt");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "PublicAPI.Unshipped.txt");
    }
}
