using System.Text;
using System.Text.RegularExpressions;

namespace Umpk.DataGen;

/// <summary>Reads packet layouts from an oracle tree and compares them with an earlier oracle tree.</summary>
/// <remarks>
/// <para>This diagnostic compares two oracle trees and reports which packets changed shape. It does not inspect UMPK registrations or prove which codec an UMPK protocol selects, so a clean result does not establish codec inheritance or compatibility.</para>
/// <para>Two limits are structural. The supported input format exposes declarative packet tables from 1.20.5 (protocol 766) onward, so older trees are refused rather than half-read. Packets whose codec has no recognized declarative or imperative layout are counted and named in the output header.</para>
/// </remarks>
internal static partial class LayoutOracle
{
    /// <summary>One packet's proposed field list from an oracle tree.</summary>
    internal sealed record PacketLayout(string Identifier, string Flow, string Group, string Class, string Form, IReadOnlyList<string> Fields);

    /// <summary>Everything one tree yielded, including what it refused.</summary>
    internal sealed record TreeLayouts(string Tree, IReadOnlyList<PacketLayout> Packets, IReadOnlyList<string> Unparsed)
    {
        /// <summary>The share of declared packets that produced a field list.</summary>
        public double ParseRate => Packets.Count + Unparsed.Count == 0
            ? 0
            : (double)Packets.Count / (Packets.Count + Unparsed.Count);
    }

    /// <summary>The oracle root, from <c>--oracle</c>, then <c>UMPK_ORACLE_ROOT</c>, then the local artifact directory.</summary>
    internal static string? OracleRoot(string? configured)
    {
        string? candidate = configured
            ?? Environment.GetEnvironmentVariable("UMPK_ORACLE_ROOT")
            ?? Path.Combine(RepoRoot(), "MinecraftOfficial");
        return Directory.Exists(candidate) ? candidate : null;
    }

    /// <summary>The tree directory for a version name, or null when the oracle has no tree for that version.</summary>
    internal static string? TreeFor(string oracleRoot, string versionName)
    {
        string tree = Path.Combine(oracleRoot, $"{versionName}-decompiled");
        return Directory.Exists(tree) ? tree : null;
    }

    /// <summary>Reads every declared packet layout from one oracle tree.</summary>
    internal static TreeLayouts Read(string tree)
    {
        string protocolDir = Path.Combine(tree, "net", "minecraft", "network", "protocol");
        if (!Directory.Exists(protocolDir))
            return new TreeLayouts(Path.GetFileName(tree), [], []);

        List<PacketLayout> packets = [];
        List<string> unparsed = [];
        foreach (string table in EnumerateFilesWithoutFollowingDirectoryLinks(protocolDir, "*PacketTypes.java")
            .Order(StringComparer.Ordinal))
        {
            string group = Path.GetFileName(Path.GetDirectoryName(table)!);
            foreach (Match declaration in PacketTypeDeclaration().Matches(File.ReadAllText(table)))
            {
                string type = declaration.Groups["class"].Value;
                string identifier = declaration.Groups["id"].Value;
                string flow = declaration.Groups["flow"].Value;
                string source = Path.Combine(Path.GetDirectoryName(table)!, $"{type}.java");
                if (!File.Exists(source))
                {
                    unparsed.Add($"{group} {flow} {identifier} ({type}.java is not in the tree)");
                    continue;
                }

                string text = File.ReadAllText(source);
                Match codec = StreamCodecAssignment().Match(text);
                switch (codec.Groups["form"].Value)
                {
                    case "StreamCodec.unit":
                        // A unit codec is a packet with no body at all, which is a layout, not a refusal.
                        packets.Add(new PacketLayout(identifier, flow, group, type, "unit", []));
                        break;
                    case "StreamCodec.composite" when CompositeFields(text, codec) is { } composite:
                        packets.Add(new PacketLayout(identifier, flow, group, type, "composite", composite));
                        break;
                    case "Packet.codec" when WriteFields(text) is { } written:
                        packets.Add(new PacketLayout(identifier, flow, group, type, "write", written));
                        break;
                    default:
                        unparsed.Add($"{group} {flow} {identifier} ({type})");
                        break;
                }
            }
        }

        return new TreeLayouts(
            Path.GetFileName(tree),
            [.. packets.OrderBy(static p => $"{p.Group} {p.Flow} {p.Identifier}", StringComparer.Ordinal)],
            [.. unparsed.Order(StringComparer.Ordinal)]);
    }

    /// <summary>Writes the report: every packet whose layout moved between the two trees.</summary>
    internal static string Report(TreeLayouts baseline, TreeLayouts proposed)
    {
        var text = new StringBuilder();
        text.Append("# Vanilla source-to-source layout heuristic. This does not inspect or prove UMPK codec\n")
            .Append("# selection or inheritance. A packet absent from this report is not thereby proven\n")
            .Append("# unchanged; see the unparsed counts.\n")
            .Append($"# baseline {baseline.Tree}: {baseline.Packets.Count} packets read, ")
            .Append($"{baseline.Unparsed.Count} unparsed ({baseline.ParseRate:P1} parsed)\n")
            .Append($"# proposed for    {proposed.Tree}: {proposed.Packets.Count} packets read, ")
            .Append($"{proposed.Unparsed.Count} unparsed ({proposed.ParseRate:P1} parsed)\n");

        Dictionary<string, PacketLayout> before = baseline.Packets.ToDictionary(Key, StringComparer.Ordinal);
        Dictionary<string, PacketLayout> after = proposed.Packets.ToDictionary(Key, StringComparer.Ordinal);

        int moved = 0;
        foreach ((string key, PacketLayout now) in after.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!before.TryGetValue(key, out PacketLayout? then))
            {
                moved++;
                text.Append($"\nNEW {key}\n");
                AppendFields(text, "  +", now.Fields);
                continue;
            }

            if (then.Fields.SequenceEqual(now.Fields, StringComparer.Ordinal))
                continue;

            moved++;
            text.Append($"\nCHANGED {key} ({then.Form} -> {now.Form})\n");
            AppendFields(text, $"  {baseline.Tree} -", then.Fields);
            AppendFields(text, $"  {proposed.Tree} +", now.Fields);
        }

        foreach (string key in before.Keys.Except(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            moved++;
            text.Append($"\nGONE {key}\n");
        }

        text.Append($"\n{moved} packet(s) moved.\n");
        if (proposed.Unparsed.Count > 0)
        {
            text.Append($"\nUnparsed in {proposed.Tree}, so unchecked:\n");
            foreach (string packet in proposed.Unparsed)
                text.Append($"  {packet}\n");

        }

        return text.ToString();
    }

    /// <summary>Writes one tree's proposals, for reading a version's shapes without a comparison.</summary>
    internal static string Listing(TreeLayouts layouts)
    {
        var text = new StringBuilder();
        text.Append($"# {layouts.Tree}: {layouts.Packets.Count} packets read, ")
            .Append($"{layouts.Unparsed.Count} unparsed ({layouts.ParseRate:P1} parsed)\n");
        foreach (PacketLayout packet in layouts.Packets)
        {
            text.Append($"\n{Key(packet)} ({packet.Form})\n");
            AppendFields(text, "  ", packet.Fields);
        }

        return text.ToString();
    }

    private static string Key(PacketLayout packet) => $"{packet.Group} {packet.Flow} minecraft:{packet.Identifier}";

    private static void AppendFields(StringBuilder text, string prefix, IReadOnlyList<string> fields)
    {
        if (fields.Count == 0)
        {
            text.Append($"{prefix} (no fields)\n");
            return;
        }

        for (int i = 0; i < fields.Count; i++)
            text.Append($"{prefix} {i}. {fields[i]}\n");

    }

    /// <summary>The field list of a <c>StreamCodec.composite(codec, Type::getter, ..., Type::new)</c>: the arguments pair up, and the trailing constructor reference is not a field.</summary>
    private static IReadOnlyList<string>? CompositeFields(string text, Match codec)
    {
        IReadOnlyList<string>? arguments = Arguments(text, codec.Index + codec.Length - 1);
        if (arguments is null || arguments.Count < 3 || arguments.Count % 2 == 0)
            return null;

        List<string> fields = [];
        for (int i = 0; i + 1 < arguments.Count - 1; i += 2)
        {
            string getter = arguments[i + 1];
            int accessor = getter.LastIndexOf("::", StringComparison.Ordinal);
            fields.Add($"{(accessor < 0 ? getter : getter[(accessor + 2)..])}: {arguments[i]}");
        }

        return fields;
    }

    /// <summary>The field list of a hand-written <c>write</c> method: the ordered buffer calls it makes. The argument is kept because <c>writeVarInt(this.food)</c> and <c>writeVarInt(this.slot)</c> are different fields written the same way, and a swap between them is a real wire change.</summary>
    private static IReadOnlyList<string>? WriteFields(string text)
    {
        Match method = WriteMethod().Match(text);
        if (!method.Success)
            return null;

        string? body = Block(text, method.Index + method.Length - 1);
        if (body is null)
            return null;

        List<string> fields = [.. BufferCall()
            .Matches(body)
            .Select(match => $"{match.Groups["call"].Value}({Collapse(match.Groups["args"].Value)})")];
        return fields.Count == 0 ? null : fields;
    }

    /// <summary>Splits a call's arguments at top-level commas, starting at the opening parenthesis.</summary>
    private static IReadOnlyList<string>? Arguments(string text, int openParenthesisAt)
    {
        int open = text.IndexOf('(', openParenthesisAt);
        if (open < 0)
            return null;

        List<string> arguments = [];
        var current = new StringBuilder();
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '(' or '[' or '{':
                    depth++;
                    if (depth > 1)
                        current.Append(c);

                    continue;
                case ')' or ']' or '}':
                    depth--;
                    if (depth == 0)
                    {
                        arguments.Add(Collapse(current.ToString()));
                        return arguments;
                    }

                    current.Append(c);
                    continue;
                case ',' when depth == 1:
                    arguments.Add(Collapse(current.ToString()));
                    current.Clear();
                    continue;
                default:
                    current.Append(c);
                    continue;
            }
        }

        return null;
    }

    /// <summary>The body of the block whose opening brace is at or after <paramref name="braceAt"/>.</summary>
    private static string? Block(string text, int braceAt)
    {
        int open = text.IndexOf('{', braceAt);
        if (open < 0)
            return null;

        int depth = 0;
        for (int i = open; i < text.Length; i++)
            if (text[i] == '{')
                depth++;

            else if (text[i] == '}' && --depth == 0)
                return text[(open + 1)..i];

        return null;
    }

    private static string Collapse(string value) => Whitespace().Replace(value, " ").Trim();

    internal static string RepoRoot() => RepoRoot(AppContext.BaseDirectory);

    internal static string RepoRoot(string startDirectory)
    {
        string? dir = Path.GetFullPath(startDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "UMPK.sln")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>Enumerates files without descending through child directory links. Oracle trees can carry a link back to a parent tree, and <see cref="SearchOption.AllDirectories"/> follows that link on platforms where directory enumeration resolves it.</summary>
    private static IEnumerable<string> EnumerateFilesWithoutFollowingDirectoryLinks(string root, string pattern)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(path);

                else if (FileNameMatches(path, pattern))
                    yield return path;

            }
        }
    }

    private static bool FileNameMatches(string path, string pattern) =>
        pattern.StartsWith("*", StringComparison.Ordinal)
            ? Path.GetFileName(path).EndsWith(pattern[1..], StringComparison.Ordinal)
            : string.Equals(Path.GetFileName(path), pattern, StringComparison.Ordinal);

    [GeneratedRegex(@"PacketType<\s*(?<class>[A-Za-z0-9_]+)\s*>\s+[A-Z0-9_]+\s*=\s*create(?<flow>Clientbound|Serverbound)\(\s*""(?<id>[a-z0-9_/]+)""\s*\)")]
    private static partial Regex PacketTypeDeclaration();

    [GeneratedRegex(@"(?:private|public|protected)\s+void\s+write\s*\([^)]*\)\s*\{")]
    private static partial Regex WriteMethod();

    // This recognizer uses the packet's first STREAM_CODEC assignment and intentionally handles only the supported layout forms. Supported oracle trees place the packet codec before nested payload types. Other forms remain unparsed rather than borrowing a nested codec.
    [GeneratedRegex(@"\bSTREAM_CODEC\s*=\s*(?<form>[A-Za-z0-9_.]+)\s*\(")]
    private static partial Regex StreamCodecAssignment();

    [GeneratedRegex(@"\.(?<call>write[A-Za-z0-9_]*)\((?<args>[^;]*?)\)\s*;")]
    private static partial Regex BufferCall();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
