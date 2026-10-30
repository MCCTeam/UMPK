using System.Buffers;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public sealed class ModernAdventurePredicateGuardTests
{
    [Theory]
    [InlineData("count-over64")]
    [InlineData("unknown-registry-id")]
    [InlineData("null-selected-nbt")]
    [InlineData("selected-root-not-map")]
    [InlineData("duplicate-partial-type")]
    [InlineData("damage-field-not-number-or-map")]
    [InlineData("damage-bound-not-number")]
    public void SourceEstablishedMalformedPartialFramesStayFramingFaults(string kind)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        writer.WriteVarInt(0);
        writer.WriteVarInt(7);
        writer.WriteShort(36);
        writer.WriteVarInt(1);
        writer.WriteVarInt(1);
        writer.WriteVarInt(1);
        writer.WriteVarInt(0);
        writer.WriteVarInt(12);
        writer.WriteVarInt(1);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteVarInt(0);
        writer.WriteVarInt(kind == "count-over64" ? 65 : kind == "duplicate-partial-type" ? 2 : 1);
        if (kind == "duplicate-partial-type")
        {
            for (int index = 0; index < 2; index++)
            {
                writer.WriteVarInt(0);
                writer.WriteByte(10);
                writer.WriteByte(0);
            }
        }
        else if (kind != "count-over64")
        {
            writer.WriteVarInt(kind == "unknown-registry-id" ? 14 : 0);
            if (kind is "damage-field-not-number-or-map" or "damage-bound-not-number")
            {
                var damage = new Umpk.Nbt.NbtCompound
                {
                    ["damage"] = kind == "damage-field-not-number-or-map"
                        ? new Umpk.Nbt.NbtString("bad")
                        : new Umpk.Nbt.NbtCompound { ["min"] = new Umpk.Nbt.NbtString("bad") },
                };
                writer.WriteNbt(damage, ItemCodecPrimitives.NetworkNbt);
            }
            else if (kind == "selected-root-not-map")
            {
                writer.WriteByte(3);
                writer.WriteInt(1);
            }
            else
                writer.WriteByte(0);
        }
        byte[] payload = output.WrittenSpan.ToArray();
        var context = new PacketCodecContext(JavaGameData.Registries(770), IConnectionCodecState.Empty);
        BoundPacketCodec codec = BoundCodec.At(770, PacketFlow.Clientbound, "minecraft:container_set_slot");
        Assert.Throws<ProtocolViolationException>(() => codec.Decode(payload, context));
    }

    [Fact]
    public void NativeWireEmptyPredicateListHasNoInventedDataHash()
    {
        var context = new PacketCodecContext(JavaGameData.Registries(770), IConnectionCodecState.Empty);
        BoundPacketCodec codec = BoundCodec.At(770, PacketFlow.Clientbound, "minecraft:container_set_slot");
        // Unchanged original-client codec output: legal wire list0, but native DATA HashOps returns 'List must have contents'.
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(Convert.FromBase64String("AAcAJAEBAQAMAA=="), context));
        AdventureModePredicateComponent value = packet.Item.Components.Get(DataComponents.CanBreak)!;
        Assert.Empty(value.Predicates);
        Assert.ThrowsAny<InvalidOperationException>(() => ItemComponentTable.V1_21_5().ByKey(DataComponents.CanBreak).Hash(new HashOps(), value, context));
    }
}
