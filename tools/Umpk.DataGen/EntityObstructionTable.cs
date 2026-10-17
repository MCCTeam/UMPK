using System.Text.Json;

namespace Umpk.DataGen;

/// <summary>Native entity types whose audited building flag is always false. Other types retain unknown obstruction rules.</summary>
internal sealed record EntityObstructionTable(int Schema, IReadOnlyDictionary<string, int> Entities, IReadOnlyDictionary<string, (float Width, float Height)> FixedBoxes, bool FixedBoxesValid)
{
    public static EntityObstructionTable? Load(VersionData version)
    {
        if (!version.RawFiles.TryGetValue("entity-obstruction.json", out JsonElement root))
            return null;
        Dictionary<string, int> entities = [];
        foreach (JsonProperty entity in root.GetProperty("entities").EnumerateObject())
            entities.Add(entity.Name, entity.Value.GetInt32());
        Dictionary<string, (float Width, float Height)> boxes = [];
        bool valid = true;
        if (root.TryGetProperty("fixed_boxes", out JsonElement fixedBoxes))
        {
            if (fixedBoxes.ValueKind != JsonValueKind.Object)
                valid = false;
            else
                foreach (JsonProperty box in fixedBoxes.EnumerateObject())
                {
                    if (box.Value.ValueKind != JsonValueKind.Array || box.Value.GetArrayLength() != 2 || box.Value[0].ValueKind != JsonValueKind.Number || box.Value[1].ValueKind != JsonValueKind.Number || !box.Value[0].TryGetSingle(out float width) || !box.Value[1].TryGetSingle(out float height))
                        valid = false;
                    else
                        boxes.Add(box.Name, (width, height));
                }
        }
        return new EntityObstructionTable(root.GetProperty("schema").GetInt32(), entities, boxes, valid);
    }

    public void Validate(VersionData version, List<string> problems)
    {
        if (Schema != 1 || Entities.Values.Any(flags => flags is < 0 or > 1))
            problems.Add($"proto {version.Protocol}: entity-obstruction.json has an unsupported schema or rule");
        HashSet<string> expected = [.. version.Entities.Select(entity => entity.Name), .. version.ObjectEntities.Select(entity => entity.Name)];
        if (!expected.SetEquals(Entities.Keys))
            problems.Add($"proto {version.Protocol}: entity-obstruction.json must measure every entity type exactly once");
        if (!FixedBoxesValid || FixedBoxes.Any(box => !Entities.TryGetValue(box.Key, out int flags) || flags != 0 || !float.IsFinite(box.Value.Width) || !float.IsFinite(box.Value.Height) || box.Value.Width <= 0 || box.Value.Height <= 0))
            problems.Add($"proto {version.Protocol}: entity-obstruction.json has an invalid or contradictory fixed box");
    }
}
