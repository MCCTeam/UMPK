using System.Text.Json;

namespace Umpk.DataGen;

internal readonly record struct BedFacingEntry(int State, int Facing);

/// <summary>Original bed-facing properties, including every raw legacy metadata alias.</summary>
internal sealed record BedFacingTable(int Schema, string Identity, int StateCount, IReadOnlyList<BedFacingEntry> States, bool ValidRows)
{
    public static BedFacingTable? Load(VersionData version)
    {
        if (!version.RawFiles.TryGetValue("bed-facings.json", out JsonElement root))
            return null;
        List<BedFacingEntry> states = [];
        JsonElement rows = root.GetProperty("states");
        bool valid = rows.ValueKind == JsonValueKind.Array;
        if (valid)
            foreach (JsonElement row in rows.EnumerateArray())
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 2 || row[0].ValueKind != JsonValueKind.Number || row[1].ValueKind != JsonValueKind.Number || !row[0].TryGetInt32(out int state) || !row[1].TryGetInt32(out int facing))
                    valid = false;
                else
                    states.Add(new BedFacingEntry(state, facing));
        return new BedFacingTable(root.GetProperty("schema").GetInt32(), root.GetProperty("identity").GetString()!, root.GetProperty("state_count").GetInt32(), states, valid);
    }

    public void Validate(VersionData version, List<string> problems)
    {
        void Reject(string detail) => problems.Add($"proto {version.Protocol}: bed-facings.json {detail}");
        if (Schema != 1 || Identity != version.Blocks.Identity || !ValidRows)
            Reject("has an unsupported schema, identity or state row");
        bool legacy = version.Blocks.Identity == "legacy";
        BlockEntry[] beds = [.. version.Blocks.Blocks.Where(block => block.Name is "bed" or "minecraft:bed" || block.Name?.EndsWith("_bed", StringComparison.Ordinal) == true)];
        Dictionary<int, int> bedForState = [];
        for (int index = 0; index < beds.Length; index++)
        {
            BlockEntry bed = beds[index];
            int first = legacy ? bed.BlockId!.Value << 4 : bed.MinState!.Value;
            int count = legacy ? 16 : bed.NumStates!.Value;
            if (count != 16)
                Reject("has an unsupported bed state count");
            for (int state = first; state < first + count; state++)
                if (!bedForState.TryAdd(state, index))
                    Reject("has overlapping bed state ranges");
        }
        Dictionary<int, int[]> counts = [];
        HashSet<int> seen = [];
        int previous = -1;
        foreach (BedFacingEntry row in States)
        {
            if (row.State <= previous || row.Facing is < 2 or > 5 || !bedForState.TryGetValue(row.State, out int bed) || !seen.Add(row.State))
                Reject("has an unknown, unordered, repeated or nonhorizontal state");
            else
            {
                if (!counts.TryGetValue(bed, out int[]? directions))
                    counts.Add(bed, directions = new int[4]);
                directions[row.Facing - 2]++;
            }
            previous = row.State;
        }
        if (beds.Length == 0 || StateCount != States.Count || !seen.SetEquals(bedForState.Keys) || counts.Count != beds.Length || counts.Values.Any(directions => directions.Any(count => count != 4)))
            Reject("must cover every bed state and its four horizontal facings exactly once");
    }
}
