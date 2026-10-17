using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class EntityGeometryDatasetTests
{
    [Theory]
    [InlineData("schema")]
    [InlineData("entity")]
    [InlineData("selector-index")]
    [InlineData("selector-kind")]
    [InlineData("selector-default")]
    [InlineData("selector-true-default")]
    [InlineData("none-selector-index")]
    [InlineData("none-selector-mask")]
    [InlineData("none-selector-default")]
    [InlineData("none-selector-true-variant")]
    [InlineData("pose-index")]
    [InlineData("pose-index-mismatch")]
    [InlineData("refresh")]
    [InlineData("scale")]
    [InlineData("width")]
    [InlineData("pose")]
    [InlineData("duplicate")]
    [InlineData("missing-case")]
    public void InvalidEntityGeometryIsRejected(string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        AddEntityRegistry(builder);
        JsonObject measurement = Measurement();
        JsonObject entities = measurement["entities"]!.AsObject();
        JsonObject entity = entities["minecraft:player"]!.AsObject();
        JsonArray selector = entity["selector"]!.AsArray();
        JsonArray variants = entity["variants"]!.AsArray();
        switch (defect)
        {
            case "schema": measurement["schema"] = 2; break;
            case "entity": entities["minecraft:missing"] = entity.DeepClone(); break;
            case "selector-index": selector[0] = -1; break;
            case "selector-kind": selector[1] = "float"; break;
            case "selector-default": selector[3] = 2; break;
            case "selector-true-default": selector[3] = 1; break;
            case "none-selector-index": entity["selector"] = new JsonArray(16, "none", 0, 0); variants.RemoveAt(1); break;
            case "none-selector-mask": entity["selector"] = new JsonArray(-1, "none", 1, 0); variants.RemoveAt(1); break;
            case "none-selector-default": entity["selector"] = new JsonArray(-1, "none", 0, 1); variants.RemoveAt(1); break;
            case "none-selector-true-variant": entity["selector"] = new JsonArray(-1, "none", 0, 0); break;
            case "pose-index": entity["pose_index"] = 255; break;
            case "pose-index-mismatch": entity["pose_index"] = 5; break;
            case "refresh": entity["refresh"] = "guess"; break;
            case "scale": entity["scale_attribute"] = "minecraft:missing"; break;
            case "width": variants[0]![2] = -1; break;
            case "pose": variants[0]![1] = 18; break;
            case "duplicate": variants.Add(variants[0]!.DeepClone()); break;
            case "missing-case": variants.RemoveAt(1); break;
        }
        File.WriteAllText(Path.Combine(builder.Root, "770/entity-geometry.json"), measurement.ToJsonString());

        IReadOnlyList<string> problems = Validator.Validate(DatasetLoader.Load(builder.Root));

        Assert.Contains(problems, problem => problem.Contains("entity-geometry.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("recenter_on_refresh")]
    [InlineData("retain_minimum_on_growth")]
    [InlineData("retain_minimum")]
    public void ValidEntityGeometryIsAccepted(string refresh)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        AddEntityRegistry(builder);
        JsonObject measurement = Measurement();
        measurement["entities"]!["minecraft:player"]!["refresh"] = refresh;
        File.WriteAllText(Path.Combine(builder.Root, "770/entity-geometry.json"), measurement.ToJsonString());
        Assert.Empty(Validator.Validate(DatasetLoader.Load(builder.Root)));
    }

    [Fact]
    public void PoseOnlyGeometryDoesNotNeedADummyMetadataSelector()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        AddEntityRegistry(builder);
        JsonObject measurement = Measurement();
        JsonObject entry = measurement["entities"]!["minecraft:player"]!.AsObject();
        entry["selector"] = new JsonArray(-1, "none", 0, 0);
        entry["variants"]!.AsArray().RemoveAt(1);
        File.WriteAllText(Path.Combine(builder.Root, "770/entity-geometry.json"), measurement.ToJsonString());

        Assert.Empty(Validator.Validate(DatasetLoader.Load(builder.Root)));
    }

    private static void AddEntityRegistry(DatasetBuilder builder) => File.WriteAllText(Path.Combine(builder.Root, "770/entities.json"), """
        {"_provenance":{"kind":"extracted"},"entries":[{"id":"minecraft:player","id_num":0}]}
        """);

    internal static JsonObject Measurement() => new()
    {
        ["_provenance"] = new JsonObject { ["kind"] = "extracted" },
        ["schema"] = 1,
        ["entities"] = new JsonObject
        {
            ["minecraft:player"] = new JsonObject
            {
                ["selector"] = new JsonArray(16, "boolean", 1, 0),
                ["pose_index"] = 6,
                ["refresh"] = "recenter_on_refresh",
                ["scale_attribute"] = "",
                ["variants"] = new JsonArray(new JsonArray(0, 0, .6, 1.8, false, true), new JsonArray(1, 0, .3, .9, false, true)),
            },
        },
    };
}
