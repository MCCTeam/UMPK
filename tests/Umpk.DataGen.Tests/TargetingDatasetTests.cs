using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class TargetingDatasetTests
{
    [Fact]
    public void CompleteLegacyTargetingWithNeighborContextIsAcceptedAndEmitted()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        File.WriteAllText(Path.Combine(builder.Root, "47/block-targeting.json"), LegacyMeasurement().ToJsonString());
        Dataset data = DatasetLoader.Load(builder.Root);

        Assert.Empty(Validator.Validate(data));
        Assert.Contains("BlockTargeting => SharedTables.BlockTargetingV47;", new Emitter(data).Emit()["V47/Descriptor.g.cs"].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("coverage")]
    [InlineData("target")]
    [InlineData("cycle")]
    [InlineData("root")]
    public void InvalidTargetingContextIsRejectedWithTheSpecificDiagnostic(string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        JsonObject measurement = LegacyMeasurement();
        switch (defect)
        {
            case "offset": measurement["context_nodes"]![0]!["offset"]![1] = 2; break;
            case "coverage": measurement["context_nodes"]![0]!["ranges"]![1]![1] = 15; break;
            case "target": measurement["context_nodes"]![0]!["ranges"]![1]![2] = 2; break;
            case "cycle": measurement["context_nodes"]![0]!["ranges"]![1]![2] = -1; break;
            case "root": measurement["context_roots"]![0]![2] = 1; break;
        }
        File.WriteAllText(Path.Combine(builder.Root, "47/block-targeting.json"), measurement.ToJsonString());

        Assert.Contains(Validator.Validate(DatasetLoader.Load(builder.Root)), problem => problem.Contains("context " + defect, StringComparison.Ordinal));
    }

    [Fact]
    public void MissingTargetingMeasurementEmitsAnExplicitUnknownTable()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var files = new Emitter(DatasetLoader.Load(builder.Root)).Emit();

        Assert.Contains("BlockTargeting => SharedTables.BlockTargetingV47;", files["V47/Descriptor.g.cs"].Text, StringComparison.Ordinal);
        Assert.Contains("BlockTargeting => SharedTables.BlockTargetingV47;", files["V770/Descriptor.g.cs"].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("clip-model")]
    [InlineData("count")]
    [InlineData("gap")]
    [InlineData("overlap")]
    [InlineData("length")]
    [InlineData("outline")]
    [InlineData("interaction")]
    [InlineData("held")]
    [InlineData("selector")]
    [InlineData("offset")]
    [InlineData("limit")]
    [InlineData("box")]
    public void InvalidTargetingMeasurementIsRejected(string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        JsonObject measurement = Measurement();
        switch (defect)
        {
            case "schema": measurement["schema"] = 2; break;
            case "clip-model": measurement["clip_model"] = "unknown"; break;
            case "count": measurement["state_count"] = 3; break;
            case "gap": measurement["state_refs"]![1]![0] = 2; break;
            case "overlap": measurement["state_refs"]![1]![0] = 0; break;
            case "length": measurement["state_refs"]![1]![1] = 0; break;
            case "outline": measurement["state_refs"]![1]![2] = 2; break;
            case "interaction": measurement["state_refs"]![1]![3] = 2; break;
            case "held": measurement["state_refs"]![1]![5] = 2; break;
            case "selector": measurement["state_refs"]![1]![4] = "minecraft:missing"; break;
            case "offset": measurement["shape_pool"]![1]!["offset"] = "unknown"; break;
            case "limit": measurement["shape_pool"]![1]!["max_horizontal_offset"] = -1; break;
            case "box": measurement["shape_pool"]![1]!["boxes"]![0]![3] = -1; break;
        }
        File.WriteAllText(Path.Combine(builder.Root, "770/block-targeting.json"), measurement.ToJsonString());

        Assert.Contains(Validator.Validate(DatasetLoader.Load(builder.Root)), problem => problem.Contains("block-targeting.json", StringComparison.Ordinal));
    }

    [Fact]
    public void CompleteTargetingMeasurementIsAcceptedAndEmitted()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        File.WriteAllText(Path.Combine(builder.Root, "770/block-targeting.json"), Measurement().ToJsonString());
        Dataset data = DatasetLoader.Load(builder.Root);

        Assert.Empty(Validator.Validate(data));
        var files = new Emitter(data).Emit();
        Assert.Contains("BlockTargeting => SharedTables.BlockTargetingV770;", files["V770/Descriptor.g.cs"].Text, StringComparison.Ordinal);
    }

    private static JsonObject Measurement() => JsonNode.Parse("""
        {"_provenance":{"kind":"extracted"},"schema":1,"state_count":2,
         "shape_pool":[
          {"boxes":[],"offset":"none","max_horizontal_offset":0,"max_vertical_offset":0},
          {"boxes":[[0,0,0,1,1,1]],"offset":"none","max_horizontal_offset":0,"max_vertical_offset":0}],
         "state_refs":[[0,1,0,0,"",0],[1,1,1,0,"",1]]}
        """)!.AsObject();

    private static JsonObject LegacyMeasurement()
    {
        JsonObject result = Measurement();
        result["clip_model"] = "aabb";
        result["state_count"] = 32;
        result["shape_pool"]![1]!["clip_model"] = "aabb_list";
        result["state_refs"] = JsonNode.Parse("[[0,16,0,0,\"\",0],[16,16,1,0,\"\",1]]");
        result["context_nodes"] = JsonNode.Parse("[{\"offset\":[0,1,0],\"ranges\":[[0,16,0],[16,16,1]]}]");
        result["context_roots"] = JsonNode.Parse("[[16,16,0]]");
        return result;
    }
}
