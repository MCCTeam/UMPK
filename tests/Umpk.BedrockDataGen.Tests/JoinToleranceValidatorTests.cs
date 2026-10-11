using Umpk.BedrockDataGen.Schema;
using Xunit;

namespace Umpk.BedrockDataGen.Tests;

public sealed class JoinToleranceValidatorTests
{
    [Fact]
    public void AcceptsSortedManifestMatchingProtocolMap()
    {
        var manifest = new JoinToleranceManifest(
            "bedrock-join-tolerance/1",
            "bedrock-1.26.45-2169",
            "observed-default-world-offline-flow",
            [new JoinToleratedPacket(13, "add_entity"), new JoinToleratedPacket(45, "respawn")]);

        JoinToleratedPacket[] validated = JoinToleranceValidator.Validate(
            manifest,
            new Dictionary<int, string> { [13] = "add_entity", [45] = "respawn" },
            [Unselected(13, "add_entity"), Unselected(45, "respawn")]);

        Assert.Equal([new JoinToleratedPacket(13, "add_entity"), new JoinToleratedPacket(45, "respawn")], validated);
    }

    [Fact]
    public void RejectsUnsortedManifest()
    {
        var manifest = new JoinToleranceManifest(
            "bedrock-join-tolerance/1",
            "bedrock-1.26.45-2169",
            "observed-default-world-offline-flow",
            [new JoinToleratedPacket(45, "respawn"), new JoinToleratedPacket(13, "add_entity")]);

        SchemaException exception = Assert.Throws<SchemaException>(() => JoinToleranceValidator.Validate(
            manifest,
            new Dictionary<int, string> { [13] = "add_entity", [45] = "respawn" },
            [Unselected(13, "add_entity"), Unselected(45, "respawn")]));

        Assert.Contains("ascending", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNameMismatchAgainstProtocolMap()
    {
        var manifest = new JoinToleranceManifest(
            "bedrock-join-tolerance/1",
            "bedrock-1.26.45-2169",
            "observed-default-world-offline-flow",
            [new JoinToleratedPacket(13, "remove_entity")]);

        SchemaException exception = Assert.Throws<SchemaException>(() => JoinToleranceValidator.Validate(
            manifest,
            new Dictionary<int, string> { [13] = "add_entity" },
            [Unselected(13, "add_entity")]));

        Assert.Contains("13", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsCollisionWithBoundCodec()
    {
        var manifest = new JoinToleranceManifest(
            "bedrock-join-tolerance/1",
            "bedrock-1.26.45-2169",
            "observed-default-world-offline-flow",
            [new JoinToleratedPacket(11, "start_game")]);

        SchemaException exception = Assert.Throws<SchemaException>(() => JoinToleranceValidator.Validate(
            manifest,
            new Dictionary<int, string> { [11] = "start_game" },
            [new RequiredPacket("start_game", 11, "clientbound", "required", "primary-schema", "aaa", true, "phase-8", "verified", "join.start-game", "offline-join", "none")]));

        Assert.Contains("11", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsWrongProfile()
    {
        var manifest = new JoinToleranceManifest(
            "bedrock-join-tolerance/1",
            "bedrock-1.26.45-2168",
            "observed-default-world-offline-flow",
            [new JoinToleratedPacket(13, "add_entity")]);

        Assert.Throws<SchemaException>(() => JoinToleranceValidator.Validate(
            manifest,
            new Dictionary<int, string> { [13] = "add_entity" },
            [Unselected(13, "add_entity")]));
    }

    private static RequiredPacket Unselected(int id, string name) =>
        new(name, id, "clientbound", "required", "primary-schema", "aaa", true, "phase-9");
}
