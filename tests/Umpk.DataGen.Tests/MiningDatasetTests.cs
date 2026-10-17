using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class MiningDatasetTests
{
    [Fact]
    public void MissingMiningMeasurementEmitsAnExplicitTable()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = new Emitter(DatasetLoader.Load(builder.Root)).Emit();

        Assert.Contains("BlockMining => SharedTables.BlockMiningV47;", files["V47/Descriptor.g.cs"].Text, StringComparison.Ordinal);
        Assert.Contains("BlockMining => SharedTables.BlockMiningV47;", files["V770/Descriptor.g.cs"].Text, StringComparison.Ordinal);
        Assert.Contains("private static ReadOnlySpan<byte> BlockMining(int protocol)", files["JavaGameData.Dispatch.g.cs"].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("missing-block")]
    [InlineData("unknown-block")]
    [InlineData("unknown-item")]
    [InlineData("unknown-tool-block")]
    [InlineData("hardness")]
    [InlineData("speed")]
    [InlineData("state-count")]
    [InlineData("item-count")]
    [InlineData("default")]
    public void InvalidMiningMeasurementIsRejected(string defect)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        JsonObject mining = Measurement();
        switch (defect)
        {
            case "schema": mining["schema"] = 2; break;
            case "missing-block": mining["blocks"]!.AsObject().Remove("minecraft:air"); break;
            case "unknown-block": mining["blocks"]!["minecraft:missing"] = new JsonArray(1.5, true); break;
            case "unknown-item": mining["tools"]!["minecraft:missing"] = new JsonObject(); break;
            case "unknown-tool-block": mining["tools"]!["minecraft:stone"]!["minecraft:missing"] = new JsonArray(2.0, true); break;
            case "hardness": mining["blocks"]!["minecraft:stone"]![0] = -2.0; break;
            case "speed": mining["tools"]!["minecraft:stone"]!["minecraft:stone"]![0] = -1.0; break;
            case "state-count": mining["state_count"] = 3; break;
            case "item-count": mining["item_count"] = 3; break;
            case "default": mining["default_tool"] = new JsonArray(2.0, false); break;
        }
        File.WriteAllText(Path.Combine(builder.Root, "770/block-mining.json"), mining.ToJsonString());

        IReadOnlyList<string> problems = Validator.Validate(DatasetLoader.Load(builder.Root));

        Assert.Contains(problems, problem => problem.Contains("block-mining.json", StringComparison.Ordinal));
    }

    [Fact]
    public void CompleteMiningMeasurementIsAcceptedAndEmitted()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        File.WriteAllText(Path.Combine(builder.Root, "770/block-mining.json"), Measurement().ToJsonString());
        Dataset data = DatasetLoader.Load(builder.Root);

        Assert.Empty(Validator.Validate(data));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = new Emitter(data).Emit();
        Assert.Contains("BlockMining => SharedTables.BlockMiningV770;", files["V770/Descriptor.g.cs"].Text, StringComparison.Ordinal);
    }

    private static JsonObject Measurement() => new()
    {
        ["_provenance"] = new JsonObject { ["kind"] = "extracted" },
        ["schema"] = 1,
        ["identity"] = "flat",
        ["state_count"] = 2,
        ["item_count"] = 2,
        ["default_tool"] = new JsonArray(1.0, false),
        ["blocks"] = new JsonObject
        {
            ["minecraft:air"] = new JsonArray(0.0, false),
            ["minecraft:stone"] = new JsonArray(1.5, true),
        },
        ["tools"] = new JsonObject
        {
            ["minecraft:stone"] = new JsonObject { ["minecraft:stone"] = new JsonArray(2.0, true) },
        },
    };
}
