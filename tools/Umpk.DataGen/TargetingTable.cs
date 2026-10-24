using System.Text.Json;

namespace Umpk.DataGen;

internal sealed record TargetingShape(IReadOnlyList<double[]> Boxes, string Offset, double Horizontal, double Vertical, string ClipModel);
internal readonly record struct TargetingRange(int First, int Count, int Outline, int Interaction, string HeldItem, int HeldOutline);
internal readonly record struct TargetingContextRange(int First, int Count, int Target);
internal sealed record TargetingContextNode(int[] Offset, IReadOnlyList<TargetingContextRange> Ranges);
internal sealed record TargetingTable(int Schema, string ClipModel, int StateCount, IReadOnlyList<TargetingShape> Shapes, IReadOnlyList<TargetingRange> Ranges, IReadOnlyList<TargetingContextNode> ContextNodes, IReadOnlyList<TargetingContextRange> ContextRoots)
{
    public static TargetingTable? Load(VersionData version)
    {
        if (!version.RawFiles.TryGetValue("block-targeting.json", out JsonElement root))
            return null;
        List<TargetingShape> shapes = [];
        foreach (JsonElement shape in root.GetProperty("shape_pool").EnumerateArray())
            shapes.Add(new TargetingShape([.. shape.GetProperty("boxes").EnumerateArray().Select(box => box.EnumerateArray().Select(value => value.GetDouble()).ToArray())],
                shape.GetProperty("offset").GetString()!, shape.GetProperty("max_horizontal_offset").GetDouble(), shape.GetProperty("max_vertical_offset").GetDouble(), shape.TryGetProperty("clip_model", out JsonElement model) ? model.GetString()! : ""));
        List<TargetingRange> ranges = [];
        foreach (JsonElement row in root.GetProperty("state_refs").EnumerateArray())
        {
            if (row.GetArrayLength() != 6)
                throw new DatasetException("block-targeting.json reference rows require six fields");
            ranges.Add(new TargetingRange(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32(), row[4].GetString()!, row[5].GetInt32()));
        }
        List<TargetingContextNode> nodes = [];
        if (root.TryGetProperty("context_nodes", out JsonElement contextNodes))
            foreach (JsonElement node in contextNodes.EnumerateArray())
                nodes.Add(new TargetingContextNode([.. node.GetProperty("offset").EnumerateArray().Select(value => value.GetInt32())], ReadContextRanges(node.GetProperty("ranges"))));
        IReadOnlyList<TargetingContextRange> roots = root.TryGetProperty("context_roots", out JsonElement contextRoots) ? ReadContextRanges(contextRoots) : [];
        return new TargetingTable(root.GetProperty("schema").GetInt32(), root.TryGetProperty("clip_model", out JsonElement clip) ? clip.GetString()! : "voxel", root.GetProperty("state_count").GetInt32(), shapes, ranges, nodes, roots);
    }

    private static List<TargetingContextRange> ReadContextRanges(JsonElement rows)
    {
        List<TargetingContextRange> result = [];
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.GetArrayLength() != 3)
                throw new DatasetException("block-targeting.json context ranges require three fields");
            result.Add(new TargetingContextRange(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32()));
        }
        return result;
    }

    public void Validate(VersionData version, List<string> problems)
    {
        void Reject(string message) => problems.Add($"proto {version.Protocol}: block-targeting.json {message}");
        int expectedStates = version.Blocks.Identity == "legacy" ? (version.Blocks.Blocks.Max(block => block.BlockId ?? -1) + 1) * 16 : version.Blocks.Blocks.Sum(block => block.NumStates ?? 1);
        if (Schema != 1 || version.Blocks.Identity is not ("flat" or "legacy") || StateCount != expectedStates)
            Reject("schema, identity or state count is invalid");
        if (ClipModel is not ("aabb" or "voxel_forward" or "voxel"))
            Reject("has an unsupported ray clip model");
        foreach (TargetingShape shape in Shapes)
        {
            if (shape.ClipModel is not ("" or "aabb_list"))
                Reject("has an unsupported per-shape clip model");
            if (shape.Offset is not ("none" or "xz" or "xyz") || !double.IsFinite(shape.Horizontal) || !double.IsFinite(shape.Vertical) || shape.Horizontal < 0 || shape.Vertical < 0)
                Reject("has an invalid offset model or limit");
            foreach (double[] box in shape.Boxes)
                if (box.Length != 6 || box.Any(value => !double.IsFinite(value)) || Enumerable.Range(0, 3).Any(axis => box[axis] > box[axis + 3]))
                    Reject("has invalid box coordinates");
        }
        HashSet<string> items = [.. version.Items.Select(item => item.Name)];
        long next = 0;
        foreach (TargetingRange range in Ranges)
        {
            if (range.First != next || range.Count <= 0)
                Reject("state ranges overlap, have a gap or have non-positive length");
            next = (long)range.First + range.Count;
            if ((uint)range.Outline >= (uint)Shapes.Count || (uint)range.Interaction >= (uint)Shapes.Count || (uint)range.HeldOutline >= (uint)Shapes.Count)
                Reject("has a dangling shape reference");
            if (range.HeldItem.Length == 0 ? range.HeldOutline != range.Outline : !items.Contains(range.HeldItem))
                Reject("has an invalid held-item shape selector");
        }
        if (next != StateCount)
            Reject("ranges do not cover every state");
        for (int index = 0; index < ContextNodes.Count; index++)
        {
            TargetingContextNode node = ContextNodes[index];
            if (node.Offset.Length != 3 || node.Offset.Any(value => value is < -1 or > 1) || node.Offset.All(value => value == 0))
                Reject("context offset is not an immediate neighbor");
            long end = 0;
            foreach (TargetingContextRange range in node.Ranges)
            {
                if (range.First != end || range.Count <= 0)
                    Reject("context coverage overlaps or has a gap");
                end = (long)range.First + range.Count;
                if (range.Target >= 0 ? range.Target >= Shapes.Count : ~range.Target >= ContextNodes.Count)
                    Reject("context target is out of range");
                if (range.Target < 0 && ~range.Target <= index)
                    Reject("context cycle or backward edge is forbidden");
            }
            if (end != StateCount)
                Reject("context coverage does not include every neighbor state");
        }
        long rootEnd = 0;
        foreach (TargetingContextRange root in ContextRoots)
        {
            if (root.First < rootEnd || root.Count <= 0 || (long)root.First + root.Count > StateCount || (uint)root.Target >= (uint)ContextNodes.Count)
                Reject("context root is invalid or overlaps another root");
            rootEnd = (long)root.First + root.Count;
        }
    }
}
