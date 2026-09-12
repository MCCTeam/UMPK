using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>
/// The 1.8 block_event wire-id invariant: block_event lives at 0x24, immediately after block_change at 0x23.
///
/// <para>The structural conformance leg bypasses the verbatim <c>RawBody</c> copy and (<c>ChunkStructuralConformanceTests</c>, every real chunk frame re-encoded through <see cref="ChunkCodecs.EncodeStructural"/>) and the hand-authored leg in <c>ChunkStructuralFidelityTests</c>.</para>
/// </summary>
public sealed class ChunkDecodeFidelityTests
{
    // The 1.8 clientbound Play id table in registration order, the expected wire mapping. block_event is 0x24, immediately after block_change at 0x23.
    private static readonly string[] Legacy47Clientbound =
    [
        "keep_alive", "login", "chat", "update_time", "entity_equipment", "spawn_position",
        "update_health", "respawn", "player_position", "held_item_slot", "use_bed", "animate",
        "add_player", "collect_item", "add_entity", "add_mob", "add_painting", "add_experience_orb",
        "set_entity_motion", "entity_destroy", "entity", "move_entity_position", "move_entity_rotation",
        "move_entity_position_rotation", "entity_teleport", "rotate_head", "entity_status",
        "attach_entity", "set_entity_data", "entity_effect", "remove_entity_effect", "set_experience",
        "update_attributes", "level_chunk", "block_change_multi", "block_change", "block_event",
        "block_break_animation",
    ];

    [Fact]
    public void Legacy_BlockEvent_ResolvesAtWire0x24_AndReEncodesByteIdentical()
    {
        // Build the descriptor and assert that block_event resolves at wire 0x24 and round-trips.
        var builder = new ProtocolDescriptorBuilder(new GameVersion(GameEdition.Java, "test", 47), new ProtocolFeatures());
        for (int wire = 0; wire < Legacy47Clientbound.Length; wire++)
            PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, wire, $"minecraft:{Legacy47Clientbound[wire]}");

        ProtocolDescriptor descriptor = builder.Build();
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));
        Assert.True(registry.TryGetInbound(0x24, out BoundPacketCodec codec), "block_event is not registered at wire 0x24.");
        Assert.Equal("minecraft:block_event", codec.Type.Id.ToString());

        // A protocol 47 block-event body: packed BlockPos (pre-1.14 layout), instrument, pitch, VarInt block id. Round-trips byte-identically through the resolved codec.
        var sample = new ClientboundBlockEventPacket(new Umpk.Geometry.BlockPos(10, 65, -7), 1, 2, 25);
        byte[] encoded = Encode(WorldBlockCodecs.BlockEventV1_8, sample);
        object decoded = codec.Decode(encoded, PacketCodecContext.Registryless);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, decoded, PacketCodecContext.Registryless);
        Assert.True(encoded.AsSpan().SequenceEqual(buffer.WrittenSpan), "block_event did not round-trip through the 0x24 registration.");
    }

    private static byte[] Encode<T>(PacketCodec<T> codec, T packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }
}
