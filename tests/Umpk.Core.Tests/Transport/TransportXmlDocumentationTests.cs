using System.Reflection;
using System.Xml.Linq;
using Umpk.Transport;
using Xunit;

namespace Umpk.Core.Tests.Transport;

/// <summary>Guards XML documentation for the Bedrock-owned message transport seam in <c>Umpk.Core</c>.</summary>
/// <remarks>The Bedrock-only projects enforce CS1591 as a build error through their project files, which <c>Umpk.Core</c> cannot do without dragging legacy surface along. This test gives the <c>Umpk.Transport</c> namespace the same compiler-backed guarantee by asserting every public member appears in the documentation file the compiler already emits.</remarks>
public sealed class TransportXmlDocumentationTests
{
    [Fact]
    public void EveryPublicTransportMemberHasCompilerDocumentation()
    {
        Assembly assembly = typeof(IMessageConnection).Assembly;
        string xmlPath = Path.ChangeExtension(assembly.Location, ".xml")!;
        Assert.True(File.Exists(xmlPath), $"Expected compiler documentation at {xmlPath}.");

        HashSet<string> documented = XDocument.Load(xmlPath)
            .Descendants("member")
            .Select(member => member.Attribute("name")?.Value)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal)!;

        List<string> missing = [];
        foreach (Type type in assembly.GetTypes().Where(static type => type.Namespace == "Umpk.Transport" && IsPublicSurface(type)))
        {
            if (!documented.Contains($"T:{DocTypeName(type)}"))
                missing.Add($"T:{DocTypeName(type)}");

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (IsSkipped(field))
                    continue;
                if (!documented.Contains($"F:{DocTypeName(type)}.{field.Name}"))
                    missing.Add($"F:{DocTypeName(type)}.{field.Name}");
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (IsSkipped(property))
                    continue;
                if (!documented.Contains($"P:{DocTypeName(type)}.{property.Name}"))
                    missing.Add($"P:{DocTypeName(type)}.{property.Name}");
            }

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (IsSkipped(method))
                    continue;
                string id = $"M:{DocTypeName(type)}.{method.Name}{DocParameterList(method)}";
                if (!documented.Contains(id))
                    missing.Add(id);
            }

            foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                string id = $"M:{DocTypeName(type)}.#ctor{DocParameterList(constructor)}";
                if (!documented.Contains(id))
                    missing.Add(id);
            }
        }

        Assert.True(missing.Count == 0, $"Missing XML documentation for:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    private static bool IsPublicSurface(Type type) => type.IsPublic || type.IsNestedPublic;

    private static bool IsSkipped(MemberInfo member) =>
        member.Name.Contains('<', StringComparison.Ordinal) ||
        member.Name.Contains('$', StringComparison.Ordinal) ||
        member.Name.Contains('.', StringComparison.Ordinal) ||
        member.Name == "value__" ||
        (member is MethodInfo method && method.IsSpecialName && !method.IsConstructor);

    private static string DocParameterList(MethodBase method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length == 0 ? string.Empty : $"({string.Join(",", parameters.Select(static parameter => DocTypeName(parameter.ParameterType)))})";
    }

    private static string DocTypeName(Type type)
    {
        if (type.IsByRef)
            return DocTypeName(type.GetElementType()!) + "@";
        if (type.IsArray)
            return DocTypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
            return $"{type.GetGenericTypeDefinition().FullName!.Split('`')[0]}{{{string.Join(",", type.GetGenericArguments().Select(DocTypeName))}}}";
        return type.FullName!;
    }
}
