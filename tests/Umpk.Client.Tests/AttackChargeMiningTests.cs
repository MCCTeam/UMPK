using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Fixture = Umpk.Client.Tests.TickMiningLifecycleTests.Fixture;

namespace Umpk.Client.Tests;

public sealed class AttackChargeMiningTests
{
    [Theory]
    [InlineData(774, false)]
    [InlineData(775, false)]
    [InlineData(776, false)]
    [InlineData(777, false)]
    [InlineData(774, true)]
    [InlineData(775, true)]
    [InlineData(776, true)]
    [InlineData(777, true)]
    public async Task ChargeComponentChangesRestartOnlyWhenNativeStacksDiffer(int protocol, bool nan)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        fixture.Hold("stone");
        ItemStack held = fixture.State.Inventory.PlayerSlots[36];
        fixture.State.Inventory.SetSlot(0, 36, held.With(DataComponents.MinimumAttackCharge,
            new MinimumAttackChargeComponent(BitConverter.Int32BitsToSingle(nan ? 0x7FC00001 : 0))));
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            fixture.State.Inventory.SetSlot(0, 36, held.With(DataComponents.MinimumAttackCharge,
                new MinimumAttackChargeComponent(BitConverter.Int32BitsToSingle(nan ? 0x7FC00002 : int.MinValue))));
            await fixture.TickAsync();

            int abort = protocol == 777 ? 2 : 1;
            Assert.Equal(nan ? new[] { 0 } : new[] { 0, abort, 0 }, fixture.Packets.OfType<ServerboundPlayerActionPacket>().Select(packet => packet.Action));
            Assert.Equal(3, fixture.Packets.Count(packet => packet is ServerboundPunchPacket or ServerboundSwingPacket));
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }
}
