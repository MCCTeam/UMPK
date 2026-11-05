using System.Text.Json;

namespace Umpk.DataGen;

internal readonly record struct MiningRow(float Value, bool Flag);

/// <summary>Default-stack vanilla mining measurements. Absence means unknown, while an omitted tool rule in a measured table means speed 1 and no special harvest capability.</summary>
internal sealed record MiningTable(
    int Schema,
    string Identity,
    int StateCount,
    int ItemCount,
    MiningRow DefaultTool,
    IReadOnlyDictionary<string, MiningRow> Blocks,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, MiningRow>> Tools)
{
    public static MiningTable? Load(VersionData version)
    {
        if (!version.RawFiles.TryGetValue("block-mining.json", out JsonElement root))
            return null;

        Dictionary<string, MiningRow> blocks = [];
        foreach (JsonProperty block in root.GetProperty("blocks").EnumerateObject())
            blocks.Add(block.Name, ReadRow(block.Value));
        Dictionary<string, IReadOnlyDictionary<string, MiningRow>> tools = [];
        foreach (JsonProperty tool in root.GetProperty("tools").EnumerateObject())
        {
            Dictionary<string, MiningRow> rules = [];
            foreach (JsonProperty block in tool.Value.EnumerateObject())
                rules.Add(block.Name, ReadRow(block.Value));
            tools.Add(tool.Name, rules);
        }
        return new MiningTable(
            root.GetProperty("schema").GetInt32(),
            root.GetProperty("identity").GetString()!,
            root.GetProperty("state_count").GetInt32(),
            root.GetProperty("item_count").GetInt32(),
            ReadRow(root.GetProperty("default_tool")), blocks, tools);
    }

    private static MiningRow ReadRow(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 2)
            throw new DatasetException("block-mining.json rows must contain exactly one number and one boolean");
        return new MiningRow(row[0].GetSingle(), row[1].GetBoolean());
    }
}
