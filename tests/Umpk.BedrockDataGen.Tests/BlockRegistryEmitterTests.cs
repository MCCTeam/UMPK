using System.Text.Json;
using Xunit;

namespace Umpk.BedrockDataGen.Tests;

public sealed class BlockRegistryEmitterTests
{
    [Fact]
    public void Emit_PinsCountSourceHashAndAirIndex()
    {
        using var output = new TemporaryDirectory();
        string inputPath = Path.Combine(output.Path, "blockStates.json");
        File.WriteAllText(inputPath, """[{"name":"stone","states":{},"version":18168865},{"name":"air","states":{},"version":18168865},{"name":"custom:widget","states":{"active":true},"version":18168865}]""");
        string outputPath = Path.Combine(output.Path, "Bedrock2169BlockRegistry.g.cs");

        BlockRegistryEmitter.Emit(inputPath, outputPath, "abc123");

        string generated = File.ReadAllText(outputPath);
        Assert.Contains("StableCount = 3", generated, StringComparison.Ordinal);
        Assert.Contains("abc123", generated, StringComparison.Ordinal);
        Assert.Contains("AirStableIndex = 1", generated, StringComparison.Ordinal);
        Assert.Contains("unresolved-live", generated, StringComparison.Ordinal);
        Assert.Contains("inherited-frozen-1.26.30", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoIndependentEmissionsAreByteIdentical()
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();
        string firstInput = Path.Combine(first.Path, "blockStates.json");
        string secondInput = Path.Combine(second.Path, "blockStates.json");
        File.WriteAllText(firstInput, """[{"name":"air","states":{},"version":18168865}]""");
        File.WriteAllText(secondInput, """[{"name":"air","states":{},"version":18168865}]""");

        BlockRegistryEmitter.Emit(firstInput, Path.Combine(first.Path, "registry.g.cs"), "aaa");
        BlockRegistryEmitter.Emit(secondInput, Path.Combine(second.Path, "registry.g.cs"), "aaa");

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(first.Path, "registry.g.cs")),
            File.ReadAllBytes(Path.Combine(second.Path, "registry.g.cs")));
    }

    [Fact]
    public void EmptyRegistryFailsWithoutGuessingEntries()
    {
        using var output = new TemporaryDirectory();
        string inputPath = Path.Combine(output.Path, "blockStates.json");
        File.WriteAllText(inputPath, "[]");

        Schema.SchemaException exception = Assert.Throws<Schema.SchemaException>(() =>
            BlockRegistryEmitter.Emit(inputPath, Path.Combine(output.Path, "registry.g.cs"), "aaa"));

        Assert.Contains("nonempty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingAirIndexIsRecordedRatherThanGuessed()
    {
        using var output = new TemporaryDirectory();
        string inputPath = Path.Combine(output.Path, "blockStates.json");
        File.WriteAllText(inputPath, """[{"name":"stone","states":{},"version":18168865}]""");
        string outputPath = Path.Combine(output.Path, "registry.g.cs");

        BlockRegistryEmitter.Emit(inputPath, outputPath, "aaa");

        string generated = File.ReadAllText(outputPath);
        Assert.Contains("AirStableIndex = -1", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void EntryWithoutNameFailsClearly()
    {
        using var output = new TemporaryDirectory();
        string inputPath = Path.Combine(output.Path, "blockStates.json");
        File.WriteAllText(inputPath, """[{"states":{},"version":18168865}]""");

        Assert.Throws<Schema.SchemaException>(() =>
            BlockRegistryEmitter.Emit(inputPath, Path.Combine(output.Path, "registry.g.cs"), "aaa"));
    }
}
