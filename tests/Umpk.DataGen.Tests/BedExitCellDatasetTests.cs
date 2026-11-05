using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class BedExitCellDatasetTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool legacy in new[] { false, true })
            foreach (string defect in new[] { "valid", "missing", "empty", "overlap", "unordered", "uncompacted", "unknown", "flags", "schema", "identity", "count", "row-type", "row-width", "rejected-overlap", "rejected-order", "rejected-unknown", "rejected-type" })
                yield return [legacy, defect];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CellsMustPartitionTheNativeRegistry(bool legacy, string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        int protocol = legacy ? 47 : 770;
        string directory = Path.Combine(builder.Root, protocol.ToString(System.Globalization.CultureInfo.InvariantCulture));
        int first = legacy ? 12 << 4 : 100;
        JsonObject block = legacy
            ? new JsonObject { ["material"] = "bed", ["block_id"] = 12, ["meta"] = 0, ["state_id"] = first }
            : new JsonObject { ["name"] = "minecraft:red_bed", ["block_id"] = 0, ["default_state"] = first, ["min_state"] = first, ["max_state"] = first + 15, ["num_states"] = 16 };
        string blocksPath = Path.Combine(directory, "blocks.json");
        JsonObject blocks = JsonNode.Parse(File.ReadAllText(blocksPath))!.AsObject();
        blocks["blocks"] = new JsonArray(block);
        File.WriteAllText(blocksPath, blocks.ToJsonString());
        JsonArray ranges = [new JsonArray(first, first + 7, 1), new JsonArray(first + 8, first + 15, 2)];
        JsonArray rejected = [];
        JsonObject measurement = new()
        {
            ["schema"] = 1,
            ["identity"] = legacy ? "legacy" : "flat",
            ["state_count"] = 16,
            ["ranges"] = ranges,
            ["rejected_states"] = rejected,
        };
        switch (defect)
        {
            case "missing": ranges[1]![1] = first + 14; measurement["state_count"] = 15; break;
            case "empty": ranges.Clear(); measurement["state_count"] = 0; break;
            case "overlap": ranges[1]![0] = first + 7; break;
            case "unordered": ranges[1]![0] = first; break;
            case "uncompacted": ranges[1]![2] = 1; break;
            case "unknown": ranges[0]![0] = first - 1; break;
            case "flags": ranges[0]![2] = 4; break;
            case "schema": measurement["schema"] = 2; break;
            case "identity": measurement["identity"] = "unknown"; break;
            case "count": measurement["state_count"] = 15; break;
            case "row-type": ranges[0]![0] = "state"; break;
            case "row-width": ranges[0]!.AsArray().Add(0); break;
            case "rejected-overlap": rejected.Add(first); break;
            case "rejected-order": rejected.Add(first + 15); rejected.Add(first + 14); break;
            case "rejected-unknown": rejected.Add(first + 16); break;
            case "rejected-type": rejected.Add("state"); break;
        }
        File.WriteAllText(Path.Combine(directory, "bed-exit-cells.json"), measurement.ToJsonString());
        VersionData version = DatasetLoader.Load(builder.Root).ByProtocol[protocol];
        BedExitCellTable table = Assert.IsType<BedExitCellTable>(BedExitCellTable.Load(version));
        List<string> problems = [];
        table.Validate(version, problems);
        if (defect == "valid")
            Assert.Empty(problems);
        else
            Assert.NotEmpty(problems);
    }

    [Fact]
    public void OnlyLegacyTablesCanExcludeNativelyRejectedMetadata()
    {
        foreach (int protocol in new[] { 47, 770 })
        {
            using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
            VersionData version = DatasetLoader.Load(builder.Root).ByProtocol[protocol];
            int[] states = version.Blocks.Identity == "legacy"
                ? [.. version.Blocks.Blocks.SelectMany(block => Enumerable.Range(block.BlockId!.Value << 4, 16))]
                : [.. version.Blocks.Blocks.SelectMany(block => Enumerable.Range(block.MinState!.Value, block.NumStates!.Value))];
            ReplacementRange[] ranges = [.. states.Skip(1).Select(state => new ReplacementRange(state, state, state % 2))];
            var table = new BedExitCellTable(1, version.Blocks.Identity, states.Length - 1, ranges, [states[0]], true);
            List<string> problems = [];
            table.Validate(version, problems);
            if (protocol == 47)
                Assert.Empty(problems);
            else
                Assert.NotEmpty(problems);
        }
    }
}
