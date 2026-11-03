using System.Buffers;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public sealed class LaterAdventureNativeCodecTests
{
    public static IEnumerable<object?[]> NativeCases()
    {
        yield return ["774-CAN_BREAK-simple", 774, false, "simple", "AAcAJAEBAQAPAQECAQAAAAA=", -1161294463];
        yield return ["774-CAN_BREAK-concrete-empty", 774, false, "concrete-empty", "AAcAJAEBAQAPAQECAQAAAAEBAAoA", -6628392];
        yield return ["774-CAN_BREAK-concrete-ranged", 774, false, "concrete-ranged", "AAcAJAEBAQAPAQAAAAABAQAKCgAGZGFtYWdlAwADbWluAAAAAQMAA21heAAAAAQAAA==", -520394857];
        yield return ["774-CAN_BREAK-exact-damage", 774, false, "exact-damage", "AAcAJAEBAQAPAQAAAAEDAwA=", -1843603740];
        yield return ["774-CAN_BREAK-recursive", 774, false, "recursive", "AAcAJAEBAQAPAQAAAAEPAQECAQAAAAAA", -1601370338];
        yield return ["774-CAN_BREAK-empty-wire-list", 774, false, "empty-wire-list", "AAcAJAEBAQAPAA==", null];
        yield return ["774-CAN_BREAK-any-damage", 774, false, "any-damage", "AAcAJAEBAQAPAQECAQAAAAEAAwoA", -6628392];
        yield return ["774-CAN_BREAK-any-rarity", 774, false, "any-rarity", "AAcAJAEBAQAPAQECAQAAAAEADAoA", -1820373396];
        yield return ["774-CAN_BREAK-any-damage-duplicate", 774, false, "any-damage-duplicate", "AAcAJAEBAQAPAQECAQAAAAIAAwoAAAMKAA==", -686728813];
        yield return ["774-CAN_BREAK-mixed-damage", 774, false, "mixed-damage", "AAcAJAEBAQAPAQECAQAAAAIBAAoAAAMKAA==", -686728813];
        yield return ["774-CAN_PLACE_ON-simple", 774, true, "simple", "AAcAJAEBAQAOAQECAQAAAAA=", -1161294463];
        yield return ["774-CAN_PLACE_ON-concrete-empty", 774, true, "concrete-empty", "AAcAJAEBAQAOAQECAQAAAAEBAAoA", -6628392];
        yield return ["774-CAN_PLACE_ON-concrete-ranged", 774, true, "concrete-ranged", "AAcAJAEBAQAOAQAAAAABAQAKCgAGZGFtYWdlAwADbWluAAAAAQMAA21heAAAAAQAAA==", -520394857];
        yield return ["774-CAN_PLACE_ON-exact-damage", 774, true, "exact-damage", "AAcAJAEBAQAOAQAAAAEDAwA=", -1843603740];
        yield return ["774-CAN_PLACE_ON-recursive", 774, true, "recursive", "AAcAJAEBAQAOAQAAAAEPAQECAQAAAAAA", -1601370338];
        yield return ["774-CAN_PLACE_ON-empty-wire-list", 774, true, "empty-wire-list", "AAcAJAEBAQAOAA==", null];
        yield return ["774-CAN_PLACE_ON-any-damage", 774, true, "any-damage", "AAcAJAEBAQAOAQECAQAAAAEAAwoA", -6628392];
        yield return ["774-CAN_PLACE_ON-any-rarity", 774, true, "any-rarity", "AAcAJAEBAQAOAQECAQAAAAEADAoA", -1820373396];
        yield return ["774-CAN_PLACE_ON-any-damage-duplicate", 774, true, "any-damage-duplicate", "AAcAJAEBAQAOAQECAQAAAAIAAwoAAAMKAA==", -686728813];
        yield return ["774-CAN_PLACE_ON-mixed-damage", 774, true, "mixed-damage", "AAcAJAEBAQAOAQECAQAAAAIBAAoAAAMKAA==", -686728813];
        yield return ["775-CAN_BREAK-simple", 775, false, "simple", "AAcAJAEBAQAPAQECAQAAAAA=", -1161294463];
        yield return ["775-CAN_BREAK-concrete-empty", 775, false, "concrete-empty", "AAcAJAEBAQAPAQECAQAAAAEBAAoA", -6628392];
        yield return ["775-CAN_BREAK-concrete-ranged", 775, false, "concrete-ranged", "AAcAJAEBAQAPAQAAAAABAQAKCgAGZGFtYWdlAwADbWluAAAAAQMAA21heAAAAAQAAA==", -520394857];
        yield return ["775-CAN_BREAK-exact-damage", 775, false, "exact-damage", "AAcAJAEBAQAPAQAAAAEDAwA=", -1843603740];
        yield return ["775-CAN_BREAK-recursive", 775, false, "recursive", "AAcAJAEBAQAPAQAAAAEPAQECAQAAAAAA", -1601370338];
        yield return ["775-CAN_BREAK-empty-wire-list", 775, false, "empty-wire-list", "AAcAJAEBAQAPAA==", null];
        yield return ["775-CAN_BREAK-any-damage", 775, false, "any-damage", "AAcAJAEBAQAPAQECAQAAAAEAAwoA", -6628392];
        yield return ["775-CAN_BREAK-any-rarity", 775, false, "any-rarity", "AAcAJAEBAQAPAQECAQAAAAEADAoA", -1820373396];
        yield return ["775-CAN_BREAK-any-damage-duplicate", 775, false, "any-damage-duplicate", "AAcAJAEBAQAPAQECAQAAAAIAAwoAAAMKAA==", -686728813];
        yield return ["775-CAN_BREAK-mixed-damage", 775, false, "mixed-damage", "AAcAJAEBAQAPAQECAQAAAAIAAwoAAQAKAA==", -686728813];
        yield return ["775-CAN_PLACE_ON-simple", 775, true, "simple", "AAcAJAEBAQAOAQECAQAAAAA=", -1161294463];
        yield return ["775-CAN_PLACE_ON-concrete-empty", 775, true, "concrete-empty", "AAcAJAEBAQAOAQECAQAAAAEBAAoA", -6628392];
        yield return ["775-CAN_PLACE_ON-concrete-ranged", 775, true, "concrete-ranged", "AAcAJAEBAQAOAQAAAAABAQAKCgAGZGFtYWdlAwADbWluAAAAAQMAA21heAAAAAQAAA==", -520394857];
        yield return ["775-CAN_PLACE_ON-exact-damage", 775, true, "exact-damage", "AAcAJAEBAQAOAQAAAAEDAwA=", -1843603740];
        yield return ["775-CAN_PLACE_ON-recursive", 775, true, "recursive", "AAcAJAEBAQAOAQAAAAEPAQECAQAAAAAA", -1601370338];
        yield return ["775-CAN_PLACE_ON-empty-wire-list", 775, true, "empty-wire-list", "AAcAJAEBAQAOAA==", null];
        yield return ["775-CAN_PLACE_ON-any-damage", 775, true, "any-damage", "AAcAJAEBAQAOAQECAQAAAAEAAwoA", -6628392];
        yield return ["775-CAN_PLACE_ON-any-rarity", 775, true, "any-rarity", "AAcAJAEBAQAOAQECAQAAAAEADAoA", -1820373396];
        yield return ["775-CAN_PLACE_ON-any-damage-duplicate", 775, true, "any-damage-duplicate", "AAcAJAEBAQAOAQECAQAAAAIAAwoAAAMKAA==", -686728813];
        yield return ["775-CAN_PLACE_ON-mixed-damage", 775, true, "mixed-damage", "AAcAJAEBAQAOAQECAQAAAAIAAwoAAQAKAA==", -686728813];
        yield return ["776-CAN_BREAK-simple", 776, false, "simple", "AAcAJAEBAQAPAQECAQAAAAA=", -1161294463];
        yield return ["776-CAN_BREAK-concrete-empty", 776, false, "concrete-empty", "AAcAJAEBAQAPAQECAQAAAAEBAAoA", -6628392];
        yield return ["776-CAN_BREAK-concrete-ranged", 776, false, "concrete-ranged", "AAcAJAEBAQAPAQAAAAABAQAKCgAGZGFtYWdlAwADbWluAAAAAQMAA21heAAAAAQAAA==", -520394857];
        yield return ["776-CAN_BREAK-exact-damage", 776, false, "exact-damage", "AAcAJAEBAQAPAQAAAAEDAwA=", -1843603740];
        yield return ["776-CAN_BREAK-recursive", 776, false, "recursive", "AAcAJAEBAQAPAQAAAAEPAQECAQAAAAAA", -1601370338];
        yield return ["776-CAN_BREAK-empty-wire-list", 776, false, "empty-wire-list", "AAcAJAEBAQAPAA==", null];
        yield return ["776-CAN_BREAK-any-damage", 776, false, "any-damage", "AAcAJAEBAQAPAQECAQAAAAEAAwoA", -6628392];
        yield return ["776-CAN_BREAK-any-rarity", 776, false, "any-rarity", "AAcAJAEBAQAPAQECAQAAAAEADAoA", -1820373396];
        yield return ["776-CAN_BREAK-any-damage-duplicate", 776, false, "any-damage-duplicate", "AAcAJAEBAQAPAQECAQAAAAIAAwoAAAMKAA==", -686728813];
        yield return ["776-CAN_BREAK-mixed-damage", 776, false, "mixed-damage", "AAcAJAEBAQAPAQECAQAAAAIBAAoAAAMKAA==", -686728813];
        yield return ["776-CAN_PLACE_ON-simple", 776, true, "simple", "AAcAJAEBAQAOAQECAQAAAAA=", -1161294463];
        yield return ["776-CAN_PLACE_ON-concrete-empty", 776, true, "concrete-empty", "AAcAJAEBAQAOAQECAQAAAAEBAAoA", -6628392];
        yield return ["776-CAN_PLACE_ON-concrete-ranged", 776, true, "concrete-ranged", "AAcAJAEBAQAOAQAAAAABAQAKCgAGZGFtYWdlAwADbWluAAAAAQMAA21heAAAAAQAAA==", -520394857];
        yield return ["776-CAN_PLACE_ON-exact-damage", 776, true, "exact-damage", "AAcAJAEBAQAOAQAAAAEDAwA=", -1843603740];
        yield return ["776-CAN_PLACE_ON-recursive", 776, true, "recursive", "AAcAJAEBAQAOAQAAAAEPAQECAQAAAAAA", -1601370338];
        yield return ["776-CAN_PLACE_ON-empty-wire-list", 776, true, "empty-wire-list", "AAcAJAEBAQAOAA==", null];
        yield return ["776-CAN_PLACE_ON-any-damage", 776, true, "any-damage", "AAcAJAEBAQAOAQECAQAAAAEAAwoA", -6628392];
        yield return ["776-CAN_PLACE_ON-any-rarity", 776, true, "any-rarity", "AAcAJAEBAQAOAQECAQAAAAEADAoA", -1820373396];
        yield return ["776-CAN_PLACE_ON-any-damage-duplicate", 776, true, "any-damage-duplicate", "AAcAJAEBAQAOAQECAQAAAAIAAwoAAAMKAA==", -686728813];
        yield return ["776-CAN_PLACE_ON-mixed-damage", 776, true, "mixed-damage", "AAcAJAEBAQAOAQECAQAAAAIAAwoAAQAKAA==", -686728813];
        yield return ["777-CAN_BREAK-simple", 777, false, "simple", "AAcAJAEBAQAPAQECAQAAAAA=", -1161294463];
        yield return ["777-CAN_BREAK-concrete-empty", 777, false, "concrete-empty", "AAcAJAEBAQAPAQECAQAAAAEBAAoA", -6628392];
        yield return ["777-CAN_BREAK-concrete-ranged", 777, false, "concrete-ranged", "AAcAJAEBAQAPAQAAAAABAQAKCgAGZGFtYWdlAwADbWluAAAAAQMAA21heAAAAAQAAA==", -520394857];
        yield return ["777-CAN_BREAK-exact-damage", 777, false, "exact-damage", "AAcAJAEBAQAPAQAAAAEDAwA=", -1843603740];
        yield return ["777-CAN_BREAK-recursive", 777, false, "recursive", "AAcAJAEBAQAPAQAAAAEPAQECAQAAAAAA", -1601370338];
        yield return ["777-CAN_BREAK-empty-wire-list", 777, false, "empty-wire-list", "AAcAJAEBAQAPAA==", null];
        yield return ["777-CAN_BREAK-any-damage", 777, false, "any-damage", "AAcAJAEBAQAPAQECAQAAAAEAAwoA", -6628392];
        yield return ["777-CAN_BREAK-any-rarity", 777, false, "any-rarity", "AAcAJAEBAQAPAQECAQAAAAEADAoA", -1820373396];
        yield return ["777-CAN_BREAK-any-damage-duplicate", 777, false, "any-damage-duplicate", "AAcAJAEBAQAPAQECAQAAAAIAAwoAAAMKAA==", -686728813];
        yield return ["777-CAN_BREAK-mixed-damage", 777, false, "mixed-damage", "AAcAJAEBAQAPAQECAQAAAAIAAwoAAQAKAA==", -686728813];
        yield return ["777-CAN_PLACE_ON-simple", 777, true, "simple", "AAcAJAEBAQAOAQECAQAAAAA=", -1161294463];
        yield return ["777-CAN_PLACE_ON-concrete-empty", 777, true, "concrete-empty", "AAcAJAEBAQAOAQECAQAAAAEBAAoA", -6628392];
        yield return ["777-CAN_PLACE_ON-concrete-ranged", 777, true, "concrete-ranged", "AAcAJAEBAQAOAQAAAAABAQAKCgAGZGFtYWdlAwADbWluAAAAAQMAA21heAAAAAQAAA==", -520394857];
        yield return ["777-CAN_PLACE_ON-exact-damage", 777, true, "exact-damage", "AAcAJAEBAQAOAQAAAAEDAwA=", -1843603740];
        yield return ["777-CAN_PLACE_ON-recursive", 777, true, "recursive", "AAcAJAEBAQAOAQAAAAEPAQECAQAAAAAA", -1601370338];
        yield return ["777-CAN_PLACE_ON-empty-wire-list", 777, true, "empty-wire-list", "AAcAJAEBAQAOAA==", null];
        yield return ["777-CAN_PLACE_ON-any-damage", 777, true, "any-damage", "AAcAJAEBAQAOAQECAQAAAAEAAwoA", -6628392];
        yield return ["777-CAN_PLACE_ON-any-rarity", 777, true, "any-rarity", "AAcAJAEBAQAOAQECAQAAAAEADAoA", -1820373396];
        yield return ["777-CAN_PLACE_ON-any-damage-duplicate", 777, true, "any-damage-duplicate", "AAcAJAEBAQAOAQECAQAAAAIAAwoAAAMKAA==", -686728813];
        yield return ["777-CAN_PLACE_ON-mixed-damage", 777, true, "mixed-damage", "AAcAJAEBAQAOAQECAQAAAAIAAwoAAQAKAA==", -686728813];
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void OriginalSetSlotVectorsRetainNativeBranchValuesHashesAndRawBytes(string witness, int protocol, bool placement, string scenario, string payload, int? nativeHash)
    {
        Assert.StartsWith($"{protocol}-", witness);
        byte[] bytes = Convert.FromBase64String(payload);
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(bytes, context));
        Assert.Equal(0, packet.ContainerId);
        Assert.Equal(7, packet.StateId);
        Assert.Equal(36, packet.Slot);
        Assert.Equal("minecraft:stone", packet.Item.Item.Id.ToString());
        Assert.Equal(1, packet.Item.Count);
        var key = placement ? DataComponents.CanPlaceOn : DataComponents.CanBreak;
        AdventureModePredicateComponent component = Assert.IsType<AdventureModePredicateComponent>(packet.Item.Components.Get(key));
        Assert.True(component.ShowInTooltip);
        Assert.Equal(scenario == "empty-wire-list" ? 0 : 1, component.Predicates.Count);
        if (component.Predicates.Count != 0)
        {
            BlockPredicateEntry predicate = Assert.Single(component.Predicates);
            Assert.Null(predicate.Nbt);
            Assert.Null(predicate.Properties);
            BlockPredicateComponentMatchers matchers = Assert.IsType<BlockPredicateComponentMatchers>(predicate.ComponentMatchers);
            if (scenario is "simple" or "concrete-empty" or "any-damage" or "any-rarity" or "any-damage-duplicate" or "mixed-damage")
                Assert.Equal(new[] { context.Registries.Blocks[Identifier.Minecraft("stone")].NetworkId }, predicate.Blocks!.Ids);
            if (scenario is "any-damage" or "any-rarity" or "any-damage-duplicate")
            {
                Assert.Equal(scenario == "any-damage-duplicate" ? 2 : 1, matchers.Partial.Count);
                foreach (BlockPredicatePartialMatcher part in matchers.Partial)
                {
                    Assert.True(part.IsAnyValue);
                    Assert.Equal(Identifier.Minecraft(scenario == "any-rarity" ? "rarity" : "damage"), part.Type);
                    Assert.Empty(Assert.IsType<NbtCompound>(part.Value));
                }
            }
            else if (scenario == "mixed-damage")
            {
                Assert.Equal(2, matchers.Partial.Count);
                Assert.Contains(matchers.Partial, part => part.IsAnyValue && part.Type == Identifier.Minecraft("damage"));
                Assert.Contains(matchers.Partial, part => !part.IsAnyValue && part.Type == Identifier.Minecraft("damage"));
            }
            else if (scenario.StartsWith("concrete-", StringComparison.Ordinal))
            {
                BlockPredicatePartialMatcher part = Assert.Single(matchers.Partial);
                Assert.False(part.IsAnyValue);
                Assert.Equal(Identifier.Minecraft("damage"), part.Type);
                var nbt = Assert.IsType<NbtCompound>(part.Value);
                if (scenario == "concrete-ranged")
                {
                    NbtCompound damage = Assert.IsType<NbtCompound>(nbt.GetCompound("damage"));
                    Assert.Equal(1, damage.GetInt("min"));
                    Assert.Equal(4, damage.GetInt("max"));
                }
                else
                    Assert.Empty(nbt);
            }
            if (scenario == "exact-damage")
                Assert.Equal(3, Assert.IsType<DamageComponent>(Assert.Single(matchers.Exact).Value).Value);
            if (scenario == "recursive")
                Assert.Single(Assert.IsType<AdventureModePredicateComponent>(Assert.Single(matchers.Exact).Value).Predicates);
        }
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        Assert.Equal(bytes, buffer.WrittenSpan.ToArray());
        ItemComponentTable table = Table(protocol);
        if (nativeHash is { } expected)
            Assert.Equal(expected, table.ByKey(key).Hash(new HashOps(), component, context));
        else
            Assert.Throws<ArgumentException>(() => ItemStackCodecs.ToHashedStack(packet.Item, table, context));
    }

    [Theory]
    [InlineData(774, "duplicate-concrete")]
    [InlineData(775, "duplicate-concrete")]
    [InlineData(776, "duplicate-concrete")]
    [InlineData(777, "duplicate-concrete")]
    [InlineData(774, "unknown-presence")]
    [InlineData(775, "unknown-presence")]
    [InlineData(776, "unknown-presence")]
    [InlineData(777, "unknown-presence")]
    [InlineData(774, "villager-type")]
    [InlineData(775, "villager-type")]
    [InlineData(776, "villager-type")]
    [InlineData(777, "villager-type")]
    public void ConcreteDuplicatesAndRegistryBoundariesKeepTheirFaultKinds(int protocol, string kind)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(0);
        writer.WriteVarInt(7);
        writer.WriteShort(36);
        writer.WriteVarInt(1);
        writer.WriteVarInt(JavaGameData.Registries(protocol).Items[Identifier.Minecraft("stone")].NetworkId);
        writer.WriteVarInt(1);
        writer.WriteVarInt(0);
        writer.WriteVarInt(15);
        writer.WriteVarInt(1);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteVarInt(0);
        writer.WriteVarInt(kind == "duplicate-concrete" ? 2 : 1);
        int count = kind == "duplicate-concrete" ? 2 : 1;
        for (int index = 0; index < count; index++)
        {
            writer.WriteBool(kind != "unknown-presence");
            writer.WriteVarInt(kind == "villager-type" ? 14 : kind == "unknown-presence" ? int.MaxValue : 0);
            writer.WriteNbt(kind == "villager-type" ? new NbtList(NbtTagType.End) : new NbtCompound(), ItemCodecPrimitives.NetworkNbt);
        }
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");
        byte[] payload = buffer.WrittenSpan.ToArray();
        if (kind == "villager-type" && protocol >= 775)
        {
            UnmodeledItemComponentException missing = Assert.Throws<UnmodeledItemComponentException>(() => codec.Decode(payload, context));
            Assert.Equal(Identifier.Minecraft("can_break"), missing.ComponentId);
            Assert.Contains("selected partial predicate", missing.Message, StringComparison.Ordinal);
        }
        else
            Assert.Throws<ProtocolViolationException>(() => codec.Decode(payload, context));
    }

    [Fact]
    public void ActualSelectedAdventureFormParticipatesInTheTableFingerprint()
    {
        var factory = typeof(ItemComponentTable).GetMethod("BuildModern", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(factory);
        System.Reflection.ParameterInfo[] parameters = factory.GetParameters();
        Assert.Equal(3, parameters.Length);
        Type form = parameters[2].ParameterType;
        ItemComponentTable Build(string name) => Assert.IsType<ItemComponentTable>(factory.Invoke(null, [ItemComponentLayout.V1_21_11, true, Enum.Parse(form, name)]));
        ItemComponentTable plain = Build("Plain14");
        ItemComponentTable either14 = Build("Either14");
        ItemComponentTable either15 = Build("Either15");
        Assert.NotEqual(plain.ShapeToken, either14.ShapeToken);
        Assert.NotEqual(either14.ShapeToken, either15.ShapeToken);
        Assert.Equal(either14.ShapeToken, ItemComponentTable.V1_21_11().ShapeToken);
        ItemComponentTable previous = Assert.IsType<ItemComponentTable>(factory.Invoke(null, [ItemComponentLayout.V1_21_9, true, Enum.Parse(form, "Plain14")]));
        Assert.Equal(ItemComponentTable.V1_21_9().ShapeToken, previous.ShapeToken);
    }

    private static ItemComponentTable Table(int protocol) => protocol switch
    {
        774 => ItemComponentTable.V1_21_11(),
        775 => ItemComponentTable.V26_1(),
        776 => ItemComponentTable.V26_2(),
        777 => ItemComponentTable.V26_3(),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };
}
