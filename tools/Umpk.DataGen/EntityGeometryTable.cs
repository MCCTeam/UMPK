using System.Text.Json;

namespace Umpk.DataGen;

internal readonly record struct EntityGeometryVariant(int Selector, int Pose, float Width, float Height, bool Fixed, bool BlocksBuilding);
internal sealed record EntityGeometryEntry(int MetadataIndex, string SelectorKind, int Mask, int DefaultValue, int PoseIndex, string Refresh, string ScaleAttribute, EntityGeometryVariant[] Variants);

/// <summary>Audited native dimensions indexed by synchronized metadata and canonical pose. Unlisted entity types remain unknown.</summary>
internal sealed record EntityGeometryTable(int Schema, IReadOnlyDictionary<string, EntityGeometryEntry> Entities)
{
    public static EntityGeometryTable? Load(VersionData version)
    {
        if (!version.RawFiles.TryGetValue("entity-geometry.json", out JsonElement root))
            return null;
        Dictionary<string, EntityGeometryEntry> entries = [];
        foreach (JsonProperty row in root.GetProperty("entities").EnumerateObject())
        {
            JsonElement value = row.Value, selector = value.GetProperty("selector");
            EntityGeometryVariant[] variants = value.GetProperty("variants").EnumerateArray().Select(variant => new EntityGeometryVariant(variant[0].GetInt32(), variant[1].GetInt32(), variant[2].GetSingle(), variant[3].GetSingle(), variant[4].GetBoolean(), variant[5].GetBoolean())).ToArray();
            entries.Add(row.Name, new EntityGeometryEntry(selector[0].GetInt32(), selector[1].GetString()!, selector[2].GetInt32(), selector[3].GetInt32(), value.GetProperty("pose_index").GetInt32(), value.GetProperty("refresh").GetString()!, value.GetProperty("scale_attribute").GetString()!, variants));
        }
        return new EntityGeometryTable(root.GetProperty("schema").GetInt32(), entries);
    }

    public void Validate(VersionData version, List<string> problems)
    {
        void Reject(string detail) => problems.Add($"proto {version.Protocol}: entity-geometry.json {detail}");
        if (Schema != 1 || Entities.Count == 0)
            Reject("has an unsupported schema or no measured entities");
        HashSet<string> known = [.. version.Entities.Select(entity => entity.Name), .. version.ObjectEntities.Select(entity => entity.Name)];
        foreach ((string name, EntityGeometryEntry entry) in Entities)
        {
            if (!known.Contains(name))
                Reject($"names an unknown entity {name}");
            if (entry.MetadataIndex is < 0 or > 254 || entry.SelectorKind != "boolean" || entry.Mask != 1 || entry.DefaultValue != 0 || entry.PoseIndex is < -1 or > 254 || entry.MetadataIndex == entry.PoseIndex)
                Reject($"has an unsupported metadata selector for {name}");
            int expectedPoseIndex = version.MetadataKeys.EntityKeys.TryGetValue("pose", out int poseIndex) ? poseIndex : -1;
            if (entry.PoseIndex != expectedPoseIndex)
                Reject($"pose metadata differs from metadata-keys.json for {name}");
            if (entry.Refresh is not ("recenter_on_refresh" or "retain_minimum_on_growth" or "retain_minimum") || entry.ScaleAttribute is not ("" or "minecraft:scale"))
                Reject($"has an unsupported refresh or scale rule for {name}");
            HashSet<(int Selector, int Pose)> seen = [];
            foreach (EntityGeometryVariant variant in entry.Variants)
                if (variant.Selector is < 0 or > 1 || variant.Pose is < 0 or > 17 || !float.IsFinite(variant.Width) || !float.IsFinite(variant.Height) || variant.Width <= 0 || variant.Height <= 0 || !seen.Add((variant.Selector, variant.Pose)))
                    Reject($"has invalid or repeated dimensions for {name}");
            HashSet<int> poses = [.. seen.Select(key => key.Pose)];
            if (!poses.Contains(0) || !seen.SetEquals(poses.SelectMany(pose => new[] { (0, pose), (1, pose) })) || (entry.PoseIndex == -1 && (poses.Count != 1 || !poses.Contains(0))))
                Reject($"has incomplete selector or pose coverage for {name}");
        }
    }
}
