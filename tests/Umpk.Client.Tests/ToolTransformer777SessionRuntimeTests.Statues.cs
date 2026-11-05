using Umpk.Client.Actions;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed partial class ToolTransformer777SessionRuntimeTests
{
    [Theory]
    [InlineData("exposed_copper_golem_statue", "copper_golem_statue", 33150, 33118, "standing", "north", "false")]
    [InlineData("weathered_copper_golem_statue", "exposed_copper_golem_statue", 33191, 33159, "sitting", "south", "true")]
    [InlineData("oxidized_copper_golem_statue", "weathered_copper_golem_statue", 33234, 33202, "running", "west", "false")]
    [InlineData("waxed_copper_golem_statue", "copper_golem_statue", 33275, 33147, "star", "east", "true")]
    [InlineData("waxed_exposed_copper_golem_statue", "exposed_copper_golem_statue", 33284, 33156, "standing", "east", "false")]
    [InlineData("waxed_weathered_copper_golem_statue", "weathered_copper_golem_statue", 33321, 33193, "sitting", "west", "true")]
    [InlineData("waxed_oxidized_copper_golem_statue", "oxidized_copper_golem_statue", 33360, 33232, "running", "south", "false")]
    public async Task NativeStatueAxeMappingsCopyAllPropertiesBeforeUseAndRetainAckShadow(string input, string target, int original, int transformed, string pose, string facing, string waterlogged)
    {
        await using Session session = await LocalReady("axe", input);
        await LocalState(session, Session.Target, original);
        await Secondary(session);
        ItemStack stack = session.Client.State.Inventory.GetSlot(36);
        List<(Task Action, int Sequence)> pending = [];
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        try
        {
            Assert.Equal(original, await Current(session));
            Assert.Equal(BlockUseResult.Success, await LocalResult(session));
            int sequence = await LocalStart(session, pending, transformed, caller: caller.Token);
            BlockState projected = await session.Client.InvokeAsync(client => client.State.JukeboxPrediction.GetBlock(client.State.World, Session.Target), session.Token);
            Assert.Equal(Identifier.Minecraft(target), projected.Block.Id);
            Assert.True(projected.TryGetProperty("copper_golem_pose", out string actualPose));
            Assert.True(projected.TryGetProperty("facing", out string actualFacing));
            Assert.True(projected.TryGetProperty("waterlogged", out string actualWaterlogged));
            Assert.Equal((pose, facing, waterlogged), (actualPose, actualFacing, actualWaterlogged));
            // These assertions preserve the received-state contract; block-entity continuity and local durability are not proved here.
            Assert.Equal(original, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(stack, session.Client.State.Inventory.GetSlot(36));
            await LocalAck(session, sequence);
            await pending[0].Action.WaitAsync(session.Token);
            Assert.True(pending[0].Action.IsCompletedSuccessfully);
            Assert.Equal(original, await Current(session));
        }
        finally
        {
            await caller.CancelAsync();
            foreach (var owned in pending)
                if (!owned.Action.IsCompletedSuccessfully)
                    try { await owned.Action.WaitAsync(session.Token); }
                    catch (OperationCanceledException) { }
        }
    }
}
