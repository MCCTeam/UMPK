using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Umpk.DataGen;

/// <summary>Emits the per-protocol translation tables and their shared string pool.</summary>
/// <remarks>Deliberately independent of <see cref="Emitter.Emit"/>: it uses its own content dedup pool (<see cref="_langDedup"/> / <see cref="_langBlobs"/>) rather than the one <c>Emit</c> feeds into the main table pool, so calling this method does not change the <c>Umpk.Data.Java</c> output or its golden-text tests. <c>Program</c>'s <c>generate</c> command only calls it when <c>--out-lang</c> is passed; the ordinary <c>--out</c> flow is untouched either way.</remarks>
internal sealed partial class Emitter
{
    // Separate dedup pool from Emit()'s _dedup/_blobs (see the class remarks above).
    private readonly Dictionary<(string Kind, string Hash), string> _langDedup = [];
    private readonly Dictionary<string, byte[]> _langBlobs = [];

    public IReadOnlyDictionary<string, EmittedFile> EmitLang()
    {
        // Distinct (key, template) pairs across every protocol, sorted ordinal by (key, template). Ordinal on both fields, not just the key: two protocols can disagree on a key's template (that is the entire reason per-protocol tables exist), so the pool has to be able to hold both "chat.type.text" -> "<%s> %s" and any other era's "chat.type.text" -> something else as two separate pool entries.
        SortedSet<(string Key, string Template)> pairs = new(Comparer<(string Key, string Template)>.Create(
            static (a, b) =>
            {
                int byKey = string.CompareOrdinal(a.Key, b.Key);
                return byKey != 0 ? byKey : string.CompareOrdinal(a.Template, b.Template);
            }));
        foreach (VersionData version in _dataset.ByProtocol.Values)
            foreach ((string key, string template) in version.Lang)
                pairs.Add((key, template));

        List<(string Key, string Template)> pool = [.. pairs];
        Dictionary<(string Key, string Template), int> poolIndex = new(pool.Count);
        for (int i = 0; i < pool.Count; i++)
            poolIndex[pool[i]] = i;

        string poolMember = InternLangBlob("langpool", "Shared", PackLangPool(pool));

        // Per protocol: the pool indices this protocol's table uses, ascending, delta-packed. Deduplication collapses protocols whose lang tables are byte-identical (e.g. two patch releases that share one representative jar) onto a single blob automatically.
        SortedDictionary<int, string> indexMember = [];
        foreach (VersionData version in _dataset.ByProtocol.Values.OrderBy(v => v.Protocol))
        {
            List<int> indices = [.. version.Lang.Select(kv => poolIndex[(kv.Key, kv.Value)])];
            indices.Sort();
            indexMember[version.Protocol] = InternLangBlob(
                "langidx", $"V{version.Protocol.ToString(CultureInfo.InvariantCulture)}", PackAscendingIndices(indices));
        }

        Dictionary<string, EmittedFile> files = [];
        EmitLangTables(files);
        EmitJavaLanguage(files, poolMember, indexMember);
        return files;
    }

    /// <summary>Packs the shared pool: <c>[VarInt count][per pair: VarInt keyLen, key utf8, VarInt tmplLen, tmpl utf8]</c>.</summary>
    private static byte[] PackLangPool(IReadOnlyList<(string Key, string Template)> pool)
    {
        using MemoryStream ms = new();
        WriteVarInt(ms, pool.Count);
        foreach ((string key, string template) in pool)
        {
            byte[] keyUtf8 = Encoding.UTF8.GetBytes(key);
            WriteVarInt(ms, keyUtf8.Length);
            ms.Write(keyUtf8);
            byte[] tmplUtf8 = Encoding.UTF8.GetBytes(template);
            WriteVarInt(ms, tmplUtf8.Length);
            ms.Write(tmplUtf8);
        }
        return ms.ToArray();
    }

    /// <summary>Packs one protocol's pool references: <c>[VarInt count][VarInt delta...]</c>, deltas over the ascending indices.</summary>
    private static byte[] PackAscendingIndices(IReadOnlyList<int> ascendingIndices)
    {
        using MemoryStream ms = new();
        WriteVarInt(ms, ascendingIndices.Count);
        int previous = 0;
        foreach (int index in ascendingIndices)
        {
            WriteVarInt(ms, index - previous);
            previous = index;
        }
        return ms.ToArray();
    }

    private string InternLangBlob(string kind, string ownerLabel, byte[] blob)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(blob));
        var key = (kind, hash);
        if (_langDedup.TryGetValue(key, out string? existing))
        {
            if (!_langBlobs[existing].AsSpan().SequenceEqual(blob))
                throw new InvalidOperationException($"SHA-256 collision while interning {kind} for {ownerLabel}.");

            return existing;
        }

        string member = kind switch
        {
            "langpool" => $"LanguagePool{ownerLabel}",
            "langidx" => $"LanguageIndex{ownerLabel}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown language-table kind."),
        };
        if (_langBlobs.ContainsKey(member))
            throw new InvalidOperationException($"Duplicate language-table member name '{member}'.");

        _langDedup[key] = member;
        _langBlobs[member] = blob;
        return member;
    }

    private void EmitLangTables(Dictionary<string, EmittedFile> files)
    {
        StringBuilder sb = new();
        LangHeader(sb);
        sb.AppendLine("namespace Umpk.Data.Lang;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Content-deduplicated binary tables backing the vanilla per-protocol translation tables. Each member is a ReadOnlySpan&lt;byte&gt; over an embedded blob; identical per-protocol index tables collapse to the member named for their earliest owning protocol. Mirrors Umpk.Data.Java.SharedTables in shape, kept in a separate package: see VanillaTranslations for the reader.</summary>");
        sb.AppendLine("internal static class LangTables");
        sb.AppendLine("{");
        foreach ((string member, byte[] blob) in _langBlobs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append($"    public static ReadOnlySpan<byte> {member} => new byte[] {{ ");
            sb.Append(string.Join(", ", blob.Select(b => "0x" + b.ToString("X2", CultureInfo.InvariantCulture))));
            sb.AppendLine(" };");
        }
        sb.AppendLine("}");
        files["LangTables.g.cs"] = new EmittedFile("LangTables.g.cs", sb.ToString());
    }

    private void EmitJavaLanguage(Dictionary<string, EmittedFile> files, string poolMember, SortedDictionary<int, string> indexMember)
    {
        StringBuilder sb = new();
        LangHeader(sb);
        sb.AppendLine("namespace Umpk.Data.Lang;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Generated vanilla-lang lookup. Pool is the deduplicated (key, template) pair table shared by every protocol; Index(protocol) is that protocol's own ascending, delta-packed list of pool positions, or an empty span for an unknown protocol. Mirrors the shape of Umpk.Data.Java.JavaGameData's per-protocol table switches (e.g. MenuDefs): a plain protocol -> blob switch, no per-version descriptor type.</summary>");
        sb.AppendLine("internal static class JavaLanguage");
        sb.AppendLine("{");
        sb.AppendLine($"    public static ReadOnlySpan<byte> Pool => LangTables.{poolMember};");
        sb.AppendLine();
        sb.AppendLine("    // Every protocol with a shipped table, ascending (indexMember is a SortedDictionary, so this and the Index(...) switch below are always built from the same order).");
        sb.Append("    public static ReadOnlySpan<int> Protocols => new int[] { ");
        sb.Append(string.Join(", ", indexMember.Keys.Select(p => p.ToString(CultureInfo.InvariantCulture))));
        sb.AppendLine(" };");
        sb.AppendLine();
        sb.AppendLine("    public static ReadOnlySpan<byte> Index(int protocol) => protocol switch");
        sb.AppendLine("    {");
        foreach ((int protocol, string member) in indexMember)
            sb.AppendLine($"        {protocol.ToString(CultureInfo.InvariantCulture)} => LangTables.{member},");

        sb.AppendLine("        _ => default,");
        sb.AppendLine("    };");
        sb.AppendLine("}");
        files["JavaLanguage.g.cs"] = new EmittedFile("JavaLanguage.g.cs", sb.ToString());
    }

    private static void LangHeader(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// Generated by Umpk.DataGen from data/java. Do not edit by hand.");
        sb.AppendLine("// Regenerate: dotnet run --project tools/Umpk.DataGen -- generate --data data/java --out /tmp/gen --out-lang src/Umpk.Data.Lang");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
    }
}
