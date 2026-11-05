using System.Buffers;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Session = Umpk.Client.Tests.ComponentAdventureMiningSessionTests.Session;

namespace Umpk.Client.Tests;

/// <summary>Constructed compact weapon-component frames through the real connection/session. These are managed wire witnesses; the original protocol770 native21B witness stays separate.</summary>
public sealed class InventoryAttackChargeKnowledgeSessionTests
{
    private const int WeaponComponent = 29;

    [Theory]
    [InlineData(774, 0)]
    [InlineData(777, 0)]
    [InlineData(774, 64)]
    [InlineData(777, 64)]
    public async Task HeldChargeRecoveryDependsOnEligibleUnknownPlayerTicks(int protocol, int unknownTicks)
    {
        await using Session session = await Create(protocol);
        await HoldRequiredSpear(session);
        await QuietTicks(session, 32);
        long beforeLoss = await session.Client.InvokeAsync(client => client.State.SessionTick, session.Token);
        await LossAndFence(session);
        Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(36));
        await QuietTicks(session, unknownTicks);
        ItemStack recovered = Spear(session).WithCount(2);
        await SendKnownAndWait(session, new ClientboundContainerSetSlotPacket(0, 7, 36, recovered),
            packet => packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 7, Slot: 36, Item.Count: 2 });
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36));
        Assert.Equal(recovered, session.Client.State.Inventory.GetSlot(36));
        long afterRecovery = await session.Client.InvokeAsync(client => client.State.SessionTick, session.Token);
        Assert.Equal(unchecked(beforeLoss + unknownTicks), afterRecovery);
        if (unknownTicks == 0)
            await AssertChargedPiercingClick(session);
        else
            await AssertUnavailableClick(session);
    }

    [Fact]
    public async Task FirstDifferentRecoveryUsesPreviousChargeBeforeTheNextPlayerTick()
    {
        await using Session session = await Create(777);
        await HoldRequiredSpear(session);
        await QuietTicks(session, 32);
        long beforeLoss = await session.Client.InvokeAsync(client => client.State.SessionTick, session.Token);
        await LossAndFence(session);
        ItemStack recovered = new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], 2)
            .With(DataComponents.MinimumAttackCharge, new MinimumAttackChargeComponent(1));
        await SendKnownAndWait(session, new ClientboundContainerSetSlotPacket(0, 7, 36, recovered),
            packet => packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 7, Slot: 36, Item.Count: 2 });
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36));
        Assert.Equal(beforeLoss, await session.Client.InvokeAsync(client => client.State.SessionTick, session.Token));
        await AssertOrdinaryClick(session);
    }

    [Fact]
    public async Task UnrelatedSlotLossRetainsTheKnownHeldCharge()
    {
        await using Session session = await Create(777);
        await HoldRequiredSpear(session);
        await QuietTicks(session, 32);
        await LossAndFence(session, lostSlot: 10);
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36));
        Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(10));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            var stab = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(await session.Tick()));
            Assert.Equal(8, stab.Action);
            Assert.Equal(default, stab.Position);
            Assert.Equal((byte)Direction.Down, stab.Direction);
            Assert.Equal(0, stab.Sequence);
            Assert.False(action.IsCompleted);
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
        }
    }

    [Theory]
    [InlineData(774, false)]
    [InlineData(777, false)]
    [InlineData(774, true)]
    [InlineData(777, true)]
    public async Task UnknownCachedPiercingCannotSuppressTheCapturedActiveBlockAbort(int protocol, bool cancelBeforeTick)
    {
        await using Session session = await Create(protocol);
        await session.Hold("stone");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        bool passed = false;
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> initial = await session.Tick();
            var start = Assert.Single(initial.OfType<ServerboundPlayerActionPacket>());
            Assert.Equal(0, start.Action);
            Assert.Equal(Session.Target, start.Position);
            Assert.Equal(2, initial.Count(packet => packet is ServerboundSwingPacket or ServerboundPunchPacket));
            await HoldRequiredSpear(session);
            Assert.Empty(await session.Tick());
            Assert.False(action.IsCompleted);
            await LossAndFence(session);
            Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(36));
            Assert.Equal(Identifier.Minecraft("diamond_spear"), session.Client.State.Inventory.GetSlot(36).Item.Id);

            if (cancelBeforeTick)
            {
                long beforeCancellation = await session.Client.InvokeAsync(client => client.State.SessionTick, session.Token);
                await caller.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
                Assert.True(action.IsCanceled);
                Assert.Equal(beforeCancellation, await session.Client.InvokeAsync(client => client.State.SessionTick, session.Token));
            }
            var abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(await session.Tick()));
            Assert.Equal(protocol == 777 ? 2 : 1, abort.Action);
            Assert.Equal(Session.Target, abort.Position);
            Assert.Equal((byte)Direction.Down, abort.Direction);
            Assert.Equal(0, abort.Sequence);
            if (!cancelBeforeTick)
                await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
            await QuietTicks(session, 2);
            passed = true;
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (!passed || action.IsFaulted) { }
        }
    }

    private static async Task<Session> Create(int protocol)
    {
        Session session = await Session.Create(protocol);
        try
        {
            await session.Mode(GameMode.Survival, instantBuild: false);
            await session.Info(GameMode.Survival);
            await session.Block(Session.Target, "bedrock");
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static ItemStack Spear(Session session)
        => new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_spear")], 1)
            .With(DataComponents.MinimumAttackCharge, new MinimumAttackChargeComponent(1));

    private static Task HoldRequiredSpear(Session session)
        => SendKnownAndWait(session, new ClientboundContainerSetSlotPacket(0, 6, 36, Spear(session)),
            packet => packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 6, Slot: 36 });

    private static async Task AssertChargedPiercingClick(Session session)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> sent = await session.Tick();
            Assert.Equal(session.Protocol == 777 ? 1 : 2, sent.Count);
            var stab = Assert.IsType<ServerboundPlayerActionPacket>(sent[0]);
            Assert.Equal(session.Protocol == 777 ? 8 : 7, stab.Action);
            Assert.Equal(default, stab.Position);
            Assert.Equal((byte)Direction.Down, stab.Direction);
            Assert.Equal(0, stab.Sequence);
            if (session.Protocol != 777)
            {
                var swing = Assert.IsType<ServerboundSwingPacket>(sent[1]);
                Assert.True(swing.HasHand);
                Assert.Equal(0, swing.Hand);
            }
            Assert.False(action.IsCompleted);
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
        }
    }

    private static async Task AssertOrdinaryClick(Session session)
    {
        Assert.Equal(777, session.Protocol);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> sent = await session.Tick();
            Assert.Collection(sent,
                packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), session.Protocol, 0),
                packet => Assert.IsType<ServerboundPunchPacket>(packet),
                packet => Assert.IsType<ServerboundPunchPacket>(packet));
            Assert.False(action.IsCompleted);
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
        }
    }

    private static async Task AssertUnavailableClick(Session session)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Empty(await session.Tick());
            await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
            await QuietTicks(session, 2);
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            if (!action.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
        }
    }

    private static async Task QuietTicks(Session session, int count)
    {
        for (int tick = 0; tick < count; tick++)
            Assert.Empty(await session.Tick());
    }

    private static async Task LossAndFence(Session session, short lostSlot = 36)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, "container_set_slot");
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        writer.WriteVarInt(0);
        writer.WriteVarInt(7);
        writer.WriteShort(lostSlot);
        writer.WriteVarInt(1);
        writer.WriteVarInt(Spear(session).Item.NetworkId);
        writer.WriteVarInt(1);
        writer.WriteVarInt(0);
        writer.WriteVarInt(WeaponComponent);
        writer.WriteVarInt(1);
        writer.WriteFloat(0);
        byte[] body = bytes.WrittenSpan.ToArray();
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var fault = Assert.Throws<UnmodeledItemComponentException>(() => codec.Decode(body, context));
        Assert.Equal(WeaponComponent, fault.ComponentWireId);
        Assert.Equal(Identifier.Minecraft("weapon"), fault.ComponentId);
        int observed = 0;
        int decodedLoss = 0;
        void Observe(PacketObservation frame)
        {
            if (frame.Phase == ProtocolPhase.Play && frame.Flow == PacketFlow.Clientbound
                && frame.WireId == codec.WireId && frame.RawPayload.SequenceEqual(body))
                Interlocked.Increment(ref observed);
        }
        using IDisposable subscription = session.Client.Events.Subscribe<PacketReceived>(received =>
        {
            if (received.Packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 7 } slot && slot.Slot == lostSlot)
                Interlocked.Increment(ref decodedLoss);
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await session.Server.SendFrameAsync(codec.WireId, body, session.Token);
            ItemStack fence = new(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], 2);
            await SendKnownAndWait(session, new ClientboundContainerSetSlotPacket(0, 8, 9, fence),
                packet => packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 8, Slot: 9, Item.Count: 2 });
            Assert.Equal(1, Volatile.Read(ref observed));
            Assert.Equal(0, Volatile.Read(ref decodedLoss));
            Assert.Equal(fence, session.Client.State.Inventory.GetSlot(9));
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static async Task SendKnownAndWait(Session session, IPacket packet, Func<object?, bool> matches)
    {
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = session.Client.Events.Subscribe<PacketReceived>(received =>
        {
            if (matches(received.Packet))
                applied.TrySetResult();
        });
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] body = Encode(codec, packet, context);
        object decoded = codec.Decode(body, context);
        Assert.Equal(body, Encode(codec, decoded, context));
        await session.Server.SendFrameAsync(codec.WireId, body, session.Token);
        await applied.Task.WaitAsync(session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
