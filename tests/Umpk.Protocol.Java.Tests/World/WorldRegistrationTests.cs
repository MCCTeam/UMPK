using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>Verifies the world family is wired through <see cref="PacketRegistrar"/>: for each exemplar protocol's codec-era key and each world packet identifier that exists on that protocol, registration resolves to an implemented (non-marker) codec that is reachable both by wire id (inbound) and by type (outbound). This exercises the same entrypoint the generated descriptors call, without a data-package reference.</summary>
public class WorldRegistrationTests
{
    private static readonly ProtocolFeatures NoFeatures = new();

    // (identifier, flow) pairs for the world family on 770 / 776 (they share identifiers).
    private static readonly (string Id, PacketFlow Flow)[] ModernIds =
    [
        ("set_time", PacketFlow.Clientbound),
        ("set_default_spawn_position", PacketFlow.Clientbound),
        ("respawn", PacketFlow.Clientbound),
        ("change_difficulty", PacketFlow.Clientbound),
        ("block_update", PacketFlow.Clientbound),
        ("section_blocks_update", PacketFlow.Clientbound),
        ("block_entity_data", PacketFlow.Clientbound),
        ("block_event", PacketFlow.Clientbound),
        ("block_destruction", PacketFlow.Clientbound),
        ("block_changed_ack", PacketFlow.Clientbound),
        ("forget_level_chunk", PacketFlow.Clientbound),
        ("chunk_batch_start", PacketFlow.Clientbound),
        ("chunk_batch_finished", PacketFlow.Clientbound),
        ("chunks_biomes", PacketFlow.Clientbound),
        ("set_chunk_cache_center", PacketFlow.Clientbound),
        ("set_chunk_cache_radius", PacketFlow.Clientbound),
        ("set_simulation_distance", PacketFlow.Clientbound),
        ("light_update", PacketFlow.Clientbound),
        ("initialize_border", PacketFlow.Clientbound),
        ("set_border_center", PacketFlow.Clientbound),
        ("set_border_size", PacketFlow.Clientbound),
        ("set_border_lerp_size", PacketFlow.Clientbound),
        ("set_border_warning_delay", PacketFlow.Clientbound),
        ("set_border_warning_distance", PacketFlow.Clientbound),
        ("game_event", PacketFlow.Clientbound),
        ("level_event", PacketFlow.Clientbound),
        ("level_particles", PacketFlow.Clientbound),
        ("sound", PacketFlow.Clientbound),
        ("sound_entity", PacketFlow.Clientbound),
        ("stop_sound", PacketFlow.Clientbound),
        ("explode", PacketFlow.Clientbound),
        ("map_item_data", PacketFlow.Clientbound),
        ("chunk_batch_received", PacketFlow.Serverbound),
    ];

    // 1.8 identifiers (some differ from modern) for the family subset that exists on 1.8.
    private static readonly string[] LegacyClientboundIds =
    [
        "update_time", "spawn_position", "respawn", "server_difficulty",
        "block_change", "block_change_multi", "block_entity_data", "block_event",
        "block_break_animation", "game_state_change", "level_event", "level_particles",
        "sound_effect", "explode", "map", "world_border",
    ];

    [Theory]
    [InlineData("V1_21_5")]
    [InlineData("V26_1")]
    [InlineData("V26_2")]
    public void Modern_AllWorldPackets_ResolveImplemented(string key)
    {
        var builder = new ProtocolDescriptorBuilder(new GameVersion(GameEdition.Java, "test", CodecKeyProtocols.Of(key)), NoFeatures);
        int wire = 0;
        foreach ((string id, PacketFlow flow) in ModernIds)
            PacketRegistrar.Register(builder, ProtocolPhase.Play, flow, wire++, $"minecraft:{id}");

        ProtocolDescriptor descriptor = builder.Build();
        AssertImplemented(descriptor, PacketFlow.Clientbound);
        AssertImplemented(descriptor, PacketFlow.Serverbound);
    }

    [Fact]
    public void Legacy_AllWorldPackets_ResolveImplemented()
    {
        var builder = new ProtocolDescriptorBuilder(new GameVersion(GameEdition.Java, "test", 47), NoFeatures);
        int wire = 0;
        foreach (string id in LegacyClientboundIds)
            PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, wire++, $"minecraft:{id}");

        ProtocolDescriptor descriptor = builder.Build();
        AssertImplemented(descriptor, PacketFlow.Clientbound);
    }

    private static void AssertImplemented(ProtocolDescriptor descriptor, PacketFlow flow)
    {
        Assert.True(
            descriptor.TryGetRegistry(ProtocolPhase.Play, flow, out PhaseRegistry registry),
            $"The play/{flow} registry was not created.");

        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec bound));
            Assert.True(bound.IsImplemented, $"{type.Id} resolved to a NotImplemented marker.");
            Assert.NotEqual(typeof(UnknownPacket), type.PayloadType);
        }
    }
}
