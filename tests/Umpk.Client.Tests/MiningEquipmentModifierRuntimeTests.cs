using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Fixture = Umpk.Client.Tests.MiningItemIdentityRuntimeTests.Fixture;

namespace Umpk.Client.Tests;

public sealed class MiningEquipmentModifierRuntimeTests
{
    public static IEnumerable<object[]> ArmorSlots()
    {
        foreach (int protocol in new[] { 47, 340, 393, 765, 766 })
            foreach (short slot in new short[] { 5, 6, 7, 8 })
                yield return [protocol, slot];
    }

    [Theory]
    [MemberData(nameof(ArmorSlots))]
    public async Task WireAquaAffinityOnAnyArmorSlotRemovesThePreAttributeWaterPenalty(int protocol, short slot)
    {
        await using var fixture = new Fixture(protocol);
        fixture.Physics = new Umpk.Physics.PhysicsState { IsUnderWater = true, OnGround = true };
        await fixture.HoldAsync("diamond_pickaxe", 0);
        await fixture.SetSlotAsync(slot, AquaAffinity(fixture.State.Registries!, protocol));
        await AssertFinishesOnTickAsync(fixture, protocol, 6);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(765)]
    [InlineData(766)]
    public async Task UnequippedAquaAffinityDoesNotRemoveTheWaterPenalty(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.Physics = new Umpk.Physics.PhysicsState { IsUnderWater = true, OnGround = true };
        await fixture.HoldAsync("diamond_pickaxe", 0);
        await fixture.SetSlotAsync(9, AquaAffinity(fixture.State.Registries!, protocol));
        await AssertFinishesOnTickAsync(fixture, protocol, 29);
    }

    [Theory]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(777)]
    public async Task WireMiningEfficiencyUsesThePlayerAttributeAcrossTheRename(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        await fixture.HoldAsync("diamond_pickaxe", 0);
        string name = protocol < 768 ? "player.mining_efficiency" : "mining_efficiency";
        Assert.True(fixture.State.Registries!.Attributes.TryGetNetworkId(Identifier.Minecraft(name), out int id));
        await fixture.ApplyAsync("update_attributes", new ClientboundUpdateAttributesPacket(fixture.State.Self.EntityId, [new AttributeSnapshot(null, id, 26, [])]));
        Assert.Equal(26, fixture.State.Self.Attributes.Value(Identifier.Minecraft("mining_efficiency"), -1));
        await AssertFinishesOnTickAsync(fixture, protocol, 2);
    }

    [Theory]
    [InlineData(47, 6)]
    [InlineData(340, 6)]
    [InlineData(393, 6)]
    [InlineData(765, 6)]
    [InlineData(766, 6)]
    [InlineData(767, 5)]
    [InlineData(768, 5)]
    [InlineData(777, 5)]
    public async Task UnderwaterClayRetainsTheNativeDivisionToAttributeBoundary(int protocol, int ticks)
    {
        await using var fixture = new Fixture(protocol, "clay");
        fixture.Physics = new Umpk.Physics.PhysicsState { IsUnderWater = true, OnGround = true };
        if (protocol >= 767)
        {
            await fixture.HoldAsync("diamond_shovel", 0);
            string name = protocol < 768 ? "player.mining_efficiency" : "mining_efficiency";
            Assert.True(fixture.State.Registries!.Attributes.TryGetNetworkId(Identifier.Minecraft(name), out int id));
            await fixture.ApplyAsync("update_attributes", new ClientboundUpdateAttributesPacket(fixture.State.Self.EntityId, [new AttributeSnapshot(null, id, 10, [])]));
        }
        else
            await fixture.SetSlotAsync(36, EnchantedItem(fixture.State.Registries!, protocol, "diamond_shovel", "efficiency", 32, 3));
        // Native pre-1.21 divides 18.0F by 5.0F; 1.21 multiplies by the 0.2F attribute. At clay's 0.6F hardness, float accumulation crosses one on different ticks.
        await AssertFinishesOnTickAsync(fixture, protocol, ticks);
    }

    private static ItemStack AquaAffinity(RegistryAccess registries, int protocol)
        => EnchantedItem(registries, protocol, "diamond_helmet", "aqua_affinity", 6, 1);

    private static ItemStack EnchantedItem(RegistryAccess registries, int protocol, string itemName, string enchantmentName, short legacyEnchantmentId, short level)
    {
        Assert.True(registries.Items.TryGet(Identifier.Minecraft(itemName), out RegistryEntry<ItemDefinition> item));
        ItemStack stack = new(item, 1);
        if (protocol >= 766)
        {
            Assert.True(registries.Enchantments.TryGet(Identifier.Minecraft(enchantmentName), out RegistryEntry<EnchantmentDefinition> enchantment));
            return stack.With(DataComponents.Enchantments, new EnchantmentsComponent([new EnchantmentInstance(enchantment, level)]));
        }
        var entry = new NbtCompound();
        if (protocol < 393)
            entry.PutShort("id", legacyEnchantmentId);
        else
            entry.PutString("id", "minecraft:" + enchantmentName);
        entry.PutShort("lvl", level);
        var list = new NbtList(NbtTagType.Compound);
        list.Add(entry);
        var tag = new NbtCompound();
        tag.Put(protocol < 393 ? "ench" : "Enchantments", list);
        return stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
    }

    private static async Task AssertFinishesOnTickAsync(Fixture fixture, int protocol, int ticks)
    {
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            for (int tick = 1; tick < ticks; tick++)
                await fixture.TickAsync();
            Assert.DoesNotContain(fixture.ActionsSent, packet => packet.Action == (protocol == 777 ? 3 : 2));
            await fixture.TickAsync();
            Assert.Equal(new[] { 0, protocol == 777 ? 3 : 2 }, fixture.ActionsSent.Select(packet => packet.Action));
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            caller.Cancel();
            try
            {
                await mining.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
                // A failed progress assertion can leave the attack pending, while a premature STOP can finish it before cleanup.
            }
        }
    }
}
