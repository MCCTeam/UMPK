using System.Text.Json;

namespace Umpk.DataGen;

internal readonly record struct InteractionItem(string Kind, string Block);

/// <summary>Measured inheritance contracts. Unknown item behavior is retained explicitly and never promoted to success.</summary>
internal sealed record InteractionTable(int Schema, string Identity, int StateCount, IReadOnlyDictionary<string, int> Blocks, IReadOnlyDictionary<string, InteractionItem> Items)
{
    public static InteractionTable? Load(VersionData version)
    {
        if (!version.RawFiles.TryGetValue("block-interactions.json", out JsonElement root))
            return null;
        Dictionary<string, int> blocks = [];
        foreach (JsonProperty block in root.GetProperty("blocks").EnumerateObject())
            blocks.Add(block.Name, block.Value.GetInt32());
        Dictionary<string, InteractionItem> items = [];
        foreach (JsonProperty item in root.GetProperty("items").EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.Array || item.Value.GetArrayLength() != 2)
                throw new DatasetException("block-interactions.json item rows must contain a kind and placed block name");
            items.Add(item.Name, new InteractionItem(item.Value[0].GetString()!, item.Value[1].GetString()!));
        }
        return new InteractionTable(root.GetProperty("schema").GetInt32(), root.GetProperty("identity").GetString()!, root.GetProperty("state_count").GetInt32(), blocks, items);
    }

    public void Validate(VersionData version, List<string> problems)
    {
        void Reject(string detail) => problems.Add($"proto {version.Protocol}: block-interactions.json {detail}");
        if (Schema != 1 || Identity != version.Blocks.Identity)
            Reject("schema or identity differs from the supported block table");
        if (StateCount != version.Blocks.Blocks.Sum(block => block.NumStates ?? 1))
            Reject("state count differs from the block table");
        HashSet<string> blocks = [.. version.Blocks.Blocks.Select(block => block.Name ?? string.Empty)];
        if (!blocks.SetEquals(Blocks.Keys))
            Reject("must measure every block name exactly once");
        if (Blocks.Values.Any(flags => flags is < 0 or > 3))
            Reject("has unsupported block flags");
        if (!Items.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(version.Items.Select(item => item.Name)))
            Reject("must measure every item name exactly once");
        foreach ((string name, InteractionItem item) in Items)
        {
            if (item.Kind is not ("unknown" or "pass" or "simple_cube"))
                Reject($"has an unsupported use contract for '{name}'");
            if (item.Block.Length != 0 && !blocks.Contains(item.Block))
                Reject($"names an unknown placed block for '{name}'");
            if ((item.Kind == "simple_cube" && item.Block.Length == 0) || (item.Kind == "pass" && item.Block.Length != 0))
                Reject($"has an inconsistent placed block for '{name}'");
        }
    }
}
