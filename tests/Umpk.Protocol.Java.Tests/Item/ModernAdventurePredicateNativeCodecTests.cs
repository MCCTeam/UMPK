using System.Buffers;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Original 1.21.5 Adventure predicate codec outputs and DATA hashes, with the original 1.21.4 tooltip control. The ten-row native witness and independent root audit are retained outside Git.</summary>
public sealed class ModernAdventurePredicateNativeCodecTests
{
    [Theory]
    [InlineData("770-wire-zero-predicates", 770, "minecraft:can_break", "AAcAJAEBAQAMAA==", 0, null)]
    [InlineData("770-singleton-empty", 770, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAAA", 1, -982207288)]
    [InlineData("770-multi-direct-tag-state", 770, "minecraft:can_break", "AAcAJAEBAQAMAwECMQEBBGF4aXMBAXkAAAABAsIBAQEDYWdlAAEBMQEBMwAAAAEAGm1pbmVjcmFmdDptaW5lYWJsZS9waWNrYXhlAAAAAA==", 3, -1577352198)]
    [InlineData("770-block-entity-nbt", 770, "minecraft:can_break", "AAcAJAEBAQAMAQECvAEAAQoIAAJpZAAPbWluZWNyYWZ0OmNoZXN0AAAA", 1, -803346133)]
    [InlineData("770-exact-recursive", 770, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAIDAQwBAAAAAAAA", 1, 663199827)]
    [InlineData("770-partial-damage-defaults-omitted", 770, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAABAAoKAAZkYW1hZ2UDAANtaW4AAAABAwADbWF4AAAAAgAA", 1, 1831699757)]
    [InlineData("770-partial-damage-defaults-explicit", 770, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAABAAoKAAZkYW1hZ2UDAANtaW4AAAABAwADbWF4AAAAAgAA", 1, 1831699757)]
    [InlineData("770-can-place-singleton-control", 770, "minecraft:can_place_on", "AAcAJAEBAQALAQAAAAAA", 1, -982207288)]
    [InlineData("771-wire-zero-predicates", 771, "minecraft:can_break", "AAcAJAEBAQAMAA==", 0, null)]
    [InlineData("771-singleton-empty", 771, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAAA", 1, -982207288)]
    [InlineData("771-exact-recursive", 771, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAIDAQwBAAAAAAAA", 1, 663199827)]
    [InlineData("771-partial-damage-defaults-omitted", 771, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAABAAoKAAZkYW1hZ2UDAANtaW4AAAABAwADbWF4AAAAAgAA", 1, 1831699757)]
    [InlineData("771-partial-damage-defaults-explicit", 771, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAABAAoKAAZkYW1hZ2UDAANtaW4AAAABAwADbWF4AAAAAgAA", 1, 1831699757)]
    [InlineData("771-can-place-singleton-control", 771, "minecraft:can_place_on", "AAcAJAEBAQALAQAAAAAA", 1, -982207288)]
    [InlineData("772-wire-zero-predicates", 772, "minecraft:can_break", "AAcAJAEBAQAMAA==", 0, null)]
    [InlineData("772-singleton-empty", 772, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAAA", 1, -982207288)]
    [InlineData("772-exact-recursive", 772, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAIDAQwBAAAAAAAA", 1, 663199827)]
    [InlineData("772-partial-damage-defaults-omitted", 772, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAABAAoKAAZkYW1hZ2UDAANtaW4AAAABAwADbWF4AAAAAgAA", 1, 1831699757)]
    [InlineData("772-partial-damage-defaults-explicit", 772, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAABAAoKAAZkYW1hZ2UDAANtaW4AAAABAwADbWF4AAAAAgAA", 1, 1831699757)]
    [InlineData("772-can-place-singleton-control", 772, "minecraft:can_place_on", "AAcAJAEBAQALAQAAAAAA", 1, -982207288)]
    [InlineData("773-wire-zero-predicates", 773, "minecraft:can_break", "AAcAJAEBAQAMAA==", 0, null)]
    [InlineData("773-singleton-empty", 773, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAAA", 1, -982207288)]
    [InlineData("773-exact-recursive", 773, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAIDAQwBAAAAAAAA", 1, 663199827)]
    [InlineData("773-partial-damage-defaults-omitted", 773, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAABAAoKAAZkYW1hZ2UDAANtaW4AAAABAwADbWF4AAAAAgAA", 1, 1831699757)]
    [InlineData("773-partial-damage-defaults-explicit", 773, "minecraft:can_break", "AAcAJAEBAQAMAQAAAAABAAoKAAZkYW1hZ2UDAANtaW4AAAABAwADbWF4AAAAAgAA", 1, 1831699757)]
    [InlineData("773-can-place-singleton-control", 773, "minecraft:can_place_on", "AAcAJAEBAQALAQAAAAAA", 1, -982207288)]
    [InlineData("769-predecessor-tooltip-false", 769, "minecraft:can_break", "AAcAJAEBAQAMAQECAQAAAA==", 1, null)]
    public void NativeSetSlotPreservesPredicatesBytesAndKnownDataHashes(string vector, int protocol, string componentName, string nativePayload, int predicateCount, int? expectedHash)
    {
        byte[] payload = Convert.FromBase64String(nativePayload);
        var registries = JavaGameData.Registries(protocol);
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.Decode(payload, context));
        Assert.Equal(0, packet.ContainerId);
        Assert.Equal(7, packet.StateId);
        Assert.Equal(36, packet.Slot);
        Assert.Equal(1, packet.Item.Count);
        Assert.Equal(Identifier.Minecraft("stone"), packet.Item.Item.Id);
        DataComponentType<AdventureModePredicateComponent> key = componentName == "minecraft:can_break" ? DataComponents.CanBreak : DataComponents.CanPlaceOn;
        var value = Assert.IsType<AdventureModePredicateComponent>(packet.Item.Components.Get(key));
        Assert.Equal(predicateCount, value.Predicates.Count);
        Assert.Equal(protocol != 769, value.ShowInTooltip);
        if (protocol is >= 770 and <= 773)
        {
            foreach (BlockPredicateEntry predicate in value.Predicates)
                Assert.IsType<BlockPredicateComponentMatchers>(predicate.ComponentMatchers);
            if (vector.EndsWith("-exact-recursive", StringComparison.Ordinal))
            {
                BlockPredicateComponentMatchers matchers = Assert.Single(value.Predicates).ComponentMatchers!;
                Assert.Equal(2, matchers.Exact.Count);
                Assert.Equal(DataComponents.Damage, matchers.Exact[0].Type);
                Assert.Equal(1, Assert.IsType<DamageComponent>(matchers.Exact[0].Value).Value);
                Assert.Equal(DataComponents.CanBreak, matchers.Exact[1].Type);
                var nested = Assert.IsType<AdventureModePredicateComponent>(matchers.Exact[1].Value);
                BlockPredicateComponentMatchers inner = Assert.Single(nested.Predicates).ComponentMatchers!;
                Assert.Empty(inner.Exact);
                Assert.Empty(inner.Partial);
                Assert.Empty(matchers.Partial);
            }
            else if (vector.Contains("-partial-damage-", StringComparison.Ordinal))
            {
                BlockPredicateComponentMatchers matchers = Assert.Single(value.Predicates).ComponentMatchers!;
                Assert.Empty(matchers.Exact);
                BlockPredicatePartialMatcher partial = Assert.Single(matchers.Partial);
                Assert.Equal(Identifier.Minecraft("damage"), partial.Type);
                Assert.False(partial.IsAnyValue);
                var data = Assert.IsType<Umpk.Nbt.NbtCompound>(partial.Value);
                Umpk.Nbt.NbtCompound bounds = data.GetCompound("damage")!;
                Assert.Equal(1, bounds.GetInt("min"));
                Assert.Equal(2, bounds.GetInt("max"));
            }
            else
            {
                foreach (BlockPredicateEntry predicate in value.Predicates)
                {
                    Assert.Empty(predicate.ComponentMatchers!.Exact);
                    Assert.Empty(predicate.ComponentMatchers.Partial);
                }
            }
        }

        if (vector == "770-multi-direct-tag-state")
        {
            Assert.True(registries.Blocks.TryGetNetworkId(Identifier.Minecraft("oak_log"), out int log));
            Assert.True(registries.Blocks.TryGetNetworkId(Identifier.Minecraft("wheat"), out int wheat));
            Assert.Equal(new[] { log }, value.Predicates[0].Blocks!.Ids);
            Assert.Equal(new[] { wheat }, value.Predicates[1].Blocks!.Ids);
            Assert.Equal(Identifier.Minecraft("mineable/pickaxe"), value.Predicates[2].Blocks!.Tag);
            StatePropertyMatcher axis = Assert.Single(value.Predicates[0].Properties!);
            Assert.Equal("axis", axis.Name);
            Assert.True(axis.Exact);
            Assert.Equal("y", axis.Value);
            StatePropertyMatcher age = Assert.Single(value.Predicates[1].Properties!);
            Assert.Equal("age", age.Name);
            Assert.False(age.Exact);
            Assert.Equal("1", age.MinValue);
            Assert.Equal("3", age.MaxValue);
        }
        else if (vector == "770-block-entity-nbt")
            Assert.Equal("minecraft:chest", Assert.Single(value.Predicates).Nbt!.GetString("id"));
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        bound.Encode(ref writer, packet, context);
        Assert.Equal(payload, output.WrittenSpan.ToArray());
        if (expectedHash is { } hash)
            Assert.Equal(hash, Table(protocol).ByKey(key).Hash(new HashOps(), value, context));
    }

    [Fact]
    public void Protocol774RetainsItsUnmodeledAdventureBoundary()
    {
        var registries = JavaGameData.Registries(774);
        ItemComponentTable table = ItemComponentTable.V1_21_11();
        int componentId = table.WireId(DataComponents.CanBreak);
        Assert.Equal(15, componentId);
        Assert.Equal(Identifier.Minecraft("can_break"), table.KeyByWireId(componentId));
        Assert.False(table.TryGetCodec(componentId, out _));
        // Source-composed 774 control: its own component ID and the legal zero-predicate STREAM payload; this is not a native 774 capture.
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        writer.WriteVarInt(0);
        writer.WriteVarInt(7);
        writer.WriteShort(36);
        writer.WriteVarInt(1);
        writer.WriteVarInt(registries.Items[Identifier.Minecraft("stone")].NetworkId);
        writer.WriteVarInt(1);
        writer.WriteVarInt(0);
        writer.WriteVarInt(componentId);
        writer.WriteVarInt(0);
        byte[] payload = output.WrittenSpan.ToArray();
        Assert.Equal("AAcAJAEBAQAPAA==", Convert.ToBase64String(payload));
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        BoundPacketCodec bound = BoundCodec.At(774, PacketFlow.Clientbound, "minecraft:container_set_slot");
        var error = Assert.Throws<UnmodeledItemComponentException>(() => bound.Decode(payload, context));
        Assert.Equal(Identifier.Minecraft("can_break"), error.ComponentId);
    }

    private static ItemComponentTable Table(int protocol) => protocol switch
    {
        770 => ItemComponentTable.V1_21_5(),
        771 or 772 => ItemComponentTable.V1_21_6(),
        773 => ItemComponentTable.V1_21_9(),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };

    [Fact]
    public void NativeUnmodeledRecursiveExactComponentKeepsPacketScopedIdentity()
    {
        byte[] payload = Convert.FromBase64String("AAcAJAEBAQAMAQAAAAEaAQAAAAAA");
        var context = new PacketCodecContext(JavaGameData.Registries(770), IConnectionCodecState.Empty);
        BoundPacketCodec bound = BoundCodec.At(770, PacketFlow.Clientbound, "minecraft:container_set_slot");
        var error = Assert.Throws<UnmodeledItemComponentException>(() => bound.Decode(payload, context));
        Assert.Equal(Identifier.Minecraft("weapon"), error.ComponentId);
    }
}
