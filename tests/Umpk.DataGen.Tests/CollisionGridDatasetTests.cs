using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class CollisionGridDatasetTests
{
    [Fact]
    public void CollisionGridBlobsUseTheirOwnGeneratedSourceFile()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        File.WriteAllText(Path.Combine(builder.Root, "770/block-collision-grids.json"), Measurement().ToJsonString());
        var files = new Emitter(DatasetLoader.Load(builder.Root)).Emit();
        Assert.True(files.ContainsKey("SharedCollisionGrids.g.cs"));
        Assert.Contains("partial class SharedTables", files["SharedCollisionGrids.g.cs"].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("BlockCollisionGrids", files["SharedTables.g.cs"].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactCollisionGridIsEmittedThroughTheDatasetPipeline()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        File.WriteAllText(Path.Combine(builder.Root, "770/block-collision-grids.json"), Measurement().ToJsonString());
        Dataset data = DatasetLoader.Load(builder.Root);
        Assert.Empty(Validator.Validate(data));
        Assert.Contains("BlockCollisionGrids => SharedTables.BlockCollisionGridsV770;", new Emitter(data).Emit()["V770/Descriptor.g.cs"].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("state-count")]
    [InlineData("missing-state")]
    [InlineData("missing-group")]
    [InlineData("backwards-plane")]
    [InlineData("duplicate-cell")]
    [InlineData("cell-outside-grid")]
    [InlineData("missing-box")]
    [InlineData("canonical")]
    [InlineData("cell-order")]
    public void InvalidGridIsRejectedWithAnOwningFileDiagnostic(string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        JsonObject grid = Measurement();
        switch (defect)
        {
            case "schema": grid["schema"] = 2; break;
            case "state-count": grid["state_count"] = 3; break;
            case "missing-state": grid["states"]!.AsObject().Remove("1"); break;
            case "missing-group": grid["states"]!["1"] = 2; break;
            case "backwards-plane": grid["groups"]![1]!["planes"]![0]![1] = -1; break;
            case "duplicate-cell": grid["groups"]![1]!["cells"]!.AsArray().Add(0); break;
            case "cell-outside-grid": grid["groups"]![1]!["cells"]![0] = 1; break;
            case "missing-box": grid["groups"]![1]!["boxes"]!.AsArray().Clear(); break;
            case "canonical": grid["groups"]![1]!["planes"]![0]![1] = .5; break;
            case "cell-order": grid["cell_order"] = "reversed"; break;
        }
        File.WriteAllText(Path.Combine(builder.Root, "770/block-collision-grids.json"), grid.ToJsonString());
        Assert.Contains(Validator.Validate(DatasetLoader.Load(builder.Root)), problem => problem.Contains("block-collision-grids.json", StringComparison.Ordinal));
    }

    private static JsonObject Measurement() => JsonNode.Parse("""
        {"_provenance":{"kind":"extracted"},"schema":1,"state_count":2,"cell_order":"(x * y_size + y) * z_size + z","states":{"0":0,"1":1},"groups":[
         {"canonical_full_block":false,"planes":[[0],[0],[0]],"cells":[],"boxes":[]},
         {"canonical_full_block":true,"planes":[[0,1],[0,1],[0,1]],"cells":[0],"boxes":[[0,0,0,1,1,1]]}]}
        """)!.AsObject();
}
