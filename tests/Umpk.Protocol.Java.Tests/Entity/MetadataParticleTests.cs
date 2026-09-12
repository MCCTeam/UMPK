using System.Buffers;
using Umpk.Game.Entities;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Particle entity-metadata values reach a consumer without consuming later metadata entries.</summary>
/// <remarks>
/// <para>Raw-tailing a PARTICLE or PARTICLES value stops structured decoding and absorbs every later field. These tests require the particle and following entries to decode independently.</para>
/// <para>Deliberately driven through <see cref="BoundCodec"/>, so what is being tested is the codec the registrar picks for a protocol number, not a codec the test chose. The behavioral assertions cover in-place codec changes that identity pins cannot observe.</para>
/// </remarks>
public sealed class MetadataParticleTests
{
    private const string SetEntityData = "minecraft:set_entity_data";

    /// <summary>A PARTICLES (list) metadata value decodes to real particles, and an entry placed AFTER it still decodes instead of being swallowed into the raw tail.</summary>
    /// <remarks>The serializer ids are per-era table data, pinned here rather than derived: 768/769 use the 31-entry pre-1.21.5 table (PARTICLES at 18), 770-772 the 35-entry 1.21.5 table (18), and 773+ the tables that dropped COMPOUND_TAG's mid-list slot (17). Vanilla gains the PARTICLES serializer at 1.21.2, which is why the sweep starts at 768.</remarks>
    [Theory]
    [InlineData(768, 18)]
    [InlineData(769, 18)]
    [InlineData(770, 18)]
    [InlineData(772, 18)]
    [InlineData(773, 17)]
    [InlineData(774, 17)]
    [InlineData(775, 17)]
    [InlineData(776, 17)]
    public void ParticlesValue_DecodesAndDoesNotSwallowTheRestOfTheList(int protocol, int particlesSerializerId)
    {
        // index 8 = a two-particle list; index 9 = a plain byte that must remain independently decoded.
        byte[] frame = Frame(
            entityId: 7,
            [
                (8, particlesSerializerId, ParticlesValue(blockStateIds: [3, 11])),
                (9, ByteSerializerId, [0x2A]),
            ]);

        var packet = (ClientboundSetEntityDataPacket)Decode(protocol, frame);

        Assert.Empty(packet.Metadata.RawTail);
        Assert.Equal(2, packet.Metadata.Entries.Count);

        EntityDataEntry particles = packet.Metadata.Entries[0];
        Assert.Equal(8, particles.Index);
        Assert.Equal(MetadataValueKind.Particles, particles.Value.Kind);
        IReadOnlyList<object> decoded = particles.Value.AsParticles();
        Assert.Equal(2, decoded.Count);
        Assert.Equal(BlockParticleTypeId, ((ParticleData)decoded[0]).TypeId);
        Assert.Equal(new byte[] { 3 }, ((ParticleData)decoded[0]).Options);
        Assert.Equal(new byte[] { 11 }, ((ParticleData)decoded[1]).Options);

        // The entry behind the particles proves that the list parser consumes exactly its own payload.
        EntityDataEntry trailing = packet.Metadata.Entries[1];
        Assert.Equal(9, trailing.Index);
        Assert.Equal(42, trailing.Value.AsByte());

        // And it still goes back out byte-for-byte.
        Assert.Equal(frame, Encode(protocol, packet));
    }

    /// <summary>The single PARTICLE serializer decodes on the same span, with the same guarantee.</summary>
    /// <remarks>Ids again per era: 768/769 and 770-772 put PARTICLE at 17, and 773+ at 16 (one slot earlier, the COMPOUND_TAG removal).</remarks>
    [Theory]
    [InlineData(768, 17)]
    [InlineData(770, 17)]
    [InlineData(773, 16)]
    [InlineData(776, 16)]
    public void ParticleValue_DecodesAndDoesNotSwallowTheRestOfTheList(int protocol, int particleSerializerId)
    {
        byte[] frame = Frame(
            entityId: 7,
            [
                (8, particleSerializerId, [BlockParticleTypeId, 5]),
                (9, ByteSerializerId, [0x2A]),
            ]);

        var packet = (ClientboundSetEntityDataPacket)Decode(protocol, frame);

        Assert.Empty(packet.Metadata.RawTail);
        Assert.Equal(2, packet.Metadata.Entries.Count);
        Assert.Equal(MetadataValueKind.Particle, packet.Metadata.Entries[0].Value.Kind);

        var particle = (ParticleData)packet.Metadata.Entries[0].Value.AsParticle();
        Assert.Equal(BlockParticleTypeId, particle.TypeId);
        Assert.Equal(new byte[] { 5 }, particle.Options);
        Assert.Equal(42, packet.Metadata.Entries[1].Value.AsByte());
        Assert.Equal(frame, Encode(protocol, packet));
    }

    /// <summary>Below 1.21.2 a particle is still captured verbatim, honestly: there is no particle option-shape table to decode against on those eras. Pins the boundary so a later "wire it everywhere" change has to face the missing table rather than guess at it.</summary>
    [Theory]
    [InlineData(764, 17)]   // 1.20.2, V1_21_5 serializer table, no shape table exists
    [InlineData(767, 17)]   // 1.21.1, likewise
    public void BelowTheShapeTables_ParticlesAreStillCaptured(int protocol, int particleSerializerId)
    {
        byte[] frame = Frame(
            entityId: 7,
            [
                (8, particleSerializerId, [BlockParticleTypeId, 5]),
                (9, ByteSerializerId, [0x2A]),
            ]);

        var packet = (ClientboundSetEntityDataPacket)Decode(protocol, frame);

        Assert.Empty(packet.Metadata.Entries);
        Assert.NotEmpty(packet.Metadata.RawTail);

        // Captured, not corrupted: the frame still round-trips exactly.
        Assert.Equal(frame, Encode(protocol, packet));
    }

    // Frame construction.

    /// <summary>The BYTE serializer, id 0 in every modern era table.</summary>
    private const int ByteSerializerId = 0;

    /// <summary><c>minecraft:block</c>, particle type id 1 on every shape table from 766 to 776, and the one entry that is <c>ParticleOptionShape.VarInt</c> on all of them, so one option byte is the payload.</summary>
    private const byte BlockParticleTypeId = 1;

    private static byte[] ParticlesValue(byte[] blockStateIds)
    {
        var value = new List<byte> { (byte)blockStateIds.Length };
        foreach (byte state in blockStateIds)
        {
            value.Add(BlockParticleTypeId);
            value.Add(state);
        }

        return [.. value];
    }

    /// <summary>Builds a modern set_entity_data frame: a VarInt entity id, then (index byte, VarInt serializer id, value) entries, terminated by 0xFF. Every id used here is single-byte.</summary>
    private static byte[] Frame(int entityId, (int Index, int SerializerId, byte[] Value)[] entries)
    {
        var bytes = new List<byte> { (byte)entityId };
        foreach ((int index, int serializerId, byte[] value) in entries)
        {
            bytes.Add((byte)index);
            bytes.Add((byte)serializerId);
            bytes.AddRange(value);
        }

        bytes.Add(0xFF);
        return [.. bytes];
    }

    private static object Decode(int protocol, byte[] frame) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, SetEntityData)
            .Decode(frame, PacketCodecContext.Registryless);

    private static byte[] Encode(int protocol, ClientboundSetEntityDataPacket packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        BoundCodec.At(protocol, PacketFlow.Clientbound, SetEntityData)
            .Encode(ref writer, packet, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }
}
