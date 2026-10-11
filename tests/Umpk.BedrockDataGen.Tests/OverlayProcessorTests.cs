using Umpk.BedrockDataGen.Schema;
using Xunit;

namespace Umpk.BedrockDataGen.Tests;

public sealed class OverlayProcessorTests
{
    [Fact]
    public void StaleOverlayPreconditionFails()
    {
        SchemaIrDocument schema = CreateSchema();
        var overlay = new SchemaOverlay(
            "large-bitset",
            ["bedrock-1.26.45-2169"],
            "wrong",
            "packet_sync",
            "data_flags",
            "wrong",
            "replace-reference",
            "verified",
            32,
            131,
            []);

        SchemaException exception = Assert.Throws<SchemaException>(
            () => OverlayProcessor.Apply(schema, [overlay], "actual"));

        Assert.Contains("protocol hash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OverbroadOverlayFails()
    {
        SchemaIrDocument schema = CreateSchema();
        var overlay = new SchemaOverlay(
            "large-bitset",
            ["bedrock-1.26.45-2169", "latest"],
            "actual",
            "packet_sync",
            "data_flags",
            SchemaHash.Compute(schema.Types["packet_sync"].Fields[0].Node),
            "replace-reference",
            "verified",
            32,
            131,
            []);

        SchemaException exception = Assert.Throws<SchemaException>(
            () => OverlayProcessor.Apply(schema, [overlay], "actual"));

        Assert.Contains("exactly one profile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedLargeBitsetOverlaySupportsBitsBeyond127()
    {
        SchemaIrDocument schema = CreateSchema();
        SchemaNode sourceNode = schema.Types["packet_sync"].Fields[0].Node;
        var overlay = new SchemaOverlay(
            "large-bitset",
            ["bedrock-1.26.45-2169"],
            "actual",
            "packet_sync",
            "data_flags",
            SchemaHash.Compute(sourceNode),
            "replace-reference",
            "verified",
            32,
            131,
            ["minecraft-data:packet_client_movement_prediction_sync.data_flags"]);

        OverlayProcessor.Apply(schema, [overlay], "actual");

        SchemaNode node = schema.Types["packet_sync"].Fields[0].Node;
        Assert.Equal(SchemaNodeKind.LargeBitSet, node.Kind);
        Assert.True(node.MaxBytes >= 19);
        Assert.Equal(131, node.FixedBitCount);
    }

    private static SchemaIrDocument CreateSchema() => new(
        "bedrock-1.26.45-2169",
        new SortedDictionary<string, SchemaNode>(StringComparer.Ordinal)
        {
            ["packet_sync"] = new()
            {
                Kind = SchemaNodeKind.Container,
                Fields = [new SchemaField("data_flags", false, new SchemaNode { Kind = SchemaNodeKind.Reference, TypeName = "varint128" })],
            },
        });
}
