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
        if (protocol == 770)
        {
            foreach (BlockPredicateEntry predicate in value.Predicates)
                Assert.IsType<BlockPredicateComponentMatchers>(predicate.ComponentMatchers);
            if (vector == "770-exact-recursive")
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
            else if (vector.StartsWith("770-partial-damage-", StringComparison.Ordinal))
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
            Assert.Equal(hash, ItemComponentTable.V1_21_5().ByKey(key).Hash(new HashOps(), value, context));
    }

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
