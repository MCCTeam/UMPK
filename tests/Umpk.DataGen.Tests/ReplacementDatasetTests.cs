using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class ReplacementDatasetTests
{
    [Fact]
    public void MissingBlockRegistryIsReportedAlongsideReplacementErrors()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/blocks.json", root => root["blocks"]!.AsArray().Clear());
        File.WriteAllText(Path.Combine(builder.Root, "770/block-replacement.json"), Measurement(false).ToJsonString());

        IReadOnlyList<string> problems = Validator.Validate(DatasetLoader.Load(builder.Root));

        Assert.Contains(problems, problem => problem.Contains("block-replacement.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("identity")]
    [InlineData("count")]
    [InlineData("missing-state")]
    [InlineData("overlap")]
    [InlineData("negative")]
    [InlineData("reversed")]
    [InlineData("unknown-state")]
    [InlineData("flags")]
    [InlineData("malformed-range")]
    [InlineData("uncompacted")]
    [InlineData("unknown-target")]
    [InlineData("unknown-below")]
    [InlineData("redundant-override")]
    [InlineData("modern-neighbor")]
    public void InvalidNativeReplacementRulesAreRejected(string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        bool legacy = defect is "unknown-target" or "unknown-below" or "redundant-override";
        JsonObject measurement = Measurement(legacy);
        JsonArray ranges = measurement["ranges"]!.AsArray();
        switch (defect)
        {
            case "schema": measurement["schema"] = 2; break;
            case "identity": measurement["identity"] = "unsupported"; break;
            case "count": measurement["state_count"] = 3; break;
            case "missing-state": ranges.RemoveAt(1); measurement["state_count"] = 1; break;
            case "overlap": ranges[1]![0] = 0; break;
            case "negative": ranges[0]![0] = -1; break;
            case "reversed": ranges[1]![0] = 2; break;
            case "unknown-state": ranges[1]![1] = 2; measurement["state_count"] = 3; break;
            case "flags": ranges[1]![2] = 4; break;
            case "malformed-range": ranges[1]!.AsArray().RemoveAt(2); break;
            case "uncompacted": ranges[1]![2] = 3; break;
            case "unknown-target": measurement["below_overrides"]!["32"] = new JsonArray(new JsonArray(0, 0, 3)); break;
            case "unknown-below": measurement["below_overrides"]!["16"] = new JsonArray(new JsonArray(32, 32, 3)); break;
            case "redundant-override": measurement["below_overrides"]!["16"] = new JsonArray(new JsonArray(0, 0, 0)); break;
            case "modern-neighbor": measurement["below_overrides"]!["1"] = new JsonArray(new JsonArray(0, 0, 3)); break;
        }
        File.WriteAllText(Path.Combine(builder.Root, legacy ? "47/block-replacement.json" : "770/block-replacement.json"), measurement.ToJsonString());

        IReadOnlyList<string> problems = Validator.Validate(DatasetLoader.Load(builder.Root));

        Assert.Contains(problems, problem => problem.Contains("block-replacement.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteNativeReplacementRulesAreAccepted(bool legacy)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        File.WriteAllText(Path.Combine(builder.Root, legacy ? "47/block-replacement.json" : "770/block-replacement.json"), Measurement(legacy).ToJsonString());
        Dataset dataset = DatasetLoader.Load(builder.Root);
        Assert.Empty(Validator.Validate(dataset));
        string tables = new Emitter(dataset).Emit()["SharedTables.g.cs"].Text;
        Assert.Contains($"BlockInteractionsV{(legacy ? 47 : 770)} => new byte[] {{ 0x04,", tables, StringComparison.Ordinal);
    }

    private static JsonObject Measurement(bool legacy) => new()
    {
        ["_provenance"] = new JsonObject { ["kind"] = "extracted" },
        ["schema"] = 1,
        ["identity"] = legacy ? "legacy" : "flat",
        ["state_count"] = 2,
        ["ranges"] = new JsonArray(new JsonArray(0, 0, 3), new JsonArray(legacy ? 16 : 1, legacy ? 16 : 1, 0)),
        ["below_overrides"] = legacy ? new JsonObject { ["16"] = new JsonArray(new JsonArray(0, 0, 3)) } : new JsonObject(),
    };
}
