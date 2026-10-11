using Umpk.BedrockDataGen.Schema;
using Xunit;

namespace Umpk.BedrockDataGen.Tests;

public sealed class DeterministicEmitterTests
{
    [Fact]
    public void TwoIndependentTreesAreByteIdentical()
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();
        RequiredPacket[] packets =
        [
            new("network_settings", 143, "clientbound", "required", "primary-schema", "bbb", true),
            new("login", 1, "serverbound", "required", "primary-schema", "aaa", true),
        ];
        ImportedInput[] inputs =
        [
            new("version", "bedrock/1.26.45/version.json", "version.json", "bbb", "release-identity"),
            new("protocol", "bedrock/1.26.45/protocol.json", "protocol.json", "aaa", "primary-packet-schema"),
        ];
        var profile = new GeneratedProfile("bedrock-1.26.45-2169", "1.26.45", 2169, "base-2168", "data", "behavior", 131, 19, 32, "source", "schema", inputs, []);

        Emitter.Emit(profile, packets, Path.Combine(first.Path, "protocol"), Path.Combine(first.Path, "data"));
        Emitter.Emit(profile, packets, Path.Combine(second.Path, "protocol"), Path.Combine(second.Path, "data"));

        AssertTreesEqual(first.Path, second.Path);
    }

    [Fact]
    public void DispatchPreservesPacketDirectionAndBlockedEvidence()
    {
        using var output = new TemporaryDirectory();
        RequiredPacket[] packets =
        [
            new("move_player", 19, "bidirectional", "blocked-on-D06", "candidate-unverified", "abc", false),
        ];
        var profile = new GeneratedProfile("bedrock-1.26.45-2169", "1.26.45", 2169, "base-2168", "data", "behavior", 131, 19, 32, "source", "schema", [], []);

        Emitter.Emit(profile, packets, Path.Combine(output.Path, "protocol"), Path.Combine(output.Path, "data"));

        string generated = File.ReadAllText(Path.Combine(output.Path, "protocol", "Bedrock2169PacketCatalog.g.cs"));
        Assert.Contains("19, \"move_player\", \"bidirectional\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"blocked-on-D06\", \"candidate-unverified\"", generated, StringComparison.Ordinal);
        Assert.Contains("false", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedBindingsEmitDirectAdaptersAndMetadata()
    {
        using var output = new TemporaryDirectory();
        RequiredPacket[] packets =
        [
            new("login", 1, "serverbound", "required", "primary-schema", "aaa", true, "phase-7", "verified", "bootstrap.login", "authentication", "none"),
            new("play_status", 2, "clientbound", "required", "primary-schema", "aab", true, "phase-8", "verified", "bootstrap.play-status", "offline-join", "none"),
            new("server_to_client_handshake", 3, "clientbound", "required-when-encrypted", "primary-schema", "aac", true, "phase-6", "verified", "bootstrap.server-to-client-handshake", "encryption", "none"),
            new("client_to_server_handshake", 4, "serverbound", "required-when-encrypted", "primary-schema", "aad", true, "phase-6", "verified", "bootstrap.client-to-server-handshake", "encryption", "none"),
            new("disconnect", 5, "clientbound", "required", "primary-schema", "aae", true, "phase-8", "verified", "bootstrap.disconnect", "lifecycle", "none"),
            new("level_chunk", 58, "clientbound", "required", "primary-schema", "bbb", true, "phase-10", "verified", "world.level-chunk", "terrain", "none"),
            new("request_chunk_radius", 69, "serverbound", "required", "primary-schema", "bbc", true, "phase-8", "verified", "bootstrap.request-chunk-radius", "offline-join", "none"),
            new("chunk_radius_update", 70, "clientbound", "required", "primary-schema", "bbd", true, "phase-8", "verified", "bootstrap.chunk-radius-update", "offline-join", "none"),
            new("set_local_player_as_initialized", 113, "serverbound", "required", "primary-schema", "bbe", true, "phase-8", "verified", "bootstrap.set-local-player-as-initialized", "offline-join", "none"),
            new("client_cache_status", 129, "serverbound", "required", "primary-schema", "bbf", true, "phase-8", "verified", "bootstrap.client-cache-status", "offline-join", "none"),
            new("network_settings", 143, "clientbound", "required", "primary-schema", "bbc", true, "phase-6", "verified", "bootstrap.network-settings", "compression", "none"),
            new("item_registry", 162, "clientbound", "required-context", "primary-schema", "ccc", true, "phase-6", "verified", "registry.item", "items", "item-registry"),
            new("request_network_settings", 193, "serverbound", "required", "primary-schema", "ccd", true, "phase-8", "verified", "bootstrap.request-network-settings", "bootstrap", "none"),
        ];
        var profile = new GeneratedProfile("bedrock-1.26.45-2169", "1.26.45", 2169, "base-2168", "data", "behavior", 131, 19, 32, "source", "schema", [], []);

        Emitter.Emit(profile, packets, Path.Combine(output.Path, "protocol"), Path.Combine(output.Path, "data"));

        string generated = File.ReadAllText(Path.Combine(output.Path, "protocol", "Bedrock2169CodecRegistry.g.cs"));
        Assert.Contains("BedrockBootstrapCodecs.DecodeLogin(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockBootstrapCodecs.DecodePlayStatus(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockBootstrapCodecs.DecodeServerToClientHandshake(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockBootstrapCodecs.DecodeClientToServerHandshake(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockBootstrapCodecs.DecodeDisconnect(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockWorldCodecs.DecodeLevelChunk(payload, context.Limits)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockBootstrapCodecs.DecodeRequestChunkRadius(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockBootstrapCodecs.DecodeChunkRadiusUpdate(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockBootstrapCodecs.DecodeSetLocalPlayerAsInitialized(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockBootstrapCodecs.DecodeClientCacheStatus(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockBootstrapCodecs.DecodeNetworkSettings(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockItemRegistryCodec.Decode(payload, context.Limits)", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockBootstrapCodecs.DecodeRequestNetworkSettings(payload)", generated, StringComparison.Ordinal);
        Assert.Contains("\"authentication\"", generated, StringComparison.Ordinal);
        Assert.Contains("BedrockRegistryGeneration.ItemRegistry", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockedBindingsHaveNoDecoder()
    {
        using var output = new TemporaryDirectory();
        RequiredPacket[] packets =
        [
            new("move_player", 19, "bidirectional", "blocked-on-D06", "candidate-unverified", "aaa", false, "phase-6", "blocked", null, "movement", "none", "MovePlayer actor ID widths need an exact witness."),
            new("client_movement_prediction_sync", 322, "unresolved", "blocked-on-direction-witness", "direction-unverified", "bbb", false, "phase-13", "blocked", null, "movement", "none", "Prediction sync direction needs an exact witness."),
        ];
        var profile = new GeneratedProfile("bedrock-1.26.45-2169", "1.26.45", 2169, "base-2168", "data", "behavior", 131, 19, 32, "source", "schema", [], []);

        Emitter.Emit(profile, packets, Path.Combine(output.Path, "protocol"), Path.Combine(output.Path, "data"));

        string generated = File.ReadAllText(Path.Combine(output.Path, "protocol", "Bedrock2169CodecRegistry.g.cs"));
        Assert.Contains("19, \"move_player\", \"bidirectional\"", generated, StringComparison.Ordinal);
        Assert.Contains("322, \"client_movement_prediction_sync\", \"unresolved\"", generated, StringComparison.Ordinal);
        Assert.Contains("null, \"MovePlayer actor ID widths need an exact witness.\"", generated, StringComparison.Ordinal);
        Assert.Contains("null, \"Prediction sync direction needs an exact witness.\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("DecodeMovePlayer", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("DecodeClientMovementPredictionSync", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedBindingWithoutConcreteCodecFailsGeneration()
    {
        using var output = new TemporaryDirectory();
        RequiredPacket[] packets =
        [
            new("login", 1, "serverbound", "required", "primary-schema", "aaa", true, "phase-7", "verified", "missing", "authentication", "none"),
        ];
        var profile = new GeneratedProfile("bedrock-1.26.45-2169", "1.26.45", 2169, "base-2168", "data", "behavior", 131, 19, 32, "source", "schema", [], []);

        SchemaException exception = Assert.Throws<SchemaException>(() =>
            Emitter.Emit(profile, packets, Path.Combine(output.Path, "protocol"), Path.Combine(output.Path, "data")));

        Assert.Contains("concrete codec", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnselectedBindingsStayOutOfCodecRegistry()
    {
        using var output = new TemporaryDirectory();
        RequiredPacket[] packets =
        [
            new("resource_packs_info", 6, "clientbound", "required", "primary-schema", "aaa", true, "phase-7"),
            new("resource_pack_stack", 7, "clientbound", "required", "primary-schema", "bbb", true, "phase-7"),
            new("resource_pack_client_response", 8, "serverbound", "required", "primary-schema", "ccc", true, "phase-7"),
            new("start_game", 11, "clientbound", "required", "primary-schema", "ddd", true, "phase-8"),
            new("subchunk", 174, "clientbound", "required", "primary-schema", "eee", true, "phase-10"),
        ];
        var profile = new GeneratedProfile("bedrock-1.26.45-2169", "1.26.45", 2169, "base-2168", "data", "behavior", 131, 19, 32, "source", "schema", [], []);

        Emitter.Emit(profile, packets, Path.Combine(output.Path, "protocol"), Path.Combine(output.Path, "data"));

        string catalog = File.ReadAllText(Path.Combine(output.Path, "protocol", "Bedrock2169PacketCatalog.g.cs"));
        string registry = File.ReadAllText(Path.Combine(output.Path, "protocol", "Bedrock2169CodecRegistry.g.cs"));
        foreach (RequiredPacket packet in packets)
        {
            Assert.Contains($"new({packet.Id}, \"{packet.Name}\"", catalog, StringComparison.Ordinal);
            Assert.DoesNotContain($"new({packet.Id}, \"{packet.Name}\"", registry, StringComparison.Ordinal);
        }
    }

    private static void AssertTreesEqual(string first, string second)
    {
        string[] firstFiles = Directory.GetFiles(first, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(first, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] secondFiles = Directory.GetFiles(second, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(second, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(firstFiles, secondFiles);

        foreach (string relativePath in firstFiles)
            Assert.Equal(File.ReadAllBytes(Path.Combine(first, relativePath)), File.ReadAllBytes(Path.Combine(second, relativePath)));
    }
}
