using Xunit;

namespace Umpk.BedrockDataGen.Tests;

public sealed class ImporterTests
{
    [Fact]
    public void HashMismatchStopsImportBeforeCopyingInput()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        string sourceFile = Path.Combine(source.Path, "source.json");
        File.WriteAllText(sourceFile, "{}");
        var specification = new ImportSpecification(
            "test",
            "revision",
            "MIT",
            "hash",
            [new ImportSource("protocol", "source.json", "protocol.json", new string('0', 64), "primary-packet-schema")]);

        Schema.SchemaException exception = Assert.Throws<Schema.SchemaException>(
            () => Importer.Import(source.Path, destination.Path, specification));

        Assert.Contains("hash", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(destination.Path, "imported", "protocol.json")));
    }
}
