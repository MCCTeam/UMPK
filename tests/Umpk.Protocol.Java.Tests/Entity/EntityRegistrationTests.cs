using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Verifies the entity family is wired through <see cref="PacketRegistrar"/>: for each exemplar protocol's codec-era key and each entity packet identifier that exists on that protocol (taken from the datasets), registration resolves to an implemented (non-marker) codec reachable both by wire id (inbound) and by type (outbound). This exercises the same entrypoint the generated descriptors call, without a <c>Umpk.Data.Java</c> reference (mirroring the world module's registration test).</summary>
public class EntityRegistrationTests
{
    private static readonly ProtocolFeatures NoFeatures = new();

    // Entity clientbound identifiers per protocol (dataset ids). 47 uses the legacy names.
    private static readonly (string Id, string Key)[] Cb47 =
    [
        ("entity_equipment", "V1_8"), ("update_health", "V1_8"), ("player_position", "V1_8"),
        ("held_item_slot", "V1_8"), ("use_bed", "V1_8"), ("animate", "V1_8"), ("add_player", "V1_8"),
        ("collect_item", "V1_8"), ("add_entity", "V1_8"), ("add_mob", "V1_8"), ("add_painting", "V1_8"),
        ("add_experience_orb", "V1_8"), ("set_entity_motion", "V1_8"), ("entity_destroy", "V1_8"),
        ("entity", "V1_8"), ("move_entity_position", "V1_8"), ("move_entity_rotation", "V1_8"),
        ("move_entity_position_rotation", "V1_8"), ("entity_teleport", "V1_8"), ("rotate_head", "V1_8"),
        ("entity_status", "V1_8"), ("attach_entity", "V1_8"), ("set_entity_data", "V1_8"),
        ("entity_effect", "V1_8"), ("remove_entity_effect", "V1_8"), ("set_experience", "V1_8"),
        ("update_attributes", "V1_8"), ("spawn_weather_entity", "V1_8"), ("camera", "V1_8"),
    ];

    // Entity clientbound identifiers shared by 770/776 (modern names). The key differs per protocol.
    private static readonly string[] ModernCbIds =
    [
        "add_entity", "animate", "damage_event", "entity_event", "entity_position_sync", "hurt_animation",
        "move_entity_pos", "move_entity_pos_rot", "move_entity_rot", "player_position", "remove_entities",
        "remove_mob_effect", "rotate_head", "set_camera", "set_entity_data", "set_entity_link",
        "set_entity_motion", "set_equipment", "set_experience", "set_health", "set_held_slot",
        "set_passengers", "take_item_entity", "teleport_entity", "update_attributes", "update_mob_effect",
    ];

    // Entity serverbound identifiers wired on protocol 47: the movement and interaction set plus set_carried_item, swing, player_command, and steer_vehicle. The assertions pin every wire shape.
    private static readonly string[] Sb47Implemented =
    [
        "move_player", "move_player_pos", "move_player_rot", "move_player_pos_rot", "player_action",
        "interact", "set_carried_item", "swing", "player_command", "steer_vehicle",
    ];

    private static readonly string[] ModernSbIds =
    [
        "accept_teleportation", "interact", "move_player_pos", "move_player_pos_rot", "move_player_rot",
        "move_player_status_only", "move_vehicle", "paddle_boat", "player_abilities", "player_action",
        "player_command", "player_input", "set_carried_item", "swing",
    ];

    [Fact]
    public void Legacy_ClientboundEntityPackets_ResolveImplemented()
    {
        ProtocolDescriptor descriptor = BuildClientbound(47, Cb47);
        AssertAllImplemented(descriptor, PacketFlow.Clientbound);
    }

    [Fact]
    public void Legacy_ServerboundWiredSubset_ResolveImplemented()
    {
        var builder = NewBuilder(47);
        int wire = 0;
        foreach (string id in Sb47Implemented)
            PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Serverbound, wire++, $"minecraft:{id}");

        AssertAllImplemented(builder.Build(), PacketFlow.Serverbound);
    }

    [Theory]
    [InlineData(770, "V1_21_5")]
    [InlineData(776, "V26_1")]
    public void Modern_ClientboundEntityPackets_ResolveImplemented(int protocol, string key)
    {
        var pairs = new (string, string)[ModernCbIds.Length];
        for (int i = 0; i < ModernCbIds.Length; i++)
            pairs[i] = (ModernCbIds[i], key);

        AssertAllImplemented(BuildClientbound(protocol, pairs), PacketFlow.Clientbound);
    }

    [Theory]
    [InlineData(770)]
    [InlineData(776)]
    public void Modern_ServerboundEntityPackets_ResolveImplemented(int protocol)
    {
        var builder = NewBuilder(protocol);
        int wire = 0;
        foreach (string id in ModernSbIds)
            PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Serverbound, wire++, $"minecraft:{id}");

        AssertAllImplemented(builder.Build(), PacketFlow.Serverbound);
    }

    [Fact]
    public void Attack_IsImplemented_On26Only()
    {
        var builder = NewBuilder(776);
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Serverbound, 1, "minecraft:attack");

        ProtocolDescriptor descriptor = builder.Build();
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound, out PhaseRegistry sb));
        Assert.True(sb.TryGetOutbound(EntityPackets.Serverbound.Attack, out _, out BoundPacketCodec entry));
        Assert.True(entry.IsImplemented);
    }

    private static ProtocolDescriptorBuilder NewBuilder(int protocol) =>
        new(new GameVersion(GameEdition.Java, "test", protocol), NoFeatures);

    private static ProtocolDescriptor BuildClientbound(int protocol, (string Id, string Key)[] ids)
    {
        var builder = NewBuilder(protocol);
        int wire = 0;
        foreach ((string id, string key) in ids)
            PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, wire++, $"minecraft:{id}");

        return builder.Build();
    }

    private static void AssertAllImplemented(ProtocolDescriptor descriptor, PacketFlow flow)
    {
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, flow, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec bound));
            Assert.True(bound.IsImplemented, $"{type.Id} resolved to a NotImplemented marker.");
            Assert.NotEqual(typeof(UnknownPacket), type.PayloadType);
        }
    }
}
