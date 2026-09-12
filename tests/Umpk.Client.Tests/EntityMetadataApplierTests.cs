using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Verifies entity metadata on protocols 735-758. A marker codec relays these frames without decoding health, custom name, name visibility, pose, age, or item-entity contents for consumers.</summary>
/// <remarks>
/// <para>Nothing here round-trips. Every metadata frame is a LITERAL byte array built to the protocol wire, decoded through the real per-protocol descriptor by wire id (the same entry point the live session uses) and pushed through the real applier chain, and the assertions read concrete VALUES back off the tracked entity's metadata store. A test that only asserted "the entries collection is not empty" would pass under a wrong serializer table too, so every entry carries a distinctive value.</para>
/// <para>The 735 / 754 / 755 / 758 <c>set_entity_data</c> frames carry (index, serializer) pairs 0:BYTE 1:INT 2:OPTIONAL_COMPONENT 3:BOOLEAN 4:BOOLEAN 5:BOOLEAN 6:POSE on every one of them, then 7:BYTE 8:FLOAT on 1.16.x and 7:INT 8:BYTE 9:FLOAT on 1.17+ because 1.17 inserted a frozen-ticks field at index 7 and pushed the living flags and health down by one. POSE resolving at serializer id 18 shows that the era uses the 19-entry 1.14 serializer table, which is what makes the 1.14 codec the right one here.</para>
/// </remarks>
public sealed class EntityMetadataApplierTests
{
    private const int MobId = 0x2BCD;

    // 1.16 - 1.16.5: no ticks-frozen field, so living flags are 7 and health is 8.
    [Theory]
    [InlineData(735)]
    [InlineData(736)]
    [InlineData(751)]
    [InlineData(753)]
    [InlineData(754)]
    public async Task WireLayout1_16_MetadataValuesLandOnTheTrackedEntity(int protocol)
    {
        Entity entity = await ApplyMetadataAsync(protocol, Frame(protocol, healthIndex: 8, ticksFrozen: false));

        AssertSharedFields(entity);

        // Living flags at 7 are a BYTE on this band, not the 1.17 ticks-frozen VarInt.
        Assert.True(entity.Metadata.TryGet(7, out MetadataValue livingFlags));
        Assert.Equal(MetadataValueKind.Byte, livingFlags.Kind);
        Assert.Equal(0x02, livingFlags.AsByte());

        Assert.True(entity.Metadata.TryGet(8, out MetadataValue health));
        Assert.Equal(MetadataValueKind.Float, health.Kind);
        Assert.Equal(17.5f, health.AsFloat());

        // Health is at 8 here and only at 8: nothing was written one slot further down.
        Assert.False(entity.Metadata.TryGet(9, out _));
    }

    // 1.17 - 1.18.2: ticks frozen occupies 7, so living flags are 8 and health is 9.
    [Theory]
    [InlineData(755)]
    [InlineData(756)]
    [InlineData(757)]
    [InlineData(758)]
    public async Task WireLayout1_17_MetadataValuesLandOnTheTrackedEntity(int protocol)
    {
        Entity entity = await ApplyMetadataAsync(protocol, Frame(protocol, healthIndex: 9, ticksFrozen: true));

        AssertSharedFields(entity);

        Assert.True(entity.Metadata.TryGet(7, out MetadataValue frozen));
        Assert.Equal(MetadataValueKind.VarInt, frozen.Kind);
        Assert.Equal(140, frozen.AsVarInt());

        Assert.True(entity.Metadata.TryGet(8, out MetadataValue livingFlags));
        Assert.Equal(MetadataValueKind.Byte, livingFlags.Kind);
        Assert.Equal(0x02, livingFlags.AsByte());

        Assert.True(entity.Metadata.TryGet(9, out MetadataValue health));
        Assert.Equal(MetadataValueKind.Float, health.Kind);
        Assert.Equal(17.5f, health.AsFloat());
    }

    /// <summary>A second frame for the same entity overwrites the fields it carries and leaves the others alone, which is what makes a partial (dirty-only) metadata update usable: vanilla only ever resends the fields that changed.</summary>
    [Theory]
    [InlineData(754, 8)]
    [InlineData(758, 9)]
    public async Task PartialUpdate_OverwritesOnlyTheFieldsItCarries(int protocol, int healthIndex)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = await SpawnAsync(protocol, version!);

        await DecodeAndApplyAsync(harness, protocol, Frame(protocol, healthIndex, ticksFrozen: healthIndex == 9));

        var second = new Writer();
        second.VarInt(MobId);
        second.U8((byte)healthIndex);
        second.VarInt(2);          // FLOAT
        second.F32(3.25f);
        second.U8(0xFF);
        await DecodeAndApplyAsync(harness, protocol, second.ToArray());

        Entity entity = harness.State.Entities.Get(MobId)!;
        Assert.True(entity.Metadata.TryGet(healthIndex, out MetadataValue health));
        Assert.Equal(3.25f, health.AsFloat());

        // The custom name from the first frame is still there.
        Assert.True(entity.Metadata.TryGet(2, out MetadataValue name));
        Assert.Equal("Sentinel", name.AsOptionalComponent()!.ToPlainText());
    }

    /// <summary>Metadata for an entity that is not tracked must not create one, and must not throw. This is the ordering a real session hits when a metadata frame beats its own spawn onto the wire.</summary>
    [Fact]
    public async Task MetadataForAnUntrackedEntity_IsIgnored()
    {
        Assert.True(JavaVersions.TryGetByProtocol(754, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(754);

        await DecodeAndApplyAsync(harness, 754, Frame(754, healthIndex: 8, ticksFrozen: false));

        Assert.Equal(0, harness.State.Entities.Count);
    }

    private static void AssertSharedFields(Entity entity)
    {
        Assert.True(entity.Metadata.TryGet(0, out MetadataValue flags));
        Assert.Equal(MetadataValueKind.Byte, flags.Kind);
        Assert.Equal(0x21, flags.AsByte()); // on fire + sprinting

        Assert.True(entity.Metadata.TryGet(1, out MetadataValue air));
        Assert.Equal(271, air.AsVarInt());

        Assert.True(entity.Metadata.TryGet(2, out MetadataValue name));
        Assert.Equal(MetadataValueKind.OptionalComponent, name.Kind);
        Assert.Equal("Sentinel", name.AsOptionalComponent()!.ToPlainText());

        Assert.True(entity.Metadata.TryGet(3, out MetadataValue nameVisible));
        Assert.True(nameVisible.AsBoolean());

        Assert.True(entity.Metadata.TryGet(6, out MetadataValue pose));
        Assert.Equal(MetadataValueKind.Pose, pose.Kind);
        Assert.Equal(EntityPose.FallFlying, pose.AsPose());
    }

    private static async Task<Entity> ApplyMetadataAsync(int protocol, byte[] frame)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = await SpawnAsync(protocol, version!);
        await DecodeAndApplyAsync(harness, protocol, frame);

        Entity? entity = harness.State.Entities.Get(MobId);
        Assert.NotNull(entity);
        return entity!;
    }

    private static async Task<ApplierHarness> SpawnAsync(int protocol, JavaVersion version)
    {
        var harness = new ApplierHarness(version);
        harness.State.Registries = JavaGameData.Registries(protocol);

        // The spawn itself is not what is under test; it only puts a tracked entity in the store for the literal metadata frame to land on.
        await harness.ApplyAsync(new ClientboundAddMobPacket(
            MobId, 0, 8.5, 64.0, -12.25, 0f, 0f, 0f, 0, 0, 0, new EntityMetadataList([], [])));
        Assert.Equal(1, harness.State.Entities.Count);
        return harness;
    }

    private static async Task DecodeAndApplyAsync(ApplierHarness harness, int protocol, byte[] frame)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "set_entity_data");
        object packet = codec.Decode(frame, PacketCodecContext.Registryless);
        var decoded = Assert.IsType<ClientboundSetEntityDataPacket>(packet);
        Assert.Equal(MobId, decoded.EntityId);
        await harness.ApplyAsync(packet);
    }

    /// <summary>Builds a literal 1.14-serializer-table metadata frame. Serializer ids are the ones the real recorded frames on this band use: 0 BYTE, 1 INT, 2 FLOAT, 5 OPTIONAL_COMPONENT, 7 BOOLEAN, 18 POSE. On this era a component value is a JSON string, not network NBT.</summary>
    private static byte[] Frame(int protocol, int healthIndex, bool ticksFrozen)
    {
        _ = protocol;
        var w = new Writer();
        w.VarInt(MobId);

        w.U8(0);
        w.VarInt(0);                 // BYTE
        w.I8(0x21);                  // shared flags: on fire + sprinting

        w.U8(1);
        w.VarInt(1);                 // INT
        w.VarInt(271);               // air supply

        w.U8(2);
        w.VarInt(5);                 // OPTIONAL_COMPONENT
        w.U8(1);                     // present
        w.Str("{\"text\":\"Sentinel\"}");

        w.U8(3);
        w.VarInt(7);                 // BOOLEAN
        w.U8(1);                     // custom name visible

        w.U8(6);
        w.VarInt(18);                // POSE
        w.VarInt((int)EntityPose.FallFlying);

        if (ticksFrozen)
        {
            w.U8(7);
            w.VarInt(1);             // INT
            w.VarInt(140);           // ticks frozen (1.17+ only)
        }

        w.U8((byte)(healthIndex - 1));
        w.VarInt(0);                 // BYTE
        w.I8(0x02);                  // living-entity flags

        w.U8((byte)healthIndex);
        w.VarInt(2);                 // FLOAT
        w.F32(17.5f);

        w.U8(0xFF);                  // terminator
        return w.ToArray();
    }

    /// <summary>A hand-rolled big-endian frame builder, so no production writer can agree with itself.</summary>
    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public void U8(int value) => _bytes.Add((byte)value);

        public void I8(sbyte value) => _bytes.Add((byte)value);

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

        public void F32(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            _bytes.AddRange(bytes);
        }

        public void Str(string value)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(value);
            VarInt(utf8.Length);
            _bytes.AddRange(utf8);
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
