using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>No codec may see a protocol number. Version differences are resolved once, when the descriptor picks which codec to bind; a codec that branches on a version number re-grows the inline-branch failure mode the whole timeline design exists to prevent.</summary>
/// <remarks>The proof is <see cref="PacketCodecContextExposesNoProtocol"/>: the context object has no protocol on its public surface, so a codec has nothing to compare. The source scan beside it is a SIGNAL, not a proof, and it is named that way so nobody reads a text walk as a guarantee.</remarks>
public sealed class CodecTreeConformanceTests
{
    /// <summary>The three files that legitimately compare a protocol. One is a bind-site decision nothing in the declarative timeline can express, and it sits in the key packet's own wire file, three lines under the timeline it is the other half of; two are the resolution machinery itself, which compares protocols by construction.</summary>
    private static readonly string[] AllowedProtocolComparisons =
    [
        Path.Combine("Families", "Login", "Key", "Key.Wire.cs"),
        Path.Combine("Registration", "PacketTimeline.cs"),
        Path.Combine("Registration", "PacketBindings.cs"),
    ];

    /// <summary>The four trees a codec can live in. <c>Core/</c> is listed because the codec primitives moved to the top level, and <c>Families/</c> and <c>Wire/</c> would not reach them.</summary>
    private static readonly string[] CodecTrees = ["Core", "Families", "Wire", "Registration"];

    /// <summary>The whole public surface of <c>PacketCodecContext</c>, which is what makes the rule structural: a codec is handed registries and connection state and nothing else. A new line here means someone widened what a codec can see, and the widening has to be argued for rather than noticed later.</summary>
    private static readonly string[] ExpectedContextSurface =
    [
        "Umpk.Protocol.Java.Codecs.PacketCodecContext",
        "Umpk.Protocol.Java.Codecs.PacketCodecContext.PacketCodecContext(Umpk.Game.Registries.RegistryAccess! registries, Umpk.Protocol.Java.Codecs.IConnectionCodecState! state) -> void",
        "Umpk.Protocol.Java.Codecs.PacketCodecContext.Registries.get -> Umpk.Game.Registries.RegistryAccess!",
        "Umpk.Protocol.Java.Codecs.PacketCodecContext.State.get -> Umpk.Protocol.Java.Codecs.IConnectionCodecState!",
        "static Umpk.Protocol.Java.Codecs.PacketCodecContext.Registryless.get -> Umpk.Protocol.Java.Codecs.PacketCodecContext!",
    ];

    private const string ContextTypeName = "Umpk.Protocol.Java.Codecs.PacketCodecContext";

    [Fact]
    public void PacketCodecContextExposesNoProtocol()
    {
        string api = Path.Combine(PackageRoot(), "PublicAPI.Unshipped.txt");
        Assert.True(File.Exists(api), $"No public API file at {api}.");

        string[] surface =
        [
            .. File.ReadLines(api).Where(DeclaresTheContext).Order(StringComparer.Ordinal),
        ];

        Assert.Equal(ExpectedContextSurface.Order(StringComparer.Ordinal), surface);
    }

    [Fact]
    public void NoCodecSourceMentionsAProtocolComparison()
    {
        List<string> offenders = [];
        foreach (string file in CodecTreeFiles())
        {
            if (AllowedProtocolComparisons.Any(allowed => file.EndsWith(allowed, StringComparison.Ordinal)))
                continue;

            SyntaxNode root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

            // Trivia is not part of the tree, so a doc comment discussing a boundary cannot fire, and the walk sees the reversed operand order and the relational-pattern form that a regex misses.
            foreach (SyntaxNode node in root.DescendantNodes().Where(ComparesAProtocol))
                offenders.Add($"{Path.GetFileName(file)}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");

        }

        Assert.True(offenders.Count == 0, $"Protocol comparison in the codec tree: {string.Join(", ", offenders)}.");
    }

    private static bool DeclaresTheContext(string line)
    {
        ReadOnlySpan<char> rest = line.AsSpan();
        foreach (string modifier in (string[])["static ", "virtual ", "abstract ", "override ", "readonly "])
            if (rest.StartsWith(modifier, StringComparison.Ordinal))
            {
                rest = rest[modifier.Length..];
                break;
            }

        if (!rest.StartsWith(ContextTypeName, StringComparison.Ordinal))
            return false;

        rest = rest[ContextTypeName.Length..];
        return rest.IsEmpty || rest[0] is '.' or '(';
    }

    private static bool ComparesAProtocol(SyntaxNode node) => node switch
    {
        BinaryExpressionSyntax binary when IsComparison(binary.Kind()) =>
            IsProtocol(binary.Left) || IsProtocol(binary.Right),
        IsPatternExpressionSyntax pattern when IsProtocol(pattern.Expression) =>
            pattern.Pattern.DescendantNodesAndSelf().OfType<RelationalPatternSyntax>().Any(),
        SwitchExpressionSyntax switched => IsProtocol(switched.GoverningExpression),
        _ => false,
    };

    private static bool IsComparison(SyntaxKind kind) => kind
        is SyntaxKind.LessThanExpression
        or SyntaxKind.LessThanOrEqualExpression
        or SyntaxKind.GreaterThanExpression
        or SyntaxKind.GreaterThanOrEqualExpression
        or SyntaxKind.EqualsExpression
        or SyntaxKind.NotEqualsExpression;

    /// <summary>Whether an operand names a protocol number: the local every registrar hook calls <c>protocol</c>, a <c>.Protocol</c> property read, or a <c>JavaProtocols</c> constant.</summary>
    private static bool IsProtocol(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText is "protocol",
        MemberAccessExpressionSyntax member =>
            member.Name.Identifier.ValueText is "Protocol"
            || (member.Expression is IdentifierNameSyntax owner && owner.Identifier.ValueText is "JavaProtocols"),
        _ => false,
    };

    private static IEnumerable<string> CodecTreeFiles()
    {
        string package = PackageRoot();
        foreach (string relative in CodecTrees)
        {
            string root = Path.Combine(package, relative);
            Assert.True(Directory.Exists(root), $"No codec tree at {root}.");
            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                yield return file;

        }
    }

    private static string PackageRoot() => Path.Combine(FixturePaths.RepoRoot(), "src", "Umpk.Protocol.Java");
}
