using System.Text.Json;

namespace Umpk.DataGen;

internal readonly record struct EntityGeometryVariant(int Selector, int Pose, float Width, float Height, bool Fixed, bool BlocksBuilding);
internal sealed record EntityGeometryEntry(int MetadataIndex, string SelectorKind, int Mask, int DefaultValue, int PoseIndex, string Refresh, string ScaleAttribute, EntityGeometryVariant[] Variants, string RefreshTiming = "synchronized_data", int RequiredLoadedRadius = 0);

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
            entries.Add(row.Name, new EntityGeometryEntry(selector[0].GetInt32(), selector[1].GetString()!, selector[2].GetInt32(), selector[3].GetInt32(), value.GetProperty("pose_index").GetInt32(), value.GetProperty("refresh").GetString()!, value.GetProperty("scale_attribute").GetString()!, variants,
                value.TryGetProperty("refresh_timing", out JsonElement timing) ? timing.GetString()! : "synchronized_data",
                value.TryGetProperty("required_loaded_radius", out JsonElement radius) ? radius.GetInt32() : 0));
        }
        return new EntityGeometryTable(root.GetProperty("schema").GetInt32(), entries);
    }

    public void Validate(VersionData version, List<string> problems)
    {
        void Reject(string detail) => problems.Add($"proto {version.Protocol}: entity-geometry.json {detail}");
        if (Schema != 1 || Entities.Count == 0)
            Reject("has an unsupported schema or no measured entities");
        HashSet<string> known = [.. version.Entities.Concat(version.ObjectEntities).Select(entity => entity.Name.Contains(':', StringComparison.Ordinal) ? entity.Name : "minecraft:" + entity.Name)];
        foreach ((string name, EntityGeometryEntry entry) in Entities)
        {
            if (!known.Contains(name))
                Reject($"names an unknown entity {name}");
            bool booleanSelector = entry.MetadataIndex is >= 0 and <= 254 && entry.SelectorKind == "boolean" && entry.Mask == 1 && entry.DefaultValue == 0;
            bool negativeByteSelector = entry.MetadataIndex is >= 0 and <= 254 && entry.SelectorKind == "negative_byte" && entry.Mask == 128 && entry.DefaultValue == 0;
            bool noSelector = entry.MetadataIndex == -1 && entry.SelectorKind == "none" && entry.Mask == 0 && entry.DefaultValue == 0;
            if ((!booleanSelector && !negativeByteSelector && !noSelector) || entry.PoseIndex is < -1 or > 254 || (entry.MetadataIndex >= 0 && entry.MetadataIndex == entry.PoseIndex))
                Reject($"has an unsupported metadata selector for {name}");
            if (entry.RefreshTiming is not ("synchronized_data" or "living_tick") || entry.RequiredLoadedRadius is < 0 or > 32 || (entry.RefreshTiming == "synchronized_data" && entry.RequiredLoadedRadius != 0))
                Reject($"has an unsupported dimensions refresh timing or loaded radius for {name}");
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
            bool complete = noSelector
                ? seen.SetEquals(poses.Select(pose => (0, pose)))
                : seen.SetEquals(poses.SelectMany(pose => new[] { (0, pose), (1, pose) }));
            if (!poses.Contains(0) || !complete || (entry.PoseIndex == -1 && (poses.Count != 1 || !poses.Contains(0))))
                Reject($"has incomplete selector or pose coverage for {name}");
        }
    }
}
