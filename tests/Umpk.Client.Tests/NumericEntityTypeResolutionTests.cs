using System.Buffers;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Pre-1.14 servers spawn entities through TWO disjoint wire id spaces: <c>add_mob</c> (SpawnMob) carries a living-entity id and <c>add_entity</c> (SpawnObject) carries an object id, and the same number means different things in each. These tests hand-build real frame bytes for KNOWN mobs and KNOWN objects on 47 (1.8) and 340 (1.12.2), decode them through the real per-protocol descriptor by wire id (the same entrypoint the live session uses), push them through the real applier chain against the real <see cref="JavaGameData"/> registries, and assert the resolved <see cref="Entity.Type"/> key. Nothing here round-trips: every frame is a literal byte array, so a wrong codec or a wrong id space cannot agree with itself.</summary>
public sealed class NumericEntityTypeResolutionTests
{
    // add_mob wire 0x0F: VarInt id, byte type, 3x int fixed-point (coord * 32), byte yaw, byte pitch, byte headPitch, 3x short velocity, metadata. add_entity wire 0x0E: VarInt id, byte type, 3x int fixed-point, byte pitch, byte yaw, int data, and 3x short velocity only when data > 0.

    [Theory]
    [InlineData(92, "minecraft:cow")]
    [InlineData(54, "minecraft:zombie")]
    [InlineData(50, "minecraft:creeper")]
    [InlineData(120, "minecraft:villager")]
    public async Task Protocol47_AddMob_ResolvesLivingIdSpace(int mobId, string expected)
    {
        byte[] body = Mob47(entityId: 0x1234, type: (byte)mobId);
        Entity entity = await SpawnAsync(47, 0x0F, body);
        Assert.Equal(expected, entity.Type.Id.ToString());
        Assert.Equal(0x1234, entity.Id);
        Assert.Equal(133.5, entity.Position.X, 3);
    }

    [Theory]
    [InlineData(1, "minecraft:boat")]
    [InlineData(2, "minecraft:item")]
    [InlineData(10, "minecraft:minecart")]
    [InlineData(50, "minecraft:tnt")]
    [InlineData(60, "minecraft:arrow")]
    [InlineData(70, "minecraft:falling_block")]
    [InlineData(78, "minecraft:armor_stand")]
    public async Task Protocol47_AddEntity_ResolvesObjectIdSpace(int objectId, string expected)
    {
        byte[] body = Object47(entityId: 0x5678, type: (byte)objectId);
        Entity entity = await SpawnAsync(47, 0x0E, body);
        Assert.Equal(expected, entity.Type.Id.ToString());
        Assert.Equal(0x5678, entity.Id);
    }

    // add_mob wire 0x03: VarInt id, uuid, byte type, 3x double, byte yaw, byte pitch, byte headPitch, 3x short velocity, metadata. add_entity wire 0x00: VarInt id, uuid, byte type, 3x double, byte pitch, byte yaw, int data, 3x short velocity (always present from 1.9).

    [Theory]
    [InlineData(92, "minecraft:cow")]
    [InlineData(54, "minecraft:zombie")]
    [InlineData(50, "minecraft:creeper")]
    [InlineData(105, "minecraft:parrot")]
    public async Task Protocol340_AddMob_ResolvesLivingIdSpace(int mobId, string expected)
    {
        byte[] body = Mob340(entityId: 0x2244, type: (byte)mobId);
        Entity entity = await SpawnAsync(340, 0x03, body);
        Assert.Equal(expected, entity.Type.Id.ToString());
        Assert.Equal(0x2244, entity.Id);
        Assert.Equal(133.5, entity.Position.X, 6);
    }

    [Theory]
    [InlineData(1, "minecraft:boat")]
    [InlineData(2, "minecraft:item")]
    [InlineData(3, "minecraft:area_effect_cloud")]
    [InlineData(10, "minecraft:minecart")]
    [InlineData(51, "minecraft:ender_crystal")]
    [InlineData(76, "minecraft:fireworks_rocket")]
    [InlineData(79, "minecraft:evocation_fangs")]
    [InlineData(93, "minecraft:dragon_fireball")]
    public async Task Protocol340_AddEntity_ResolvesObjectIdSpace(int objectId, string expected)
    {
        byte[] body = Object340(entityId: 0x3355, type: (byte)objectId);
        Entity entity = await SpawnAsync(340, 0x00, body);
        Assert.Equal(expected, entity.Type.Id.ToString());
        Assert.Equal(0x3355, entity.Id);
    }

    // 1.9, 1.10, 1.11 and 1.12 share the split-spawn wire shape; the mob space grew (polar_bear at 1.10, llama/vex at 1.11, parrot at 1.12) and the object space gained llama_spit and evocation_fangs at 1.11, so each protocol is checked against a type that exists on it.

    [Theory]
    [InlineData(107, 92, "minecraft:cow")]
    [InlineData(107, 54, "minecraft:zombie")]
    [InlineData(210, 102, "minecraft:polar_bear")]
    [InlineData(315, 103, "minecraft:llama")]
    [InlineData(315, 35, "minecraft:vex")]
    [InlineData(335, 105, "minecraft:parrot")]
    public async Task PreFlatteningBand_AddMob_ResolvesLivingIdSpace(int protocol, int mobId, string expected)
    {
        Entity entity = await SpawnAsync(protocol, 0x03, Mob340(entityId: 0x11, type: (byte)mobId));
        Assert.Equal(expected, entity.Type.Id.ToString());
    }

    [Theory]
    [InlineData(107, 1, "minecraft:boat")]
    [InlineData(107, 70, "minecraft:falling_block")]
    [InlineData(210, 51, "minecraft:ender_crystal")]
    [InlineData(315, 68, "minecraft:llama_spit")]
    [InlineData(335, 79, "minecraft:evocation_fangs")]
    public async Task PreFlatteningBand_AddEntity_ResolvesObjectIdSpace(int protocol, int objectId, string expected)
    {
        Entity entity = await SpawnAsync(protocol, 0x00, Object340(entityId: 0x22, type: (byte)objectId));
        Assert.Equal(expected, entity.Type.Id.ToString());
    }

    /// <summary>1.13 is a HYBRID and this is the test that catches it: add_mob writes the FLAT <c>minecraft:entity_type</c> registry id while add_entity still writes the LEGACY object id. The two spaces disagree on every number, so decoding a 1.13 mob through the object space, or the reverse, yields the wrong entity name.</summary>
    [Theory]
    [InlineData(393, 0x03, 9, "minecraft:cow")]
    [InlineData(393, 0x03, 87, "minecraft:zombie")]
    [InlineData(393, 0x03, 17, "minecraft:ender_dragon")]
    [InlineData(404, 0x03, 9, "minecraft:cow")]
    public async Task Protocol1_13_AddMob_UsesFlatRegistryId(int protocol, int wireId, int typeId, string expected)
    {
        Entity entity = await SpawnAsync(protocol, wireId, Mob340(entityId: 0x33, type: (byte)typeId));
        Assert.Equal(expected, entity.Type.Id.ToString());
    }

    [Theory]
    [InlineData(393, 1, "minecraft:boat")]
    [InlineData(393, 94, "minecraft:trident")]
    [InlineData(404, 90, "minecraft:fishing_bobber")]
    [InlineData(404, 79, "minecraft:evoker_fangs")]
    public async Task Protocol1_13_AddEntity_StillUsesLegacyObjectId(int protocol, int objectId, string expected)
    {
        Entity entity = await SpawnAsync(protocol, 0x00, Object340(entityId: 0x44, type: (byte)objectId));
        Assert.Equal(expected, entity.Type.Id.ToString());
    }

    /// <summary>The two spaces really are disjoint, so a number must NOT mean the same thing in both. If a future change collapses them into one table this test is what fails.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(340)]
    public async Task ObjectAndMobSpaces_DisagreeOnTheSameNumber(int protocol)
    {
        (int mobWire, int objectWire) = protocol == 47 ? (0x0F, 0x0E) : (0x03, 0x00);
        byte[] mobBody = protocol == 47 ? Mob47(1, 1) : Mob340(1, 1);
        byte[] objectBody = protocol == 47 ? Object47(2, 1) : Object340(2, 1);

        Entity asMob = await SpawnAsync(protocol, mobWire, mobBody);
        Entity asObject = await SpawnAsync(protocol, objectWire, objectBody);

        Assert.Equal("minecraft:item", asMob.Type.Id.ToString());
        Assert.Equal("minecraft:boat", asObject.Type.Id.ToString());
        Assert.NotEqual(asMob.Type.Id, asObject.Type.Id);
    }

    /// <summary><c>add_player</c>, <c>add_experience_orb</c>, and <c>add_painting</c> carry no type id. Their entity types must be resolved by packet identity rather than network id 0, which identifies <c>minecraft:area_effect_cloud</c> on flattened versions.</summary>
    [Theory]
    [InlineData(770)]
    [InlineData(776)]
    public async Task ModernSpawns_WithoutATypeId_ResolveByKey(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);

        await harness.ApplyAsync(new ClientboundAddPlayerPacket(
            11, Guid.NewGuid(), 1, 2, 3, 0, 0, 0, new EntityMetadataList([], [])));
        await harness.ApplyAsync(new ClientboundAddExperienceOrbPacket(12, 1, 2, 3, 5));

        Assert.Equal("minecraft:player", harness.State.Entities.Get(11)!.Type.Id.ToString());
        Assert.Equal("minecraft:experience_orb", harness.State.Entities.Get(12)!.Type.Id.ToString());
    }

    private static async Task<Entity> SpawnAsync(int protocol, int wireId, byte[] body)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        ProtocolDescriptor descriptor = version!.Protocol;
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));
        Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
        Assert.True(codec.IsImplemented, $"protocol {protocol} wire 0x{wireId:X2} is not implemented");

        object packet = codec.Decode(body, PacketCodecContext.Registryless);

        var harness = new ApplierHarness(version);
        harness.State.Registries = JavaGameData.Registries(protocol);
        await harness.ApplyAsync(packet);

        EntityStore store = harness.State.Entities;
        Assert.Equal(1, store.Count);
        return store.All.First();
    }

    private static byte[] Mob47(int entityId, byte type)
    {
        var w = new Writer();
        w.VarInt(entityId);
        w.U8(type);
        w.I32(4272);   // x = 133.5 * 32
        w.I32(2080);   // y = 65.0 * 32
        w.I32(-1208);  // z = -37.75 * 32
        w.U8(64);      // yaw
        w.U8(200);     // pitch
        w.U8(32);      // head pitch
        w.I16(120);
        w.I16(-40);
        w.I16(9);
        w.U8(0x00);    // metadata index 0, type 0 (byte)
        w.U8(0x21);    // flags value: on fire + sprinting
        w.U8(0x66);    // index 6, type 3 (float): health
        w.F32(18.5f);
        w.U8(0x7F);    // terminator
        return w.ToArray();
    }

    private static byte[] Object47(int entityId, byte type)
    {
        var w = new Writer();
        w.VarInt(entityId);
        w.U8(type);
        w.I32(4272);
        w.I32(2080);
        w.I32(-1208);
        w.U8(12);      // pitch
        w.U8(212);     // yaw
        w.I32(7);      // object data > 0, so velocity follows
        w.I16(-300);
        w.I16(55);
        w.I16(1024);
        return w.ToArray();
    }

    private static byte[] Mob340(int entityId, byte type)
    {
        var w = new Writer();
        w.VarInt(entityId);
        w.Uuid();
        w.U8(type);
        w.F64(133.5);
        w.F64(65.0);
        w.F64(-37.75);
        w.U8(64);
        w.U8(200);
        w.U8(32);
        w.I16(120);
        w.I16(-40);
        w.I16(9);
        w.U8(0x00);    // 1.9+ typed metadata: index 0
        w.U8(0x00);    // serializer 0 (byte)
        w.U8(0x21);
        w.U8(0xFF);    // 1.9+ terminator
        return w.ToArray();
    }

    private static byte[] Object340(int entityId, byte type)
    {
        var w = new Writer();
        w.VarInt(entityId);
        w.Uuid();
        w.U8(type);
        w.F64(133.5);
        w.F64(65.0);
        w.F64(-37.75);
        w.U8(12);
        w.U8(212);
        w.I32(7);
        w.I16(-300);
        w.I16(55);
        w.I16(1024);
        return w.ToArray();
    }

    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public void U8(int value) => _bytes.Add((byte)value);

        public void VarInt(int value)
        {
            uint v = (uint)value;
            while ((v & ~0x7Fu) != 0)
            {
                _bytes.Add((byte)((v & 0x7F) | 0x80));
                v >>= 7;
            }

            _bytes.Add((byte)v);
        }

        public void I16(short value)
        {
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)value);
        }

        public void I32(int value)
        {
            _bytes.Add((byte)(value >> 24));
            _bytes.Add((byte)(value >> 16));
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)value);
        }

        public void F32(float value) => WriteBigEndian(BitConverter.GetBytes(value));

        public void F64(double value) => WriteBigEndian(BitConverter.GetBytes(value));

        public void Uuid()
        {
            for (int i = 0; i < 16; i++)
                _bytes.Add((byte)(0xA0 + i));

        }

        public byte[] ToArray() => [.. _bytes];

        private void WriteBigEndian(byte[] littleEndian)
        {
            Array.Reverse(littleEndian);
            _bytes.AddRange(littleEndian);
        }
    }
}
