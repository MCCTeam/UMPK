using System.Globalization;
using System.Text.Json;

namespace Umpk.DataGen;

internal readonly record struct ReplacementRange(int First, int Last, int Flags);

/// <summary>Native ordinary-cube replacement predicates, including the measured legacy lower-block dependency.</summary>
internal sealed record ReplacementTable(int Schema, string Identity, int StateCount, ReplacementRange[] Ranges, IReadOnlyDictionary<int, ReplacementRange[]> BelowOverrides, bool ValidRows)
{
    public static ReplacementTable? Load(VersionData version)
    {
        if (!version.RawFiles.TryGetValue("block-replacement.json", out JsonElement root))
            return null;
        bool valid = true;
        ReplacementRange[] ReadRanges(JsonElement values)
        {
            List<ReplacementRange> result = [];
            if (values.ValueKind != JsonValueKind.Array)
                valid = false;
            else
                foreach (JsonElement row in values.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 3 || row.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.Number) || !row[0].TryGetInt32(out int first) || !row[1].TryGetInt32(out int last) || !row[2].TryGetInt32(out int flags))
                        valid = false;
                    else
                        result.Add(new ReplacementRange(first, last, flags));
                }
            return [.. result];
        }
        ReplacementRange[] ranges = ReadRanges(root.GetProperty("ranges"));
        Dictionary<int, ReplacementRange[]> overrides = [];
        JsonElement below = root.GetProperty("below_overrides");
        if (below.ValueKind != JsonValueKind.Object)
            valid = false;
        else
            foreach (JsonProperty row in below.EnumerateObject())
            {
                if (!int.TryParse(row.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int state) || state < 0 || state.ToString(CultureInfo.InvariantCulture) != row.Name || !overrides.TryAdd(state, ReadRanges(row.Value)))
                    valid = false;
            }
        return new ReplacementTable(root.GetProperty("schema").GetInt32(), root.GetProperty("identity").GetString()!, root.GetProperty("state_count").GetInt32(), ranges, overrides, valid);
    }

    public void Validate(VersionData version, List<string> problems)
    {
        void Reject(string detail) => problems.Add($"proto {version.Protocol}: block-replacement.json {detail}");
        if (Schema != 1 || Identity != version.Blocks.Identity || !ValidRows)
            Reject("has an unsupported schema, identity or range row");
        bool legacy = version.Blocks.Identity == "legacy";
        HashSet<int> expected = legacy
            ? [.. version.Blocks.Blocks.SelectMany(block => Enumerable.Range(block.BlockId!.Value << 4, 16))]
            : [.. version.Blocks.Blocks.SelectMany(block => Enumerable.Range(block.MinState!.Value, block.NumStates!.Value))];
        int maximum = expected.Count == 0 ? -1 : expected.Max();
        Dictionary<int, int> covered = [];
        void ValidateRanges(ReplacementRange[] ranges, Dictionary<int, int> destination)
        {
            if (ranges.Length == 0)
                Reject("has an empty range list");
            int previous = -1, previousFlags = -1;
            foreach (ReplacementRange range in ranges)
            {
                if (range.First < 0 || range.Last < range.First || range.Last > maximum || range.First <= previous || range.Flags is < 0 or > 3 || (range.First == previous + 1 && range.Flags == previousFlags))
                {
                    Reject("has overlapping, uncompacted or invalid ranges");
                    continue;
                }
                for (int state = range.First; state <= range.Last; state++)
                    if (!expected.Contains(state) || !destination.TryAdd(state, range.Flags))
                        Reject("contains an unknown or repeated state");
                previous = range.Last;
                previousFlags = range.Flags;
            }
        }
        ValidateRanges(Ranges, covered);
        if (StateCount != covered.Count || (!legacy && !expected.SetEquals(covered.Keys)) || (legacy && !version.Blocks.Blocks.Select(block => block.BlockId!.Value).ToHashSet().SetEquals(covered.Keys.Select(state => state >> 4))))
            Reject("state count or registry coverage differs from its native registry");
        if (legacy && version.RawFiles.TryGetValue("block-interactions.json", out JsonElement interaction) && interaction.TryGetProperty("measured_state_count", out JsonElement measured) && measured.GetInt32() != StateCount)
            Reject("state count differs from the measured interaction registry");
        if (!legacy && BelowOverrides.Count != 0)
            Reject("has an unmeasured modern neighbor dependency");
        foreach ((int state, ReplacementRange[] ranges) in BelowOverrides)
        {
            Dictionary<int, int> lower = [];
            ValidateRanges(ranges, lower);
            if (!covered.TryGetValue(state, out int flags) || lower.Any(row => !covered.ContainsKey(row.Key) || row.Value == flags))
                Reject("has an unknown target, unknown lower state or redundant override");
        }
    }
}
