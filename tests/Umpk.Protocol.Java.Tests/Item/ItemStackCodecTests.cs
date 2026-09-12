using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;
using AttributeModifierEntry = Umpk.Game.Items.Components.AttributeModifierEntry;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Round-trips the legacy and modern item-stack payload codecs (the family centerpiece). The stacks are carried inside <see cref="ClientboundContainerSetSlotPacket"/> so the tests exercise the real codec entry points. Modern stacks use the 770 and 776 component tables; the typed component set is covered for its wire form and its hashed form.</summary>
public class ItemStackCodecTests
{
    private static ClientboundContainerSetSlotPacket Slot(ItemStack item, bool legacy) =>
        new(ContainerId: 1, StateId: legacy ? 0 : 5, Slot: 3, Item: item, IsLegacy: legacy);

    private static ItemStack Modern(int itemId, int count, DataComponentMap? comps = null) =>
        new(ItemTestRegistries.Item(itemId), count, comps);

    // Legacy 1.8 form.

    [Fact]
    public void Legacy_Empty_RoundTrips()
    {
        var p = Slot(ItemStack.Empty, legacy: true);
        var decoded = ItemCodecRoundTrip.Cycle(ContainerCodecs.SetSlotV1_8, p);
        Assert.True(decoded.Item.IsEmpty);
    }

    [Fact]
    public void Legacy_SubtypeBlock_RoundTrips_WithoutSyntheticDamage()
    {
        // Red wool: (35 << 16) | 14 is registered as a composite subtype, so no DamageComponent is added.
        ItemStack wool = new(ItemTestRegistries.LegacyItem(ItemTestRegistries.LegacyBlockItemId, ItemTestRegistries.LegacyBlockSubtype), 5);
        var decoded = ItemCodecRoundTrip.Cycle(ContainerCodecs.SetSlotV1_8, Slot(wool, legacy: true));
        Assert.Equal(5, decoded.Item.Count);
        Assert.Equal(wool.Item.NetworkId, decoded.Item.Item.NetworkId);
        Assert.False(decoded.Item.Components.TryGet(DataComponents.Damage, out _));
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.SetSlotV1_8, Slot(wool, legacy: true));
    }

    [Fact]
    public void Legacy_ToolDurability_CarriesDamageComponent()
    {
        // Diamond sword base (267 << 16) with wire damage 50 (durability) is not a registered composite, so it falls back to the base entry and the wire damage is preserved as a DamageComponent.
        ItemStack sword = new(ItemTestRegistries.LegacyItem(ItemTestRegistries.LegacyBaseItemId, 0), 1,
            DataComponentMap.Empty.With(DataComponents.Damage, new DamageComponent(50)));
        var decoded = ItemCodecRoundTrip.Cycle(ContainerCodecs.SetSlotV1_8, Slot(sword, legacy: true));
        Assert.True(decoded.Item.Components.TryGet(DataComponents.Damage, out DamageComponent? d));
        Assert.Equal(50, d!.Value);
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.SetSlotV1_8, Slot(sword, legacy: true));
    }

    [Fact]
    public void Legacy_WithNbt_RoundTripsAsLegacyNbtComponent()
    {
        var tag = new NbtCompound();
        tag.PutInt("RepairCost", 7);
        ItemStack sword = new(ItemTestRegistries.LegacyItem(ItemTestRegistries.LegacyBaseItemId, 0), 1,
            DataComponentMap.Empty.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag)));
        var decoded = ItemCodecRoundTrip.Cycle(ContainerCodecs.SetSlotV1_8, Slot(sword, legacy: true));
        Assert.True(decoded.Item.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy));
        Assert.True(legacy!.Nbt.TryGet("RepairCost", out NbtInt? rc));
        Assert.Equal(7, rc!.Value);
    }

    // Modern 770 and 776 forms.

    public static TheoryData<bool> Eras => new() { false, true };

    private static PacketCodec<ClientboundContainerSetSlotPacket> SetSlot(bool era776) =>
        era776 ? ContainerCodecs.ContainerSetSlot776 : ContainerCodecs.ContainerSetSlot770;

    [Theory]
    [MemberData(nameof(Eras))]
    public void Modern_Empty_RoundTrips(bool era776)
    {
        var decoded = ItemCodecRoundTrip.Cycle(SetSlot(era776), Slot(ItemStack.Empty, legacy: false));
        Assert.True(decoded.Item.IsEmpty);
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void Modern_NoComponents_RoundTrips(bool era776)
    {
        var decoded = ItemCodecRoundTrip.Cycle(SetSlot(era776), Slot(Modern(ItemTestRegistries.Stone, 64), legacy: false));
        Assert.Equal(64, decoded.Item.Count);
        Assert.Equal(ItemTestRegistries.Stone, decoded.Item.Item.NetworkId);
        Assert.True(decoded.Item.Components.HasNoPatch);
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void Modern_ScalarComponents_RoundTrip(bool era776)
    {
        DataComponentMap comps = DataComponentMap.Empty
            .With(DataComponents.Damage, new DamageComponent(120))
            .With(DataComponents.MaxDamage, new MaxDamageComponent(1561))
            .With(DataComponents.RepairCost, new RepairCostComponent(3))
            .With(DataComponents.CustomName, new CustomNameComponent(Component.Text("Excalibur")));
        var p = Slot(Modern(ItemTestRegistries.DiamondSword, 1, comps), legacy: false);
        var decoded = ItemCodecRoundTrip.Cycle(SetSlot(era776), p);
        Assert.True(decoded.Item.Components.TryGet(DataComponents.Damage, out DamageComponent? d));
        Assert.Equal(120, d!.Value);
        Assert.True(decoded.Item.Components.TryGet(DataComponents.CustomName, out CustomNameComponent? name));
        Assert.Equal("Excalibur", name!.Name.ToPlainText());
        ItemCodecRoundTrip.AssertByteStable(SetSlot(era776), p);
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void Modern_PotDecorations_RoundTrip_RawIdsWithoutRegistry(bool era776)
    {
        // The sherd ids are deliberately OUTSIDE the test item registry: the codec retains raw ids, so a decorated-pot icon decodes even when the session registry cannot resolve the sherd items (the component reaches clients through advancement icons, which must never fault).
        DataComponentMap comps = DataComponentMap.Empty.With(
            DataComponents.PotDecorations, new PotDecorationsComponent([901, 902, 903, 904]));
        var p = Slot(Modern(ItemTestRegistries.Stone, 1, comps), legacy: false);
        var decoded = ItemCodecRoundTrip.Cycle(SetSlot(era776), p);
        Assert.True(decoded.Item.Components.TryGet(DataComponents.PotDecorations, out PotDecorationsComponent? pot));
        Assert.Equal([901, 902, 903, 904], pot!.SherdItemIds);
        ItemCodecRoundTrip.AssertByteStable(SetSlot(era776), p);
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void Modern_Enchantments_RoundTrip(bool era776)
    {
        DataComponentMap comps = DataComponentMap.Empty.With(
            DataComponents.Enchantments,
            new EnchantmentsComponent([new EnchantmentInstance(ItemTestRegistries.Enchantment(ItemTestRegistries.Sharpness), 5)]));
        var p = Slot(Modern(ItemTestRegistries.DiamondSword, 1, comps), legacy: false);
        var decoded = ItemCodecRoundTrip.Cycle(SetSlot(era776), p);
        Assert.True(decoded.Item.Components.TryGet(DataComponents.Enchantments, out EnchantmentsComponent? ench));
        Assert.Single(ench!.Enchantments);
        Assert.Equal(5, ench.Enchantments[0].Level);
        Assert.Equal(ItemTestRegistries.Sharpness, ench.Enchantments[0].Enchantment.NetworkId);
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void Modern_AttributeModifiers_RoundTrip_AcrossWireLayouts(bool era776)
    {
        DataComponentMap comps = DataComponentMap.Empty.With(
            DataComponents.AttributeModifiers,
            new AttributeModifiersComponent(
            [
                new AttributeModifierEntry(
                    ItemTestRegistries.Attribute(ItemTestRegistries.AttackDamage),
                    "minecraft:base_attack_damage", 7.0, "add_value", "mainhand"),
            ]));
        var p = Slot(Modern(ItemTestRegistries.DiamondSword, 1, comps), legacy: false);
        var decoded = ItemCodecRoundTrip.Cycle(SetSlot(era776), p);
        Assert.True(decoded.Item.Components.TryGet(DataComponents.AttributeModifiers, out AttributeModifiersComponent? mods));
        Assert.Single(mods!.Modifiers);
        Assert.Equal(7.0, mods.Modifiers[0].Amount);
        Assert.Equal("mainhand", mods.Modifiers[0].Slot);
    }

    [Fact]
    public void AttributeModifiers_V26_2_EncodesDisplayField_ThusLongerThan_770()
    {
        DataComponentMap comps = DataComponentMap.Empty.With(
            DataComponents.AttributeModifiers,
            new AttributeModifiersComponent(
            [
                new AttributeModifierEntry(
                    ItemTestRegistries.Attribute(ItemTestRegistries.AttackDamage),
                    "minecraft:base_attack_damage", 1.0, "add_value", "mainhand"),
            ]));
        var p = Slot(Modern(ItemTestRegistries.DiamondSword, 1, comps), legacy: false);
        byte[] b770 = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlot770, p);
        byte[] b776 = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlot776, p);
        // The 26.2 shape adds one VarInt display field (value 0 => DEFAULT) per modifier.
        Assert.Equal(b770.Length + 1, b776.Length);
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void Modern_CustomData_Nbt_RoundTrips(bool era776)
    {
        var tag = new NbtCompound();
        tag.PutInt("id", 42);
        DataComponentMap comps = DataComponentMap.Empty.With(DataComponents.CustomData, new CustomDataComponent(tag));
        var p = Slot(Modern(ItemTestRegistries.Stone, 1, comps), legacy: false);
        var decoded = ItemCodecRoundTrip.Cycle(SetSlot(era776), p);
        Assert.True(decoded.Item.Components.TryGet(DataComponents.CustomData, out CustomDataComponent? data));
        Assert.True(data!.Data.TryGet("id", out NbtInt? id));
        Assert.Equal(42, id!.Value);
        ItemCodecRoundTrip.AssertByteStable(SetSlot(era776), p);
    }

    [Theory]
    [MemberData(nameof(Eras))]
    public void Modern_ComponentRemoval_RoundTrips(bool era776)
    {
        // A removal is only emitted when the item prototype carries the component, so seed a prototype with damage and then remove it. The wire carries this as a removed-component id.
        var prototype = new Dictionary<DataComponentType, object> { [DataComponents.Damage] = new DamageComponent(10) };
        DataComponentMap comps = DataComponentMap.FromPrototype(prototype).Without(DataComponents.Damage);
        Assert.Contains(comps.Patch, e => e.IsRemoval && e.Type == DataComponents.Damage);

        var p = Slot(Modern(ItemTestRegistries.DiamondSword, 1, comps), legacy: false);
        var decoded = ItemCodecRoundTrip.Cycle(SetSlot(era776), p);
        Assert.Contains(decoded.Item.Components.Patch, e => e.IsRemoval && e.Type == DataComponents.Damage);
    }

    [Fact]
    public void UnknownItemId_Throws()
    {
        // A container-set-content payload with an item id absent from the registry must fault, not desync.
        var reader = MakeStackBytes(itemId: 9999, count: 1);
        Assert.Throws<ProtocolViolationException>(() =>
        {
            var r = new PacketReader(reader);
            _ = ItemStackCodecs.ReadModernStack(ref r, ItemTestRegistries.Context, ItemStackCodecs.ComponentsV1_21_5);
        });
    }

    private static byte[] MakeStackBytes(int itemId, int count)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(count);
        w.WriteVarInt(itemId);
        w.WriteVarInt(0); // added
        w.WriteVarInt(0); // removed
        return buffer.WrittenSpan.ToArray();
    }
}
