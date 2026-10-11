using System.Text.Json;
using Umpk.BedrockDataGen.Schema;
using Xunit;

namespace Umpk.BedrockDataGen.Tests;

public sealed class SchemaNormalizerTests
{
    [Fact]
    public void NormalizesSupportedSchemaConstructs()
    {
        using JsonDocument source = JsonDocument.Parse("""
            {
              "types": {
                "native_id": "native",
                "flags": ["bitflags", { "type": "varint", "flags": ["one", "two"] }],
                "payload": ["container", [
                  { "name": "kind", "type": ["mapper", { "type": "u8", "mappings": { "1": "known" } }] },
                  { "name": "values", "type": ["array", { "countType": "varint", "type": "zigzag32" }] },
                  { "name": "optional", "type": ["option", "string"] },
                  { "anon": true, "type": ["switch", { "compareTo": "kind", "fields": { "known": "native_id" }, "default": "void" }] }
                ]]
              }
            }
            """);

        SchemaIrDocument result = SchemaNormalizer.Normalize(source.RootElement, "bedrock-1.26.45-2169");

        Assert.Equal(SchemaNodeKind.Native, result.Types["native_id"].Kind);
        Assert.Equal(SchemaNodeKind.Flags, result.Types["flags"].Kind);
        Assert.Equal(SchemaNodeKind.Container, result.Types["payload"].Kind);
        Assert.Contains(result.Types["payload"].Fields, static field => field.Node.Kind == SchemaNodeKind.Array);
        Assert.Contains(result.Types["payload"].Fields, static field => field.Node.Kind == SchemaNodeKind.Option);
        Assert.Contains(result.Types["payload"].Fields, static field => field.Node.Kind == SchemaNodeKind.Switch);
    }

    [Fact]
    public void UnsupportedConstructFailsInsteadOfBecomingAnOpaqueNode()
    {
        using JsonDocument source = JsonDocument.Parse("""
            { "types": { "packet_test": ["future_construct", {}] } }
            """);

        SchemaException exception = Assert.Throws<SchemaException>(
            () => SchemaNormalizer.Normalize(source.RootElement, "test"));

        Assert.Contains("future_construct", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedArrayFailsValidation()
    {
        using JsonDocument source = JsonDocument.Parse("""
            { "types": { "packet_test": ["array", { "type": "u8" }] } }
            """);

        SchemaException exception = Assert.Throws<SchemaException>(
            () => SchemaNormalizer.Normalize(source.RootElement, "test"));

        Assert.Contains("countType", exception.Message, StringComparison.Ordinal);
    }
}
