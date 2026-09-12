using System.Text;

namespace Umpk.DataGen;

/// <summary>Structured diff of two versions' datasets: added/removed/reordered packets, registries, components, metadata serializers, and block/item changes, including an ID-shift histogram. Diffing packet codecs (not just registration lists) is the point: the 26.2 example shows packet ids can be stable while wire layouts change, so codec changes are called out.</summary>
internal static class Differ
{
    public static string Diff(VersionData older, VersionData newer)
    {
        StringBuilder sb = new();
        sb.AppendLine($"Diff: protocol {older.Protocol} ({older.VersionName}) -> {newer.Protocol} ({newer.VersionName})");
        sb.AppendLine();

        DiffPackets(older, newer, sb);
        DiffRegistryList("Items", older.Items, newer.Items, sb);
        DiffComponents(older, newer, sb);
        DiffMetadata(older, newer, sb);
        DiffBlocks(older, newer, sb);
        return sb.ToString();
    }

    private static void DiffPackets(VersionData older, VersionData newer, StringBuilder sb)
    {
        sb.AppendLine("== Packets ==");
        foreach (string phase in older.Packets.Phases.Keys.Union(newer.Packets.Phases.Keys).OrderBy(x => x))
        {
            IReadOnlyDictionary<string, IReadOnlyList<PacketEntry>> oFlows =
                older.Packets.Phases.GetValueOrDefault(phase) ?? new Dictionary<string, IReadOnlyList<PacketEntry>>();
            IReadOnlyDictionary<string, IReadOnlyList<PacketEntry>> nFlows =
                newer.Packets.Phases.GetValueOrDefault(phase) ?? new Dictionary<string, IReadOnlyList<PacketEntry>>();
            foreach (string flow in oFlows.Keys.Union(nFlows.Keys).OrderBy(x => x))
            {
                List<PacketEntry> o = [.. oFlows.GetValueOrDefault(flow) ?? []];
                List<PacketEntry> n = [.. nFlows.GetValueOrDefault(flow) ?? []];
                Dictionary<string, PacketEntry> oById = o.ToDictionary(p => p.Id);
                Dictionary<string, PacketEntry> nById = n.ToDictionary(p => p.Id);

                List<string> added = [.. nById.Keys.Except(oById.Keys).OrderBy(x => x)];
                List<string> removed = [.. oById.Keys.Except(nById.Keys).OrderBy(x => x)];
                int shifted = 0;
                int codecChanged = 0;
                foreach (string id in oById.Keys.Intersect(nById.Keys))
                {
                    if (oById[id].ProtocolId != nById[id].ProtocolId)
                        shifted++;

                    if (oById[id].Codec != nById[id].Codec)
                        codecChanged++;

                }
                if (added.Count + removed.Count + shifted + codecChanged == 0)
                    continue;

                sb.AppendLine($"  {phase}/{flow}: +{added.Count} -{removed.Count} shifted={shifted} codecChanged={codecChanged}");
                foreach (string a in added)
                    sb.AppendLine($"    + {a} (id {nById[a].ProtocolId}, codec {nById[a].Codec})");

                foreach (string r in removed)
                    sb.AppendLine($"    - {r}");

                foreach (string id in oById.Keys.Intersect(nById.Keys).OrderBy(x => x))
                    if (oById[id].Codec != nById[id].Codec)
                        sb.AppendLine($"    ~ {id} codec {oById[id].Codec} -> {nById[id].Codec} (wire layout may have changed)");

            }
        }
        sb.AppendLine();
    }

    private static void DiffRegistryList(string label, IReadOnlyList<RegistryItem> older, IReadOnlyList<RegistryItem> newer, StringBuilder sb)
    {
        sb.AppendLine($"== {label} ==");
        HashSet<string> o = [.. older.Select(i => i.Name)];
        HashSet<string> n = [.. newer.Select(i => i.Name)];
        List<string> added = [.. n.Except(o).OrderBy(x => x)];
        List<string> removed = [.. o.Except(n).OrderBy(x => x)];

        // Legacy composite item tables repeat names across damage values, so key on first occurrence rather than assuming names are unique.
        Dictionary<string, int> oId = FirstById(older);
        Dictionary<string, int> nId = FirstById(newer);
        Dictionary<int, int> shiftHistogram = [];
        foreach (string name in o.Intersect(n))
        {
            int shift = nId[name] - oId[name];
            if (shift != 0)
                shiftHistogram[shift] = shiftHistogram.GetValueOrDefault(shift) + 1;

        }
        sb.AppendLine($"  count {older.Count} -> {newer.Count}; +{added.Count} -{removed.Count}");
        if (shiftHistogram.Count > 0)
        {
            string hist = string.Join(", ", shiftHistogram.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:+#;-#;0}:{kv.Value}"));
            sb.AppendLine($"  id-shift histogram: {hist}");
        }
        foreach (string a in added)
            sb.AppendLine($"    + {a}");

        foreach (string r in removed)
            sb.AppendLine($"    - {r}");

        sb.AppendLine();
    }

    private static Dictionary<string, int> FirstById(IReadOnlyList<RegistryItem> items)
    {
        Dictionary<string, int> map = [];
        foreach (RegistryItem item in items)
            map.TryAdd(item.Name, item.Id);

        return map;
    }

    private static void DiffComponents(VersionData older, VersionData newer, StringBuilder sb)
    {
        DiffRegistryList("Data components",
            older.Components.Select(c => new RegistryItem(c.Id, c.IdNum)).ToList(),
            newer.Components.Select(c => new RegistryItem(c.Id, c.IdNum)).ToList(), sb);
    }

    private static void DiffMetadata(VersionData older, VersionData newer, StringBuilder sb)
    {
        sb.AppendLine("== Metadata serializers ==");
        List<string> o = [.. older.Metadata.Serializers.Select(s => s.Field)];
        List<string> n = [.. newer.Metadata.Serializers.Select(s => s.Field)];
        List<string> added = [.. n.Except(o)];
        List<string> removed = [.. o.Except(n)];
        bool reordered = !o.SequenceEqual(n) && added.Count == 0 && removed.Count == 0;
        sb.AppendLine($"  count {o.Count} -> {n.Count}; +{added.Count} -{removed.Count}; reordered={reordered}");
        foreach (string a in added)
            sb.AppendLine($"    + {a}");

        sb.AppendLine();
    }

    private static void DiffBlocks(VersionData older, VersionData newer, StringBuilder sb)
    {
        sb.AppendLine("== Blocks ==");
        HashSet<string> o = [.. older.Blocks.Blocks.Where(b => b.Name is not null).Select(b => b.Name!)];
        HashSet<string> n = [.. newer.Blocks.Blocks.Where(b => b.Name is not null).Select(b => b.Name!)];
        sb.AppendLine($"  count {older.Blocks.Blocks.Count} -> {newer.Blocks.Blocks.Count}; +{n.Except(o).Count()} -{o.Except(n).Count()}");
        sb.AppendLine();
    }
}
