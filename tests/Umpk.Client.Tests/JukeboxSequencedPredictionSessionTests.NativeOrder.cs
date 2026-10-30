using Umpk.Client.Actions;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed partial class JukeboxSequencedPredictionSessionTests
{
    [Theory]
    [InlineData(759, false)]
    [InlineData(761, false)]
    [InlineData(759, true)]
    [InlineData(761, true)]
    public async Task NativePendingSlotOrderSelectsFirstCollidingCaptureAcrossGrowth(int protocol, bool grow)
    {
        await using Session session = await Session.Create(protocol, "jukebox", physics: true);
        BlockPos first = new(1, 64, 1);
        BlockPos second = new(2, 64, 1);
        int filled = Filled(session);
        await SetState(session, first, filled);
        await SetState(session, second, filled);
        Vec3d firstCapture = new(8.5, 64, 8.5);
        Vec3d secondCapture = new(9.5, 64, 9.5);
        await MoveSelf(session, firstCapture);
        await using PendingUse firstUse = await BeginUse(session, protocol, 1, BlockUseResult.Success, first);
        await MoveSelf(session, secondCapture);
        await using PendingUse secondUse = await BeginUse(session, protocol, 2, BlockUseResult.Success, second);
        var additional = new List<PendingUse>();
        try
        {
            if (grow)
            {
                // These distinct native 64-slot homes avoid both colliding cells and force the original 24-entry growth threshold.
                BlockPos[] positions = [new(0, 64, 4), new(0, 64, 8), new(0, 64, 9), new(0, 64, 11), new(0, 64, 12), new(0, 64, 14), new(1, 64, 6), new(1, 64, 7), new(1, 64, 10), new(1, 64, 13), new(1, 64, 15), new(2, 64, 8), new(2, 64, 9), new(2, 64, 11), new(3, 64, 7), new(4, 64, 8), new(4, 64, 9), new(5, 64, 14), new(6, 64, 8), new(7, 64, 7), new(7, 64, 10), new(7, 64, 14), new(7, 64, 15)];
                for (int index = 0; index < positions.Length; index++)
                {
                    await SetState(session, positions[index], filled);
                    additional.Add(await BeginUse(session, protocol, index + 3, BlockUseResult.Success, positions[index]));
                }
            }
            await MoveSelf(session, new(2, 64, 1.5));
            Assert.Equal(BlockUseResult.Pass, await PredictAt(session, first));
            Assert.Equal(BlockUseResult.Pass, await PredictAt(session, second));
            await SendAck(session, protocol, grow ? 25 : 2);
            await firstUse.Action.WaitAsync(session.Token);
            await secondUse.Action.WaitAsync(session.Token);
            foreach (PendingUse use in additional)
                await use.Action.WaitAsync(session.Token);
            Assert.Equal(firstCapture, session.Client.State.Self.Position);
            Assert.Equal(firstCapture, (await ReadBody(session)).Position);
            Assert.Equal(filled, session.Client.State.World.GetBlock(first).StateId);
            Assert.Equal(filled, session.Client.State.World.GetBlock(second).StateId);
            Assert.Equal(BlockUseResult.Success, await PredictAt(session, first));
            Assert.Equal(BlockUseResult.Success, await PredictAt(session, second));
            Assert.Empty(await session.Barrier());
        }
        finally
        {
            foreach (PendingUse use in additional)
                await use.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task NativeZeroPackedPositionPrecedesOrdinaryPendingSlots(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox", physics: true);
        BlockPos zero = new(0, 0, 0);
        BlockPos ordinary = new(1, 0, 0);
        int filled = Filled(session);
        await SetState(session, zero, filled);
        await SetState(session, ordinary, filled);
        Vec3d zeroCapture = new(8.5, 0, 8.5);
        await MoveSelf(session, zeroCapture);
        await using PendingUse zeroUse = await BeginUse(session, protocol, 1, BlockUseResult.Success, zero);
        await MoveSelf(session, new(9.5, 0, 9.5));
        await using PendingUse ordinaryUse = await BeginUse(session, protocol, 2, BlockUseResult.Success, ordinary);
        await MoveSelf(session, new(1, 0, .5));
        await SendAck(session, protocol, 2);
        await zeroUse.Action.WaitAsync(session.Token);
        await ordinaryUse.Action.WaitAsync(session.Token);
        Assert.Equal(zeroCapture, session.Client.State.Self.Position);
        Assert.Equal(zeroCapture, (await ReadBody(session)).Position);
        Assert.Equal(filled, session.Client.State.World.GetBlock(zero).StateId);
        Assert.Equal(filled, session.Client.State.World.GetBlock(ordinary).StateId);
        Assert.Equal(BlockUseResult.Success, await PredictAt(session, zero));
        Assert.Equal(BlockUseResult.Success, await PredictAt(session, ordinary));
        Assert.Empty(await session.Barrier());
    }

    [Theory]
    [InlineData(759)]
    [InlineData(761)]
    public async Task NativeWrappedClusterRetainsUnacknowledgedMutationsAcrossRemovalAndReinsertion(int protocol)
    {
        await using Session session = await Session.Create(protocol, "jukebox");
        // All three native packed keys start at slot31, so insertion crosses the end of the original 32-slot array.
        BlockPos first = new(2, 64, 6);
        BlockPos second = new(2, 64, 13);
        BlockPos third = new(3, 64, 4);
        int filled = Filled(session);
        await SetState(session, first, filled);
        await SetState(session, second, filled);
        await SetState(session, third, filled);
        await using PendingUse firstUse = await BeginUse(session, protocol, 1, BlockUseResult.Success, first);
        await using PendingUse secondUse = await BeginUse(session, protocol, 2, BlockUseResult.Success, second);
        await using PendingUse thirdUse = await BeginUse(session, protocol, 3, BlockUseResult.Success, third);
        await SendAck(session, protocol, 1);
        await firstUse.Action.WaitAsync(session.Token);
        Assert.False(secondUse.Action.IsCompleted);
        Assert.False(thirdUse.Action.IsCompleted);
        Assert.Equal(BlockUseResult.Success, await PredictAt(session, first));
        Assert.Equal(BlockUseResult.Pass, await PredictAt(session, second));
        Assert.Equal(BlockUseResult.Pass, await PredictAt(session, third));
        await using PendingUse reinserted = await BeginUse(session, protocol, 4, BlockUseResult.Success, first);
        await SendAck(session, protocol, 3);
        await secondUse.Action.WaitAsync(session.Token);
        await thirdUse.Action.WaitAsync(session.Token);
        Assert.False(reinserted.Action.IsCompleted);
        Assert.Equal(BlockUseResult.Pass, await PredictAt(session, first));
        Assert.Equal(BlockUseResult.Success, await PredictAt(session, second));
        Assert.Equal(BlockUseResult.Success, await PredictAt(session, third));
        await SendAck(session, protocol, 4);
        await reinserted.Action.WaitAsync(session.Token);
        Assert.Equal(BlockUseResult.Success, await PredictAt(session, first));
        Assert.Equal(filled, session.Client.State.World.GetBlock(first).StateId);
        Assert.Equal(filled, session.Client.State.World.GetBlock(second).StateId);
        Assert.Equal(filled, session.Client.State.World.GetBlock(third).StateId);
        Assert.Empty(await session.Barrier());
    }
}
