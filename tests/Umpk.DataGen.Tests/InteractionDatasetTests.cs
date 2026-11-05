using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class InteractionDatasetTests
{
    [Theory]
    [InlineData("schema")]
    [InlineData("identity")]
    [InlineData("states")]
    [InlineData("missing-block")]
    [InlineData("flags")]
    [InlineData("missing-item")]
    [InlineData("kind")]
    [InlineData("placed-block")]
    [InlineData("empty-placement")]
    public void InvalidInteractionMeasurementIsRejected(string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        JsonObject measurement = Measurement();
        switch (defect)
        {
            case "schema": measurement["schema"] = 2; break;
            case "identity": measurement["identity"] = "legacy"; break;
            case "states": measurement["state_count"] = 3; break;
            case "missing-block": measurement["blocks"]!.AsObject().Remove("minecraft:air"); break;
            case "flags": measurement["blocks"]!["minecraft:stone"] = 4; break;
            case "missing-item": measurement["items"]!.AsObject().Remove("minecraft:stone"); break;
            case "kind": measurement["items"]!["minecraft:stone"]![0] = "always_success"; break;
            case "placed-block": measurement["items"]!["minecraft:stone"]![1] = "minecraft:missing"; break;
            case "empty-placement": measurement["items"]!["minecraft:stone"]![1] = ""; break;
        }
        File.WriteAllText(Path.Combine(builder.Root, "770/block-interactions.json"), measurement.ToJsonString());

        IReadOnlyList<string> problems = Validator.Validate(DatasetLoader.Load(builder.Root));

        Assert.Contains(problems, problem => problem.Contains("block-interactions.json", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidInteractionMeasurementAndMissingMeasurementHaveDistinctTables()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        File.WriteAllText(Path.Combine(builder.Root, "770/block-interactions.json"), Measurement().ToJsonString());
        Dataset data = DatasetLoader.Load(builder.Root);
        Assert.Empty(Validator.Validate(data));

        IReadOnlyDictionary<string, Emitter.EmittedFile> files = new Emitter(data).Emit();

        Assert.Contains("BlockInteractions => SharedTables.BlockInteractionsV770;", files["V770/Descriptor.g.cs"].Text, StringComparison.Ordinal);
        Assert.Contains("BlockInteractions => SharedTables.BlockInteractionsV47;", files["V47/Descriptor.g.cs"].Text, StringComparison.Ordinal);
    }

    private static JsonObject Measurement() => new()
    {
        ["_provenance"] = new JsonObject { ["kind"] = "extracted" },
        ["schema"] = 1,
        ["identity"] = "flat",
        ["state_count"] = 2,
        ["blocks"] = new JsonObject { ["minecraft:air"] = 1, ["minecraft:stone"] = 3 },
        ["items"] = new JsonObject
        {
            ["minecraft:air"] = new JsonArray("pass", ""),
            ["minecraft:stone"] = new JsonArray("simple_cube", "minecraft:stone"),
        },
    };
}
