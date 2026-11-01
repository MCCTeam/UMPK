using System.Buffers;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Client.Tests.Support;
using Xunit;
using Fixture = Umpk.Client.Tests.MiningItemIdentityRuntimeTests.Fixture;

namespace Umpk.Client.Tests;

public sealed class MiningEnchantmentEraRuntimeTests
{
    public static IEnumerable<object[]> ArmorCases()
    {
        foreach (int protocol in new[] { 47, 340, 393, 498, 573, 765 })
        {
            yield return [protocol, "correct", 6];
            yield return [protocol, "wrong_key", 29];
            yield return [protocol, "wrong_id_type", 29];
            yield return [protocol, "first_zero", 29];
            yield return [protocol, "first_one", 6];
            yield return [protocol, "byte_level", 6];
            yield return [protocol, "float_level", 6];
            yield return [protocol, "double_level", 6];
            yield return [protocol, "long_level", 6];
            yield return [protocol, "wide_int_level", protocol < 393 ? 29 : 6];
            yield return [protocol, "negative_wide_level", protocol < 393 ? 6 : 29];
            if (protocol < 393)
                yield return [protocol, "wrapped_numeric_id", 6];
        }
    }

    [Theory]
    [MemberData(nameof(ArmorCases))]
    public async Task NativeArmorNbtShapeAndLevelReachScheduledMining(int protocol, string scenario, int ticks)
    {
        await using var fixture = new Fixture(protocol);
        fixture.Physics = new Umpk.Physics.PhysicsState { IsUnderWater = true, OnGround = true };
        await fixture.HoldAsync("diamond_pickaxe", 0);
        NbtCompound tag = EnchantmentTag(protocol, "aqua_affinity", 6, scenario);
        ItemStack stack = new ItemStack(fixture.State.Registries!.Items[Identifier.Minecraft("diamond_helmet")], 1)
            .With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
        await SetSlotCheckedAsync(fixture, protocol, 5, stack);
        Assert.Equal(tag, fixture.State.Inventory.GetSlot(5).Components.Get(DataComponents.LegacyNbt)!.Nbt);
        await AssertFinishesAsync(fixture, protocol, ticks);
    }

    [Theory]
    [InlineData(47, "wrong_key", 6)]
    [InlineData(340, "wrong_key", 6)]
    [InlineData(393, "wrong_key", 6)]
    [InlineData(498, "wrong_id_type", 6)]
    [InlineData(393, "wide_int_level", 5)]
    [InlineData(498, "wide_int_level", 5)]
    [InlineData(573, "wide_int_level", 1)]
    [InlineData(765, "wide_int_level", 1)]
    [InlineData(498, "negative_wide_level", 6)]
    [InlineData(573, "negative_wide_level", 6)]
    public async Task NativeEfficiencyUsesTheSameEraLookupAndIntegerBonus(int protocol, string scenario, int ticks)
    {
        await using var fixture = new Fixture(protocol);
        fixture.Physics = new Umpk.Physics.PhysicsState { IsUnderWater = false, OnGround = true };
        NbtCompound tag = EnchantmentTag(protocol, "efficiency", 32, scenario);
        ItemStack stack = new ItemStack(fixture.State.Registries!.Items[Identifier.Minecraft("diamond_pickaxe")], 1)
            .With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
        await SetSlotCheckedAsync(fixture, protocol, 36, stack);
        Assert.Equal(tag, fixture.State.Inventory.GetSlot(36).Components.Get(DataComponents.LegacyNbt)!.Nbt);
        await AssertFinishesAsync(fixture, protocol, ticks);
    }

    [Theory]
    [InlineData(766, false, 29)]
    [InlineData(766, true, 6)]
    [InlineData(767, false, 29)]
    [InlineData(768, false, 29)]
    [InlineData(777, false, 29)]
    public async Task ComponentsAndServerAttributeBoundaryDoNotReinterpretCustomData(int protocol, bool enchanted, int ticks)
    {
        await using var fixture = new Fixture(protocol);
        fixture.Physics = new Umpk.Physics.PhysicsState { IsUnderWater = true, OnGround = true };
        await fixture.HoldAsync("diamond_pickaxe", 0);
        ItemStack helmet = new(fixture.State.Registries!.Items[Identifier.Minecraft("diamond_helmet")], 1);
        helmet = helmet.With(DataComponents.CustomData, new CustomDataComponent(EnchantmentTag(765, "aqua_affinity", 6, "correct")));
        if (enchanted)
        {
            Assert.True(fixture.State.Registries.Enchantments.TryGet(Identifier.Minecraft("aqua_affinity"), out var enchantment));
            helmet = helmet.With(DataComponents.Enchantments, new EnchantmentsComponent([new EnchantmentInstance(enchantment, 1)]));
        }
        await SetSlotCheckedAsync(fixture, protocol, 5, helmet);
        Assert.True(fixture.State.Inventory.GetSlot(5).Components.TryGet(DataComponents.CustomData, out CustomDataComponent? decoded));
        Assert.Equal(helmet.Components.Get(DataComponents.CustomData)!.Data, decoded.Data);
        await AssertFinishesAsync(fixture, protocol, ticks);
    }

    [Theory]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(777)]
    public async Task ModernSubmergedAttributePacketOwnsTheMiningModifier(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.Physics = new Umpk.Physics.PhysicsState { IsUnderWater = true, OnGround = true };
        await fixture.HoldAsync("diamond_pickaxe", 0);
        string name = protocol == 767 ? "player.submerged_mining_speed" : "submerged_mining_speed";
        Assert.True(fixture.State.Registries!.Attributes.TryGetNetworkId(Identifier.Minecraft(name), out int id));
        var modifier = new Umpk.Protocol.Java.Packets.AttributeModifierEntry(Guid.Empty, "minecraft:enchantment.aqua_affinity", 4, 2);
        var packet = new ClientboundUpdateAttributesPacket(fixture.State.Self.EntityId, [new AttributeSnapshot(null, id, 0.2, [modifier])]);
        var codec = BoundDescriptorCodec.Clientbound(protocol, "update_attributes");
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        var first = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(first);
        codec.Encode(ref writer, packet, context);
        var decoded = Assert.IsType<ClientboundUpdateAttributesPacket>(codec.Decode(first.WrittenSpan, context));
        Assert.Equal(0.2, Assert.Single(decoded.Attributes).BaseValue);
        Assert.Equal(4.0, Assert.Single(decoded.Attributes[0].Modifiers).Amount);
        var second = new ArrayBufferWriter<byte>();
        var otherWriter = new PacketWriter(second);
        codec.Encode(ref otherWriter, decoded, context);
        Assert.Equal(first.WrittenSpan.ToArray(), second.WrittenSpan.ToArray());
        await fixture.ApplyAsync("update_attributes", decoded);
        Assert.Equal(1.0, fixture.State.Self.Attributes.Value(Identifier.Minecraft("submerged_mining_speed"), -1));
        await AssertFinishesAsync(fixture, protocol, 6);
    }

    [Theory]
    [InlineData(765, "aqua_affinity", 0, 1, 29)]
    [InlineData(765, "aqua_affinity", 1, 0, 6)]
    [InlineData(766, "aqua_affinity", 0, 1, 6)]
    [InlineData(766, "aqua_affinity", 1, 0, 29)]
    [InlineData(765, "efficiency", 0, 1, 6)]
    [InlineData(765, "efficiency", 1, 0, 5)]
    [InlineData(766, "efficiency", 0, 1, 5)]
    [InlineData(766, "efficiency", 1, 0, 6)]
    public async Task DuplicateEnchantmentsUseTheNativeListOrMapValue(int protocol, string enchantment, int firstLevel, int lastLevel, int ticks)
    {
        await using var fixture = new Fixture(protocol);
        bool armor = enchantment == "aqua_affinity";
        fixture.Physics = new Umpk.Physics.PhysicsState { IsUnderWater = armor, OnGround = true };
        if (armor)
            await fixture.HoldAsync("diamond_pickaxe", 0);
        ItemStack stack = new(fixture.State.Registries!.Items[Identifier.Minecraft(armor ? "diamond_helmet" : "diamond_pickaxe")], 1);
        if (protocol == 765)
        {
            NbtCompound tag = EnchantmentTag(protocol, enchantment, armor ? (short)6 : (short)32, firstLevel == 0 ? "first_zero" : "first_one");
            stack = stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
        }
        else
        {
            Assert.True(fixture.State.Registries.Enchantments.TryGet(Identifier.Minecraft(enchantment), out var handle));
            stack = stack.With(DataComponents.Enchantments, new EnchantmentsComponent([new EnchantmentInstance(handle, firstLevel), new EnchantmentInstance(handle, lastLevel)]));
        }
        short slot = armor ? (short)5 : (short)36;
        await SetSlotCheckedAsync(fixture, protocol, slot, stack);
        if (protocol == 765)
        {
            var legacy = Assert.IsType<LegacyNbtComponent>(fixture.State.Inventory.GetSlot(slot).Components.Get(DataComponents.LegacyNbt));
            NbtList entries = Assert.IsType<NbtList>(legacy.Nbt.GetList("Enchantments"));
            Assert.Equal(new[] { firstLevel, lastLevel }, entries.OfType<NbtCompound>().Select(entry => entry.GetInt("lvl")));
        }
        if (protocol == 766)
        {
            var component = Assert.IsType<EnchantmentsComponent>(fixture.State.Inventory.GetSlot(slot).Components.Get(DataComponents.Enchantments));
            Assert.Equal(new[] { firstLevel, lastLevel }, component.Enchantments.Select(instance => instance.Level));
            Assert.All(component.Enchantments, instance => Assert.Equal(Identifier.Minecraft(enchantment), instance.Enchantment.Id));
        }
        await AssertFinishesAsync(fixture, protocol, ticks);
    }

    private static NbtCompound EnchantmentTag(int protocol, string enchantment, short numericId, string scenario)
    {
        bool numeric = protocol < 393;
        var entry = new NbtCompound();
        if (numeric != (scenario == "wrong_id_type"))
            entry.PutInt("id", scenario == "wrapped_numeric_id" ? numericId + 65536 : numericId);
        else
            entry.PutString("id", "minecraft:" + enchantment);
        NbtTag level = scenario switch
        {
            "byte_level" => new NbtByte(1),
            "wide_int_level" => new NbtInt(65536),
            "negative_wide_level" => new NbtInt(-65535),
            "float_level" => new NbtFloat(1.9F),
            "double_level" => new NbtDouble(1.9),
            "long_level" => new NbtLong(1),
            "first_zero" => new NbtShort(0),
            _ => new NbtShort(1),
        };
        entry.Put("lvl", level);
        var list = new NbtList(NbtTagType.Compound);
        list.Add(entry);
        if (scenario is "first_zero" or "first_one")
        {
            var duplicate = (NbtCompound)entry.Copy();
            duplicate.PutShort("lvl", scenario == "first_zero" ? (short)1 : (short)0);
            list.Add(duplicate);
        }
        string key = numeric ? "ench" : "Enchantments";
        var tag = new NbtCompound();
        tag.Put(scenario == "wrong_key" ? numeric ? "Enchantments" : "ench" : key, list);
        return tag;
    }

    private static async Task SetSlotCheckedAsync(Fixture fixture, int protocol, short slot, ItemStack stack)
    {
        string packet = protocol == 47 ? "set_slot" : "container_set_slot";
        var codec = BoundDescriptorCodec.Clientbound(protocol, packet);
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        var first = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(first);
        codec.Encode(ref writer, new ClientboundContainerSetSlotPacket(0, 0, slot, stack), context);
        var decoded = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(first.WrittenSpan, context));
        Assert.Equal((int)slot, decoded.Slot);
        if (stack.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? expectedNbt))
            Assert.Equal(expectedNbt.Nbt, Assert.IsType<LegacyNbtComponent>(decoded.Item.Components.Get(DataComponents.LegacyNbt)).Nbt);
        if (stack.Components.TryGet(DataComponents.Enchantments, out EnchantmentsComponent? expectedEnchantments))
        {
            var decodedEnchantments = Assert.IsType<EnchantmentsComponent>(decoded.Item.Components.Get(DataComponents.Enchantments));
            Assert.Equal(expectedEnchantments.ShowInTooltip, decodedEnchantments.ShowInTooltip);
            Assert.Equal(expectedEnchantments.Enchantments.Select(instance => (instance.Enchantment.Id, instance.Level)), decodedEnchantments.Enchantments.Select(instance => (instance.Enchantment.Id, instance.Level)));
        }
        var second = new ArrayBufferWriter<byte>();
        var otherWriter = new PacketWriter(second);
        codec.Encode(ref otherWriter, decoded, context);
        Assert.Equal(first.WrittenSpan.ToArray(), second.WrittenSpan.ToArray());
        await fixture.ApplyAsync(packet, decoded);
    }

    private static async Task AssertFinishesAsync(Fixture fixture, int protocol, int ticks)
    {
        using var caller = new CancellationTokenSource();
        int stop = protocol == 777 ? 3 : 2;
        Task action = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            for (int tick = 1; tick < ticks; tick++)
            {
                await fixture.TickAsync();
                Assert.DoesNotContain(fixture.ActionsSent, packet => packet.Action == stop);
            }
            await fixture.TickAsync();
            Assert.Equal(ticks == 1 ? new[] { 0 } : new[] { 0, stop }, fixture.ActionsSent.Select(packet => packet.Action));
            await action.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }
}
