using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Umpk.Text;

/// <summary>An immutable, ordinal-keyed translation table: key -&gt; format template, backed by a <see cref="FrozenDictionary{TKey, TValue}"/> for allocation-free, read-optimized lookups.</summary>
/// <remarks>This is the concrete <see cref="ITranslationSource"/> a host assembles from real data (a vanilla per-protocol <c>en_us</c> table, a server resource-pack override, a runtime-authored fallback set). <see cref="Umpk.Text"/> ships the mechanism; the data comes from wherever the host gets it (for vanilla data specifically, <c>Umpk.Data.Lang.VanillaTranslations</c>).</remarks>
public sealed class TranslationTable : ITranslationSource
{
    private readonly FrozenDictionary<string, string> _entries;

    private TranslationTable(FrozenDictionary<string, string> entries) => _entries = entries;

    /// <summary>A table that resolves nothing. <see cref="Count"/> is 0.</summary>
    public static readonly TranslationTable Empty = new(FrozenDictionary<string, string>.Empty);

    /// <summary>Builds a table from (key, template) pairs. Keys compare ordinally. When the same key appears more than once in <paramref name="entries"/>, the LAST occurrence in enumeration order wins, the same rule a JSON object with a repeated property name resolves under.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    public static TranslationTable FromEntries(IEnumerable<KeyValuePair<string, string>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in entries)
        {
            map[entry.Key] = entry.Value; // last duplicate wins
        }

        return new TranslationTable(map.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>The number of distinct keys this table resolves.</summary>
    public int Count => _entries.Count;

    /// <inheritdoc/>
    public bool TryResolve(string key, [NotNullWhen(true)] out string? template)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.TryGetValue(key, out template);
    }
}
