using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

using static Umpk.Client.Tests.ComponentAdventureMiningSessionTests;

namespace Umpk.Client.Tests;

public sealed class ComponentMiningIdentitySessionTests
{
    [Theory]
    [InlineData(766, false)]
    [InlineData(767, false)]
    [InlineData(768, false)]
    [InlineData(769, false)]
    [InlineData(766, true)]
    [InlineData(767, true)]
    [InlineData(768, true)]
    [InlineData(769, true)]
    public async Task EquivalentWirePredicateRetainsNativeTargetProgressAfterOptionalDeniedRestart(int protocol, bool deniedRestart)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.HoldStonePermission();
        AdventureModePredicateComponent original = session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak)!;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        bool passed = false;
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> start = await session.Tick();
            AssertAction(Assert.Single(start.OfType<ServerboundPlayerActionPacket>()), protocol, 0);
            Assert.Equal(2, start.OfType<ServerboundSwingPacket>().Count());
            if (deniedRestart)
            {
                await session.HoldPermission("nonmatch");
                Assert.Empty(await session.Tick());
                Assert.False(action.IsCompleted);
            }
            await session.HoldStonePermission();
            Assert.NotSame(original, session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak));
            List<IPacket> continued = await session.Tick();
            Assert.Empty(continued.OfType<ServerboundPlayerActionPacket>());
            Assert.Single(continued.OfType<ServerboundSwingPacket>());
            ServerboundPlayerActionPacket? stop = null;
            for (int tick = 1; tick <= 4; tick++)
            {
                List<IPacket> sent = await session.Tick();
                Assert.Single(sent.OfType<ServerboundSwingPacket>());
                if (tick < 4)
                    Assert.Empty(sent.OfType<ServerboundPlayerActionPacket>());
                else
                    stop = Assert.Single(sent.OfType<ServerboundPlayerActionPacket>());
            }
            Assert.NotNull(stop);
            AssertAction(stop, protocol, 2);
            await session.Complete(action, stop);
            passed = true;
        }
        finally
        {
            await caller.CancelAsync();
            try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (!passed) { }
        }
    }

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    public async Task ChangedTooltipStillChangesNativeDestroyTargetIdentity(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await session.HoldStonePermission();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            AssertAction(Assert.Single((await session.Tick()).OfType<ServerboundPlayerActionPacket>()), protocol, 0);
            await session.HoldPermission("stone", showInTooltip: false);
            List<IPacket> changed = await session.Tick();
            ServerboundPlayerActionPacket[] actions = changed.OfType<ServerboundPlayerActionPacket>().ToArray();
            Assert.Equal(new[] { 1, 0 }, actions.Select(packet => packet.Action));
            foreach (ServerboundPlayerActionPacket packet in actions)
                AssertAction(packet, protocol, packet.Action);
            Assert.Single(changed.OfType<ServerboundSwingPacket>());
        }
        finally
        {
            await caller.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
        }
    }

}
