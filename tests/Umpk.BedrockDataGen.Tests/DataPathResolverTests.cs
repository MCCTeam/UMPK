using System.Text.Json;
using Xunit;

namespace Umpk.BedrockDataGen.Tests;

public sealed class DataPathResolverTests
{
    [Fact]
    public void ResolvesEach2169DataCategoryIndependently()
    {
        using JsonDocument dataPaths = JsonDocument.Parse("""
            {
              "bedrock": {
                "1.26.45": {
                  "protocol": "bedrock/1.26.45",
                  "items": "bedrock/1.26.30",
                  "blocks": "bedrock/1.26.30",
                  "blockStates": "bedrock/1.26.30",
                  "blockCollisionShapes": "bedrock/1.26.30",
                  "entities": "bedrock/1.21.80",
                  "biomes": "bedrock/1.21.60",
                  "recipes": "bedrock/1.19.10",
                  "windows": "bedrock/1.16.201",
                  "steve": "bedrock/1.21.70"
                }
              }
            }
            """);

        SortedDictionary<string, string> resolved = DataPathResolver.Resolve(
            dataPaths.RootElement,
            "1.26.45",
            ["protocol", "items", "blocks", "blockStates", "blockCollisionShapes", "entities", "biomes", "recipes", "windows", "steve"]);

        Assert.Equal("bedrock/1.26.45", resolved["protocol"]);
        Assert.Equal("bedrock/1.26.30", resolved["items"]);
        Assert.Equal("bedrock/1.21.80", resolved["entities"]);
        Assert.Equal("bedrock/1.21.60", resolved["biomes"]);
        Assert.Equal("bedrock/1.19.10", resolved["recipes"]);
        Assert.Equal("bedrock/1.16.201", resolved["windows"]);
        Assert.Equal("bedrock/1.21.70", resolved["steve"]);
    }

    [Fact]
    public void MissingCategoryFailsWithoutClosestVersionFallback()
    {
        using JsonDocument dataPaths = JsonDocument.Parse("""
            { "bedrock": { "1.26.45": { "protocol": "bedrock/1.26.45" } } }
            """);

        Schema.SchemaException exception = Assert.Throws<Schema.SchemaException>(
            () => DataPathResolver.Resolve(dataPaths.RootElement, "1.26.45", ["items"]));

        Assert.Contains("items", exception.Message, StringComparison.Ordinal);
    }
}
