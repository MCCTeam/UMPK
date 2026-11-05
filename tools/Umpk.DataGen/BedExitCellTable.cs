using System.Text.Json;

namespace Umpk.DataGen;

/// <summary>Native bed-exit support/material predicates and explicitly rejected legacy metadata.</summary>
internal sealed record BedExitCellTable(int Schema, string Identity, int StateCount, ReplacementRange[] Ranges, int[] RejectedStates, bool ValidRows)
{
    public static BedExitCellTable? Load(VersionData version)
    {
        if (!version.RawFiles.TryGetValue("bed-exit-cells.json", out JsonElement root))
            return null;
        bool valid = true;
        List<ReplacementRange> ranges = [];
        JsonElement rows = root.GetProperty("ranges");
        if (rows.ValueKind != JsonValueKind.Array)
            valid = false;
        else
            foreach (JsonElement row in rows.EnumerateArray())
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 3 || row.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.Number) || !row[0].TryGetInt32(out int first) || !row[1].TryGetInt32(out int last) || !row[2].TryGetInt32(out int flags))
                    valid = false;
                else
                    ranges.Add(new ReplacementRange(first, last, flags));
        List<int> rejected = [];
        JsonElement rejectedRows = root.GetProperty("rejected_states");
        if (rejectedRows.ValueKind != JsonValueKind.Array)
            valid = false;
        else
            foreach (JsonElement row in rejectedRows.EnumerateArray())
                if (row.ValueKind != JsonValueKind.Number || !row.TryGetInt32(out int state))
                    valid = false;
                else
                    rejected.Add(state);
        return new BedExitCellTable(root.GetProperty("schema").GetInt32(), root.GetProperty("identity").GetString()!, root.GetProperty("state_count").GetInt32(), [.. ranges], [.. rejected], valid);
    }

    public void Validate(VersionData version, List<string> problems)
    {
        void Reject(string detail) => problems.Add($"proto {version.Protocol}: bed-exit-cells.json {detail}");
        if (Schema != 1 || Identity != version.Blocks.Identity || !ValidRows)
            Reject("has an unsupported schema, identity or row");
        bool legacy = version.Blocks.Identity == "legacy";
        HashSet<int> expected = legacy
            ? [.. version.Blocks.Blocks.SelectMany(block => Enumerable.Range(block.BlockId!.Value << 4, 16))]
            : [.. version.Blocks.Blocks.SelectMany(block => Enumerable.Range(block.MinState!.Value, block.NumStates!.Value))];
        int maximum = expected.Count == 0 ? -1 : expected.Max();
        HashSet<int> known = [];
        int previous = -1, previousFlags = -1;
        foreach (ReplacementRange range in Ranges)
        {
            if (range.First < 0 || range.Last < range.First || range.Last > maximum || range.First <= previous || range.Flags is < 0 or > 3 || (range.First == previous + 1 && range.Flags == previousFlags))
            {
                Reject("has overlapping, uncompacted or invalid ranges");
                continue;
            }
            for (int state = range.First; state <= range.Last; state++)
                if (!expected.Contains(state) || !known.Add(state))
                    Reject("contains an unknown or repeated measured state");
            previous = range.Last;
            previousFlags = range.Flags;
        }
        HashSet<int> rejected = [];
        previous = -1;
        foreach (int state in RejectedStates)
        {
            if (!legacy || state <= previous || !expected.Contains(state) || known.Contains(state) || !rejected.Add(state))
                Reject("has an unsupported, overlapping or unordered metadata rejection");
            previous = state;
        }
        if (known.Count == 0 || StateCount != known.Count || !expected.SetEquals(known.Concat(rejected)))
            Reject("must partition the complete registry into measured and natively rejected states");
    }
}
