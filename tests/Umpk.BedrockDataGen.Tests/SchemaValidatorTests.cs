using System.Text.Json;
using Umpk.BedrockDataGen.Schema;
using Xunit;

namespace Umpk.BedrockDataGen.Tests;

public sealed class SchemaValidatorTests
{
    [Fact]
    public void EmptyRequiredPacketManifestFails()
    {
        var schema = new SchemaIrDocument("test", []);

        SchemaException exception = Assert.Throws<SchemaException>(
            () => SchemaValidator.Validate(schema, []));

        Assert.Contains("nonempty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownTypeInsideRequiredClosureFails()
    {
        var schema = new SchemaIrDocument(
            "test",
            new SortedDictionary<string, SchemaNode>(StringComparer.Ordinal)
            {
                ["packet_test"] = new()
                {
                    Kind = SchemaNodeKind.Container,
                    Fields = [new SchemaField("value", false, new SchemaNode { Kind = SchemaNodeKind.Reference, TypeName = "missing" })],
                },
            });
        RequiredPacket[] required = [new("test", 1, "serverbound", "required", "verified", "hash", true)];

        SchemaException exception = Assert.Throws<SchemaException>(
            () => SchemaValidator.Validate(schema, required));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatePacketIdsFailDuringManifestCreation()
    {
        using JsonDocument protocol = JsonDocument.Parse("""
            {
              "types": {
                "mcpe_packet": ["container", [{ "name": "name", "type": ["mapper", { "type": "varint", "mappings": { "1": "one", "01": "two" } }] }]],
                "packet_one": ["container", []],
                "packet_two": ["container", []]
              }
            }
            """);

        SchemaException exception = Assert.Throws<SchemaException>(
            () => RequiredPacketBuilder.ReadPacketIds(protocol.RootElement));

        Assert.Contains("duplicate packet ID 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownNativeInsideRequiredClosureFails()
    {
        var schema = new SchemaIrDocument(
            "test",
            new SortedDictionary<string, SchemaNode>(StringComparer.Ordinal)
            {
                ["packet_test"] = new() { Kind = SchemaNodeKind.Reference, TypeName = "mystery" },
                ["mystery"] = new() { Kind = SchemaNodeKind.Native, TypeName = "mystery" },
            });
        RequiredPacket[] required = [new("test", 1, "serverbound", "required", "verified", "hash", true)];

        SchemaException exception = Assert.Throws<SchemaException>(
            () => SchemaValidator.Validate(schema, required));

        Assert.Contains("unknown native", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnresolvedDirectionCannotBeMarkedVerified()
    {
        var schema = new SchemaIrDocument(
            "test",
            new SortedDictionary<string, SchemaNode>(StringComparer.Ordinal)
            {
                ["packet_test"] = new() { Kind = SchemaNodeKind.Container },
            });
        RequiredPacket[] required = [new("test", 1, "unresolved", "blocked", "direction-unverified", "hash", true)];

        SchemaException exception = Assert.Throws<SchemaException>(
            () => SchemaValidator.Validate(schema, required));

        Assert.Contains("cannot bind", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockedBindingCannotSelectCodec()
    {
        var schema = new SchemaIrDocument(
            "test",
            new SortedDictionary<string, SchemaNode>(StringComparer.Ordinal)
            {
                ["packet_test"] = new() { Kind = SchemaNodeKind.Container },
            });
        RequiredPacket[] required = [new("test", 1, "serverbound", "blocked", "primary-schema", "hash", true, "phase-6", "blocked", "bootstrap.login", "bootstrap", "none", "blocked")];

        SchemaException exception = Assert.Throws<SchemaException>(
            () => SchemaValidator.Validate(schema, required));

        Assert.Contains("blocked binding", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
