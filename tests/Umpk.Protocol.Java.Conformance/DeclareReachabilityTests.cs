using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>A packet declares its own timelines beside its codecs, and a family index does nothing but call those declarations. Nothing enforces the call: a declaration no index reaches compiles, ships and leaves the packet a marker on every protocol, which is the failure this tree was reorganised to make impossible to miss. So the call graph is checked as source, the way the protocol-comparison scan is, and both halves have to agree exactly.</summary>
public sealed class DeclareReachabilityTests
{
    [Fact]
    public void EveryDeclareIsCalledExactlyOnce()
    {
        string package = Path.Combine(FixturePaths.RepoRoot(), "src", "Umpk.Protocol.Java");
        string[] declared = [.. Declarations(Path.Combine(package, "Families")).Order(StringComparer.Ordinal)];
        List<string> calls = [.. Calls(Path.Combine(package, "Registration"))];

        Assert.True(declared.Length > 0, "No Declare method under Families/; the scan found nothing to check.");

        string[] twice = [.. calls.GroupBy(static call => call, StringComparer.Ordinal).Where(static group => group.Count() > 1).Select(static group => group.Key).Order(StringComparer.Ordinal)];
        Assert.True(twice.Length == 0, $"Declared more than once: {string.Join(", ", twice)}.");

        string[] called = [.. calls.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] unreachable = [.. declared.Except(called, StringComparer.Ordinal)];
        string[] missing = [.. called.Except(declared, StringComparer.Ordinal)];

        Assert.True(
            unreachable.Length == 0,
            $"No index calls: {string.Join(", ", unreachable)}. Those packets are markers on every protocol.");
        Assert.True(missing.Length == 0, $"Called but not declared under Families/: {string.Join(", ", missing)}.");
    }

    /// <summary>Every <c>internal static void Declare*(PacketBindings)</c> under the family tree.</summary>
    private static IEnumerable<string> Declarations(string root)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            SyntaxNode tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (MethodDeclarationSyntax method in tree.DescendantNodes().OfType<MethodDeclarationSyntax>())
                if (IsDeclaration(method) && method.Parent is TypeDeclarationSyntax owner)
                    yield return owner.Identifier.ValueText + "." + method.Identifier.ValueText;

        }
    }

    /// <summary>Every <c>Type.Declare*(bindings)</c> call inside a family index's <c>Register</c>.</summary>
    private static IEnumerable<string> Calls(string root)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*Bindings.cs", SearchOption.TopDirectoryOnly))
        {
            SyntaxNode tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (MethodDeclarationSyntax register in tree.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (register.Identifier.ValueText is not "Register")
                    continue;

                foreach (InvocationExpressionSyntax call in register.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    if (call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner } access
                        && access.Name.Identifier.ValueText.StartsWith("Declare", StringComparison.Ordinal))
                        yield return owner.Identifier.ValueText + "." + access.Name.Identifier.ValueText;

            }
        }
    }

    private static bool IsDeclaration(MethodDeclarationSyntax method) =>
        method.Identifier.ValueText.StartsWith("Declare", StringComparison.Ordinal)
        && method.Modifiers.Any(static modifier => modifier.ValueText is "internal")
        && method.Modifiers.Any(static modifier => modifier.ValueText is "static")
        && method.ParameterList.Parameters.Count == 1
        && method.ParameterList.Parameters[0].Type?.ToString() is "PacketBindings";
}
