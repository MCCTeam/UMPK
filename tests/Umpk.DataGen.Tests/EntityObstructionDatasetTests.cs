using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class EntityObstructionDatasetTests
{
    [Theory]
    [InlineData("shape")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("unknown")]
    [InlineData("nonblocking")]
    public void InvalidFixedEntityBoxesAreRejected(string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        File.WriteAllText(Path.Combine(builder.Root, "770/entities.json"), """{"_provenance":{"kind":"extracted"},"flat_id_space":true,"entries":[{"id":"minecraft:item","id_num":0},{"id":"minecraft:zombie","id_num":1}]}""");
        JsonObject boxes = new() { ["minecraft:zombie"] = new JsonArray(2.0, 2.0) };
        switch (defect)
        {
            case "shape": boxes["minecraft:zombie"] = new JsonArray(2.0); break;
            case "zero": boxes["minecraft:zombie"] = new JsonArray(0.0, 2.0); break;
            case "negative": boxes["minecraft:zombie"] = new JsonArray(2.0, -1.0); break;
            case "unknown": boxes["minecraft:missing"] = new JsonArray(2.0, 2.0); break;
            case "nonblocking": boxes["minecraft:item"] = new JsonArray(2.0, 2.0); break;
        }
        JsonObject rules = new() { ["_provenance"] = new JsonObject { ["kind"] = "extracted" }, ["schema"] = 1, ["entities"] = new JsonObject { ["minecraft:item"] = 1, ["minecraft:zombie"] = 0 }, ["fixed_boxes"] = boxes };
        File.WriteAllText(Path.Combine(builder.Root, "770/entity-obstruction.json"), rules.ToJsonString());

        IReadOnlyList<string> problems = Validator.Validate(DatasetLoader.Load(builder.Root));

        Assert.Contains(problems, problem => problem.Contains("entity-obstruction.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("flags")]
    public void InvalidEntityObstructionRulesAreRejected(string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        File.WriteAllText(Path.Combine(builder.Root, "770/entities.json"), """{"_provenance":{"kind":"extracted"},"flat_id_space":true,"entries":[{"id":"minecraft:item","id_num":0},{"id":"minecraft:zombie","id_num":1}]}""");
        JsonObject rules = new() { ["_provenance"] = new JsonObject { ["kind"] = "extracted" }, ["schema"] = 1, ["entities"] = new JsonObject { ["minecraft:item"] = 1, ["minecraft:zombie"] = 0 } };
        switch (defect)
        {
            case "schema": rules["schema"] = 2; break;
            case "missing": rules["entities"]!.AsObject().Remove("minecraft:item"); break;
            case "extra": rules["entities"]!["minecraft:unknown"] = 1; break;
            case "flags": rules["entities"]!["minecraft:zombie"] = 2; break;
        }
        File.WriteAllText(Path.Combine(builder.Root, "770/entity-obstruction.json"), rules.ToJsonString());

        IReadOnlyList<string> problems = Validator.Validate(DatasetLoader.Load(builder.Root));

        Assert.Contains(problems, problem => problem.Contains("entity-obstruction.json", StringComparison.Ordinal));
    }
}
