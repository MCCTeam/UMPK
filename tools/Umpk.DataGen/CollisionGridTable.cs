using System.Globalization;
using System.Text.Json;

namespace Umpk.DataGen;

internal sealed record CollisionGridRow(bool Canonical, double[][] Planes, int[] Cells, double[][] Boxes, double MaxHorizontalOffset = 0);

/// <summary>Exact native default collision grids. Absence means the protocol has no measured grid table.</summary>
internal sealed record CollisionGridTable(int Schema, int StateCount, string CellOrder, IReadOnlyDictionary<string, int> States, IReadOnlyList<CollisionGridRow> Groups, bool Offsets = false)
{
    internal static CollisionGridTable? Load(VersionData version) => Load(version, offsets: false);
    internal static CollisionGridTable? LoadOffsets(VersionData version) => Load(version, offsets: true);

    private static CollisionGridTable? Load(VersionData version, bool offsets)
    {
        if (!version.RawFiles.TryGetValue(offsets ? "block-collision-offsets.json" : "block-collision-grids.json", out JsonElement root))
            return null;
        Dictionary<string, int> states = [];
        foreach (JsonProperty state in root.GetProperty("states").EnumerateObject())
            states.Add(state.Name, state.Value.GetInt32());
        List<CollisionGridRow> groups = [];
        foreach (JsonElement group in root.GetProperty("groups").EnumerateArray())
            groups.Add(new CollisionGridRow(group.GetProperty("canonical_full_block").GetBoolean(), Matrix(group.GetProperty("planes")), [.. group.GetProperty("cells").EnumerateArray().Select(value => value.GetInt32())], Matrix(group.GetProperty("boxes")), group.TryGetProperty("max_horizontal_offset", out JsonElement limit) ? limit.GetDouble() : 0));
        return new CollisionGridTable(root.GetProperty("schema").GetInt32(), root.GetProperty("state_count").GetInt32(), root.GetProperty("cell_order").GetString()!, states, groups, offsets);
    }

    private static double[][] Matrix(JsonElement matrix) => [.. matrix.EnumerateArray().Select(row => row.EnumerateArray().Select(value => value.GetDouble()).ToArray())];

    internal void Validate(VersionData version, List<string> problems)
    {
        void Reject(string text) => problems.Add($"proto {version.Protocol}: {(Offsets ? "block-collision-offsets.json" : "block-collision-grids.json")} {text}");
        if (Offsets && !version.RawFiles.ContainsKey("block-collision-grids.json"))
            Reject("requires the native default collision-grid table");
        int expected = version.Blocks.Blocks.Sum(block => block.NumStates ?? 1);
        if (Schema != 1 || version.Identity != "flat" || StateCount != expected || StateCount <= 0)
            Reject("schema, identity or state count is invalid");
        if (CellOrder != "(x * y_size + y) * z_size + z")
            Reject("cell order is invalid");
        if (!Offsets && (States.Count != StateCount || Enumerable.Range(0, Math.Max(0, StateCount)).Any(index => !States.ContainsKey(index.ToString(CultureInfo.InvariantCulture)))))
            Reject("state references must cover the contiguous native registry");
        if (Offsets && (States.Count == 0 || States.Keys.Any(key => !int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 0 || id >= StateCount || key != id.ToString(CultureInfo.InvariantCulture))))
            Reject("offset state references must be canonical ids within the native registry");
        if (Groups.Count == 0 || States.Values.Any(index => (uint)index >= (uint)Groups.Count) || States.Values.Distinct().Count() != Groups.Count)
            Reject("shape references are missing, invalid or unused");
        for (int index = 0; index < Groups.Count; index++)
        {
            CollisionGridRow group = Groups[index];
            if (!double.IsFinite(group.MaxHorizontalOffset) || (Offsets ? group.MaxHorizontalOffset <= 0 || group.Canonical || group.Cells.Length == 0 : group.MaxHorizontalOffset != 0))
                Reject($"group {index} has an invalid position-offset contract");
            if (group.Planes.Length == 3 && group.Planes.All(axis => axis.Length == 0))
            {
                if (group.Canonical || group.Cells.Length != 0 || group.Boxes.Length != 0)
                    Reject($"group {index} absent coordinate planes require an empty noncanonical shape");
                continue;
            }
            if (group.Planes.Length != 3 || group.Planes.Any(axis => axis.Length == 0 || axis.Any(value => !double.IsFinite(value)) || axis.Zip(axis.Skip(1)).Any(pair => pair.First > pair.Second)))
            {
                Reject($"group {index} has invalid coordinate planes");
                continue;
            }
            int nx = group.Planes[0].Length - 1, ny = group.Planes[1].Length - 1, nz = group.Planes[2].Length - 1;
            long capacity = (long)nx * ny * nz;
            if (capacity > int.MaxValue || group.Cells.Any(cell => cell < 0 || cell >= capacity) || !group.Cells.SequenceEqual(group.Cells.Distinct().Order()))
            {
                Reject($"group {index} has invalid occupied cells");
                continue;
            }
            if (group.Boxes.Any(box => box.Length != 6 || box.Any(value => !double.IsFinite(value)) || Enumerable.Range(0, 3).Any(axis => box[axis] > box[axis + 3]) || Enumerable.Range(0, 6).Any(axis => !group.Planes[axis % 3].Contains(box[axis]))))
            {
                Reject($"group {index} has invalid bounding boxes");
                continue;
            }
            if (group.Canonical && (!group.Planes.All(axis => axis.SequenceEqual(new double[] { 0, 1 })) || !group.Cells.SequenceEqual(new[] { 0 }) || group.Boxes.Length != 1 || !group.Boxes[0].SequenceEqual(new double[] { 0, 0, 0, 1, 1, 1 })))
                Reject($"group {index} has invalid canonical identity");
            HashSet<int> occupied = [.. group.Cells];
            bool[] covered = new bool[group.Boxes.Length];
            bool agrees = true;
            for (int x = 0; x < nx; x++)
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                    {
                        bool full = occupied.Contains((x * ny + y) * nz + z);
                        bool positive = group.Planes[0][x] < group.Planes[0][x + 1] && group.Planes[1][y] < group.Planes[1][y + 1] && group.Planes[2][z] < group.Planes[2][z + 1];
                        bool any = false;
                        for (int boxIndex = 0; boxIndex < group.Boxes.Length; boxIndex++)
                        {
                            double[] box = group.Boxes[boxIndex];
                            bool covers = box[0] <= group.Planes[0][x] && group.Planes[0][x + 1] <= box[3]
                                && box[1] <= group.Planes[1][y] && group.Planes[1][y + 1] <= box[4]
                                && box[2] <= group.Planes[2][z] && group.Planes[2][z + 1] <= box[5];
                            any |= covers;
                            covered[boxIndex] |= full && covers;
                        }
                        agrees &= full ? any : !positive || !any;
                    }
            if (!agrees || covered.Any(value => !value))
                Reject($"group {index} bounding boxes disagree with occupied cells");
        }
    }
}
