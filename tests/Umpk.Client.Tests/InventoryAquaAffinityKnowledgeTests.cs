using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Fixture = Umpk.Client.Tests.MiningItemIdentityRuntimeTests.Fixture;

namespace Umpk.Client.Tests;

/// <summary>Constructed armor loss through the existing progress fixture's writable physics snapshot. No helper or native input is added.</summary>
public sealed class InventoryAquaAffinityKnowledgeTests
{
    [Fact]
    public async Task KnownAquaAffinityProvesUnderwaterPermissionDespiteAnEarlierUnknownArmorSlot()
    {
        await using var fixture = new Fixture(766);
        fixture.Physics = new Umpk.Physics.PhysicsState { IsUnderWater = true, OnGround = true };
        await fixture.HoldAsync("diamond_pickaxe", 0);
        Assert.True(fixture.State.Registries!.Enchantments.TryGet(Identifier.Minecraft("aqua_affinity"), out var enchantment));
        ItemStack armor = new ItemStack(fixture.State.Registries.Items[Identifier.Minecraft("diamond_helmet")], 1)
            .With(DataComponents.Enchantments, new EnchantmentsComponent([new EnchantmentInstance(enchantment, 1)]));
        await fixture.SetSlotAsync(8, armor);
        Assert.True(fixture.State.Inventory.GetSlot(8).TryGetEnchantmentLevel(Identifier.Minecraft("aqua_affinity"),
            Umpk.Data.Java.JavaGameData.LegacyItemBridgeEra, Umpk.Data.Java.JavaGameData.LegacyItemBridgeSource, out int level));
        Assert.Equal(1, level);
        fixture.State.Inventory.MarkSlotUnavailable(0, 5);
        Assert.False(fixture.State.Inventory.IsPlayerSlotAvailable(5));
        Assert.True(fixture.State.Inventory.IsPlayerSlotAvailable(8));
        Assert.True(fixture.State.Inventory.IsPlayerSlotAvailable(36));
        using var caller = new CancellationTokenSource();
        Task action = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            Assert.Equal(0, Assert.Single(fixture.ActionsSent).Action);
            Assert.Equal(2, fixture.Sent.OfType<ServerboundSwingPacket>().Count());
            Assert.Equal(3, fixture.Sent.Count);
            Assert.False(action.IsCompleted);
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LostArmorKnowledgeIsRequiredOnlyWhenLegacyUnderwaterMiningQueriesIt(bool underwater)
    {
        await using var fixture = new Fixture(766);
        fixture.Physics = new Umpk.Physics.PhysicsState { IsUnderWater = underwater, OnGround = true };
        await fixture.HoldAsync("diamond_pickaxe", 0);
        Assert.True(fixture.State.Registries!.Enchantments.TryGet(Identifier.Minecraft("aqua_affinity"), out var enchantment));
        ItemStack armor = new ItemStack(fixture.State.Registries.Items[Identifier.Minecraft("diamond_helmet")], 1)
            .With(DataComponents.Enchantments, new EnchantmentsComponent([new EnchantmentInstance(enchantment, 1)]));
        await fixture.SetSlotAsync(8, armor);
        Assert.True(fixture.State.Inventory.GetSlot(8).TryGetEnchantmentLevel(Identifier.Minecraft("aqua_affinity"),
            Umpk.Data.Java.JavaGameData.LegacyItemBridgeEra, Umpk.Data.Java.JavaGameData.LegacyItemBridgeSource, out int level));
        Assert.Equal(1, level);
        fixture.State.Inventory.MarkSlotUnavailable(0, 8);
        Assert.False(fixture.State.Inventory.IsPlayerSlotAvailable(8));
        Assert.True(fixture.State.Inventory.IsPlayerSlotAvailable(36));
        using var caller = new CancellationTokenSource();
        Task action = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            if (underwater)
            {
                Assert.Empty(fixture.Sent);
                await Assert.ThrowsAnyAsync<InvalidOperationException>(() => action.WaitAsync(TimeSpan.FromSeconds(5)));
                await fixture.TickAsync();
                Assert.Empty(fixture.Sent);
            }
            else
            {
                Assert.Equal(0, Assert.Single(fixture.ActionsSent).Action);
                Assert.Equal(2, fixture.Sent.OfType<ServerboundSwingPacket>().Count());
                Assert.Equal(3, fixture.Sent.Count);
                Assert.False(action.IsCompleted);
            }
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }
}
