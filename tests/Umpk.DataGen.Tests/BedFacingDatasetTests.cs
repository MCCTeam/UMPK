using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class BedFacingDatasetTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool legacy in new[] { false, true })
            foreach (string defect in new[] { "valid", "missing", "duplicate", "unordered", "nonhorizontal", "unknown", "schema", "identity", "count", "row-type", "row-width", "unbalanced" })
                yield return [legacy, defect];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BedFactsRequireCompleteOrderedHorizontalCoverage(bool legacy, string defect)
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
        JsonArray states = [];
        for (int index = 0; index < 16; index++)
            states.Add(new JsonArray(first + index, 2 + index % 4));
        JsonObject measurement = new()
        {
            ["_provenance"] = new JsonObject { ["kind"] = "test" },
            ["schema"] = 1,
            ["identity"] = legacy ? "legacy" : "flat",
            ["state_count"] = 16,
            ["states"] = states,
        };
        switch (defect)
        {
            case "missing": states.RemoveAt(15); break;
            case "duplicate": states[15]![0] = first; break;
            case "unordered": states[0]![0] = first + 1; states[1]![0] = first; break;
            case "nonhorizontal": states[0]![1] = 1; break;
            case "unknown": states[0]![0] = 0; break;
            case "schema": measurement["schema"] = 2; break;
            case "identity": measurement["identity"] = "unknown"; break;
            case "count": measurement["state_count"] = 15; break;
            case "row-type": states[0]![0] = "state"; break;
            case "row-width": states[0]!.AsArray().Add(0); break;
            case "unbalanced": states[0]![1] = 3; break;
        }
        File.WriteAllText(Path.Combine(directory, "bed-facings.json"), measurement.ToJsonString());
        VersionData version = DatasetLoader.Load(builder.Root).ByProtocol[protocol];
        BedFacingTable table = Assert.IsType<BedFacingTable>(BedFacingTable.Load(version));
        List<string> problems = [];
        table.Validate(version, problems);
        if (defect == "valid")
            Assert.Empty(problems);
        else
            Assert.NotEmpty(problems);
    }
}
