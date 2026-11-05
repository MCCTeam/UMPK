using System.Text.Json;

namespace Umpk.DataGen;

internal sealed record DefaultMiningRule(string? Tag, IReadOnlyList<string> Blocks, float? Speed, bool? Correct);
internal sealed record DefaultMiningTool(float Speed, int Damage, bool Creative, IReadOnlyList<DefaultMiningRule> Rules);

internal static class DefaultMiningTools
{
    public static IReadOnlyDictionary<string, DefaultMiningTool?>? Load(VersionData version)
    {
        if (!version.RawFiles.TryGetValue("block-mining.json", out JsonElement mining) || !mining.TryGetProperty("default_components", out JsonElement entries))
            return null;
        Dictionary<string, DefaultMiningTool?> tools = [];
        foreach (JsonProperty entry in entries.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Null)
            {
                tools.Add(entry.Name, null);
                continue;
            }
            JsonElement tool = entry.Value;
            List<DefaultMiningRule> rules = [];
            foreach (JsonElement rule in tool.GetProperty("rules").EnumerateArray())
            {
                JsonElement selector = rule.GetProperty("blocks");
                string? tag = selector.ValueKind == JsonValueKind.String && selector.GetString()!.StartsWith('#') ? selector.GetString()![1..] : null;
                string[] blocks = tag is not null ? [] : selector.ValueKind == JsonValueKind.String ? [selector.GetString()!] : [.. selector.EnumerateArray().Select(block => block.GetString()!)];
                rules.Add(new DefaultMiningRule(tag, blocks, rule.TryGetProperty("speed", out JsonElement speed) ? speed.GetSingle() : null,
                    rule.TryGetProperty("correct_for_drops", out JsonElement correct) ? correct.GetBoolean() : null));
            }
            tools.Add(entry.Name, new DefaultMiningTool(tool.TryGetProperty("default_mining_speed", out JsonElement value) ? value.GetSingle() : 1,
                tool.TryGetProperty("damage_per_block", out value) ? value.GetInt32() : 1,
                !tool.TryGetProperty("can_destroy_blocks_in_creative", out value) || value.GetBoolean(), rules));
        }
        return tools;
    }
}
