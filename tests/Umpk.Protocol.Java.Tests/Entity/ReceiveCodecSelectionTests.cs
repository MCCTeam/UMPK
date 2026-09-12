using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Byte-level round-trip tests for the mid-era (1.14-1.20.1) RECEIVE codecs added for client parity: the pre-1.19 clientbound chat (with/without the 1.16 sender UUID), the era-correct update/remove mob-effect codecs (byte vs VarInt effect id, and the 1.19 trailing factor NBT), and the pre-1.19 spawn_entity_living (add_mob) with and without the trailing 1.14 metadata.</summary>
public class ReceiveCodecSelectionTests
{
    // Clientbound chat.

    [Fact]
    public void ChatV1_8_RoundTrips_NoSenderUuid()
    {
        var p = new ClientboundLegacyChatPacket(Component.Text("hello world"), Position: 1);
        byte[] bytes = CodecRoundTrip.Encode(ChatCodecs.ClientLegacyV1_8, p);
        var d = CodecRoundTrip.Cycle(ChatCodecs.ClientLegacyV1_8, p);
        Assert.Equal("hello world", d.Message.ToPlainText());
        Assert.Equal((byte)1, d.Position);
        Assert.Null(d.Sender);
        // No trailing UUID: the string (VarInt len + JSON) + one position byte only.
        Assert.Equal((byte)1, bytes[^1]);
    }

    [Fact]
    public void ChatV1_16_RoundTrips_WithSenderUuid()
    {
        var sender = Guid.NewGuid();
        var p = new ClientboundLegacyChatPacket(Component.Text("hi"), Position: 0, Sender: sender);
        var d = CodecRoundTrip.Cycle(ChatCodecs.ClientLegacyV1_16, p);
        Assert.Equal("hi", d.Message.ToPlainText());
        Assert.Equal((byte)0, d.Position);
        Assert.Equal(sender, d.Sender);
    }

    [Fact]
    public void ChatV1_16_CarriesSixteenMoreBytesThanV1_8()
    {
        var msg = Component.Text("same message");
        byte[] v18 = CodecRoundTrip.Encode(ChatCodecs.ClientLegacyV1_8, new ClientboundLegacyChatPacket(msg, 1));
        byte[] v116 = CodecRoundTrip.Encode(ChatCodecs.ClientLegacyV1_16, new ClientboundLegacyChatPacket(msg, 1, Guid.Empty));
        Assert.Equal(v18.Length + 16, v116.Length);
    }

    [Fact]
    public void Chat_RawJson_ReEncodesVerbatim()
    {
        // A component whose JSON does not survive the parse/serialise round-trip byte-for-byte (key order
        // + simple-component collapse). Decoding retains the raw JSON so re-encode is byte-exact.
        const string rawJson = "{\"text\":\"a\",\"bold\":true,\"extra\":[{\"text\":\"b\"}]}";
        byte[] wire = BuildChatWire(rawJson, position: 1, sender: null);
        var decoded = CodecRoundTrip.Decode(ChatCodecs.ClientLegacyV1_8, wire);
        Assert.Equal(rawJson, decoded.RawJson);
        byte[] reencoded = CodecRoundTrip.Encode(ChatCodecs.ClientLegacyV1_8, decoded);
        Assert.Equal(wire, reencoded);
    }

    // update_mob_effect.

    [Fact]
    public void UpdateMobEffect_V1_8_ByteEffectId()
    {
        var p = new ClientboundUpdateMobEffectPacket(EntityId: 42, EffectId: 1, Amplifier: 0, Duration: 600, Flags: 0);
        byte[] bytes = CodecRoundTrip.Encode(EntityEffectCodecs.UpdateMobEffectV1_8, p);
        var d = CodecRoundTrip.Cycle(EntityEffectCodecs.UpdateMobEffectV1_8, p);
        Assert.Equal(1, d.EffectId);
        Assert.Equal(0, d.Amplifier);
        Assert.Equal(600, d.Duration);
        // varint eid(1) + i8 effect(1) + i8 amp(1) + varint dur(2) + i8 flags(1) = 6 bytes.
        Assert.Equal(6, bytes.Length);
    }

    [Fact]
    public void UpdateMobEffect_V1_18_VarIntEffectId_NoFactor()
    {
        var p = new ClientboundUpdateMobEffectPacket(EntityId: 42, EffectId: 200, Amplifier: 3, Duration: 600, Flags: 5);
        var d = CodecRoundTrip.Cycle(EntityEffectCodecs.UpdateMobEffectV1_18, p);
        Assert.Equal(200, d.EffectId); // > 127: a byte effect id would corrupt this; VarInt survives.
        Assert.Equal(3, d.Amplifier);
        Assert.Equal((byte)5, d.Flags);
        Assert.Null(d.FactorData);
    }

    [Fact]
    public void UpdateMobEffect_V1_19_NoFactor_HasTrailingFalseByte()
    {
        var p = new ClientboundUpdateMobEffectPacket(EntityId: 7, EffectId: 1, Amplifier: 0, Duration: 100, Flags: 1);
        byte[] bytes = CodecRoundTrip.Encode(EntityEffectCodecs.UpdateMobEffectV1_19, p);
        var d = CodecRoundTrip.Cycle(EntityEffectCodecs.UpdateMobEffectV1_19, p);
        Assert.Null(d.FactorData);
        // ...flags byte, then the option-present boolean = 0x00. The V1_18 encoding of the same fields is one byte shorter (no trailing option boolean).
        Assert.Equal((byte)0, bytes[^1]);
        Assert.Equal(CodecRoundTrip.Encode(EntityEffectCodecs.UpdateMobEffectV1_18, p).Length + 1, bytes.Length);
    }

    [Fact]
    public void UpdateMobEffect_V1_19_WithFactorData_RoundTrips()
    {
        var factor = new NbtCompound();
        factor.PutString("dummy", "value");
        var p = new ClientboundUpdateMobEffectPacket(EntityId: 7, EffectId: 33, Amplifier: 1, Duration: 200, Flags: 2, FactorData: factor);
        var d = CodecRoundTrip.Cycle(EntityEffectCodecs.UpdateMobEffectV1_19, p);
        Assert.NotNull(d.FactorData);
        Assert.Equal(factor, d.FactorData);
        Assert.Equal(33, d.EffectId);
    }

    // remove_mob_effect.

    [Fact]
    public void RemoveMobEffect_V1_8_ByteEffectId()
    {
        var p = new ClientboundRemoveMobEffectPacket(EntityId: 42, EffectId: 1);
        byte[] bytes = CodecRoundTrip.Encode(EntityEffectCodecs.RemoveMobEffectV1_8, p);
        var d = CodecRoundTrip.Cycle(EntityEffectCodecs.RemoveMobEffectV1_8, p);
        Assert.Equal(1, d.EffectId);
        Assert.Equal(2, bytes.Length); // varint eid(1) + i8 effect(1).
    }

    // add_mob (spawn_entity_living).

    [Fact]
    public void AddMob_V1_15_NoTrailingMetadata()
    {
        var uuid = Guid.NewGuid();
        var p = new ClientboundAddMobPacket(
            EntityId: 99, TypeId: 12, X: 1.5, Y: 64.0, Z: -3.5,
            Yaw: 0, Pitch: 0, HeadPitch: 0, VelocityX: 10, VelocityY: 20, VelocityZ: 30,
            Metadata: new EntityMetadataList([], []), Uuid: uuid);
        var d = CodecRoundTrip.Cycle(EntitySpawnCodecs.AddMobV1_15, p);
        Assert.Equal(99, d.EntityId);
        Assert.Equal(12, d.TypeId);
        Assert.Equal(uuid, d.Uuid);
        Assert.Equal((short)10, d.VelocityX);
        Assert.Empty(d.Metadata.Entries);
    }

    [Fact]
    public void AddMob_V1_14_WithEmptyMetadata_RoundTrips()
    {
        var uuid = Guid.NewGuid();
        var p = new ClientboundAddMobPacket(
            EntityId: 5, TypeId: 3, X: 0.0, Y: 70.0, Z: 0.0,
            Yaw: 0, Pitch: 0, HeadPitch: 0, VelocityX: 0, VelocityY: 0, VelocityZ: 0,
            Metadata: new EntityMetadataList([], []), Uuid: uuid);
        var d = CodecRoundTrip.Cycle(EntitySpawnCodecs.AddMobV1_14, p);
        Assert.Equal(5, d.EntityId);
        Assert.Equal(uuid, d.Uuid);
        Assert.Empty(d.Metadata.Entries);
    }

    private static byte[] BuildChatWire(string json, byte position, Guid? sender)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteString(json, 262144);
        w.WriteByte(position);
        if (sender is { } s)
            w.WriteUuid(s);

        return buffer.WrittenSpan.ToArray();
    }
}
