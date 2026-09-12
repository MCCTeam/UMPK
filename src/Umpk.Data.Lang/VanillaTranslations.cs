using System.Collections.Concurrent;
using System.Text;
using Umpk.Text;

namespace Umpk.Data.Lang;

/// <summary>The <c>en_us</c> translation table for each supported protocol.</summary>
/// <remarks>
/// <para>One table per protocol, not one shared table, because argument arity genuinely drifts across eras for the SAME key, and not only in the pre-flattening bands: 1.13's <c>commands.spawnpoint.success.single</c> takes four positional arguments where 26.2's takes six, and <c>menu.preparingSpawn</c> is "Preparing spawn area" through 1.13 but gained a <c>%s%%</c> progress suffix at 1.14.4 (protocol 498) that 1.13 never had. Resolving a band's chat against another era's template is not a cosmetic mismatch; it can render a truncated or wrongly-argumented line.</para>
/// <para>Each table is materialized from the shared pool and that protocol's delta-packed index on first use, then cached. Indexing the sorted pool directly could avoid allocations on misses, but the simpler dictionary representation is retained until profiling shows a need to change it.</para>
/// </remarks>
public static class VanillaTranslations
{
    // Build and cache one translation source per protocol on demand.
    private static readonly ConcurrentDictionary<int, ITranslationSource> Cache = new();

    // The shared (key, template) pool, decoded from JavaLanguage.Pool exactly once regardless of how many protocols end up materialized, since every protocol's index only ever references INTO it.
    private static readonly Lazy<(string Key, string Template)[]> LazyPool =
        new(BuildPool, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The vanilla <c>en_us</c> translation table for <paramref name="protocol"/>, or a source that resolves nothing when <paramref name="protocol"/> is not one of the shipped tables (see <see cref="Protocols"/>). Materialized on first use and cached; safe to call repeatedly and from multiple threads.</summary>
    public static ITranslationSource ForProtocol(int protocol) =>
        Cache.GetOrAdd(protocol, static p => Materialize(p));

    /// <summary>The newest shipped protocol's translation table.</summary>
    public static ITranslationSource Latest => ForProtocol(Protocols[^1]);

    /// <summary>Every protocol with a shipped table, ascending. 49 entries.</summary>
    public static ReadOnlySpan<int> Protocols => JavaLanguage.Protocols;

    /// <summary>The number of entries <paramref name="protocol"/>'s table carries, or 0 when unknown.</summary>
    /// <remarks>Reads only the VarInt entry count out of that protocol's index blob; it does not materialize (or touch the cache for) the table, so calling this alone never pays the pool-parsing cost.</remarks>
    public static int CountFor(int protocol)
    {
        ReadOnlySpan<byte> index = JavaLanguage.Index(protocol);
        if (index.IsEmpty)
            return 0;

        int pos = 0;
        return ReadVarInt(index, ref pos);
    }

    private static ITranslationSource Materialize(int protocol)
    {
        ReadOnlySpan<byte> index = JavaLanguage.Index(protocol);
        if (index.IsEmpty)
            return NullTranslationSource.Instance;

        (string Key, string Template)[] pool = LazyPool.Value;

        int pos = 0;
        int count = ReadVarInt(index, ref pos);
        var entries = new KeyValuePair<string, string>[count];
        int running = 0;
        for (int i = 0; i < count; i++)
        {
            running += ReadVarInt(index, ref pos);
            (string key, string template) = pool[running];
            entries[i] = new KeyValuePair<string, string>(key, template);
        }

        return TranslationTable.FromEntries(entries);
    }

    /// <summary>Decodes the shared pool: <c>[VarInt count][per pair: VarInt keyLen, key utf8, VarInt tmplLen, tmpl utf8]</c>, matching <c>Umpk.DataGen</c>'s <c>PackLangPool</c> format.</summary>
    private static (string Key, string Template)[] BuildPool()
    {
        ReadOnlySpan<byte> pool = JavaLanguage.Pool;
        int pos = 0;
        int count = ReadVarInt(pool, ref pos);
        var entries = new (string Key, string Template)[count];
        for (int i = 0; i < count; i++)
        {
            int keyLen = ReadVarInt(pool, ref pos);
            string key = Encoding.UTF8.GetString(pool.Slice(pos, keyLen));
            pos += keyLen;
            int tmplLen = ReadVarInt(pool, ref pos);
            string template = Encoding.UTF8.GetString(pool.Slice(pos, tmplLen));
            pos += tmplLen;
            entries[i] = (key, template);
        }

        return entries;
    }

    private static int ReadVarInt(ReadOnlySpan<byte> data, ref int pos)
    {
        int result = 0;
        int shift = 0;
        while (true)
        {
            byte b = data[pos++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;

            shift += 7;
        }
    }
}
