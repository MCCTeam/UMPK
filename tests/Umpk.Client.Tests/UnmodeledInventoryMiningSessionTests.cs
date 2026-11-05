using System.Buffers;
using System.Security.Cryptography;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = ComponentAdventureMiningSessionTests.Session;

public sealed class UnmodeledInventoryMiningSessionTests
{
    private const int Protocol = 770;
    private const int NativeStateId = 7;
    private const int FenceStateId = 8;
    private const int FenceSlot = 9;

    // Original 1.21.5 STREAM_CODEC output, accepted exact10 row 770-exact-declared-unmodeled-weapon: window0/state7/slot36/stone1/can_break(exact weapon defaults). These bytes are sent unchanged.
    private const string NativeSetSlotBase64 = "AAcAJAEBAQAMAQAAAAEaAQAAAAAA";
    private const string NativeSetSlotSha256 = "578cc93204fbdc086a8c783fcc0442525635ef4cc4388311ed14bda69ff5e165";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DroppedNewerHeldStackEndsUnavailableWithoutGuessingAnotherAttack(bool started, bool dropNewerHeldStack)
    {
        await using Session session = await Session.Create(Protocol);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        await session.Block(Session.Target, "bedrock");
        await session.Hold("diamond_pickaxe");
        Assert.Equal("minecraft:diamond_pickaxe", session.Client.State.Inventory.GetSlot(36).Item.Id.ToString());
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task? action = null;
        try
        {
            if (started)
            {
                action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
                await session.Client.InvokeAsync(_ => true, session.Token);
                AssertStartAndSwings(await session.Tick());
                Assert.False(action.IsCompleted);
            }

            await DeliverNativeDropAndUnrelatedFence(session, dropNewerHeldStack);
            action ??= session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> sent = await session.Tick();
            if (dropNewerHeldStack)
            {
                if (started)
                {
                    ServerboundPlayerActionPacket abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(sent));
                    Assert.Equal(1, abort.Action);
                    Assert.Equal(Session.Target, abort.Position);
                    Assert.Equal((byte)Direction.Down, abort.Direction);
                    Assert.Equal(0, abort.Sequence);
                }
                else
                    Assert.Empty(sent);

                // This is an unavailable public request, independently of the private spelling of the new freshness owner.
                await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
                Assert.Empty(await session.Tick());
                Assert.Empty(await session.Tick());
            }
            else
            {
                if (started)
                    AssertSwing(Assert.Single(sent));
                else
                    AssertStartAndSwings(sent);
                Assert.False(action.IsCompleted);
                AssertSwing(Assert.Single(await session.Tick()));
            }
            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally
        {
            await caller.CancelAsync();
            if (action is not null)
            {
                await session.Tick();
                try { await action.WaitAsync(session.Token); }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) when (dropNewerHeldStack) { }
            }
        }
    }

    private static async Task DeliverNativeDropAndUnrelatedFence(Session session, bool sendNativeDrop)
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion version));
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        var entry = Assert.Single(registry.Packets, entry => entry.Type.Id == Identifier.Minecraft("container_set_slot"));
        Assert.True(registry.TryGetInbound(entry.WireId, out BoundPacketCodec codec));
        byte[] original = Convert.FromBase64String(NativeSetSlotBase64);
        Assert.Equal(21, original.Length);
        Assert.Equal(NativeSetSlotSha256, Convert.ToHexStringLower(SHA256.HashData(original)));
        var appliedFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int observedNativeFrames = 0;
        int decodedNativeUpdates = 0;
        void Observe(PacketObservation observation)
        {
            if (observation.Flow == PacketFlow.Clientbound && observation.Phase == ProtocolPhase.Play
                && observation.WireId == entry.WireId && observation.RawPayload.SequenceEqual(original))
                Interlocked.Increment(ref observedNativeFrames);
        }
        using IDisposable received = session.Client.Events.Subscribe<PacketReceived>(packet =>
        {
            if (packet.Packet is not ClientboundContainerSetSlotPacket slot || slot.ContainerId != 0)
                return;
            if (slot.Slot == 36 && slot.StateId == NativeStateId)
                Interlocked.Increment(ref decodedNativeUpdates);
            if (slot.Slot == FenceSlot && slot.StateId == FenceStateId && slot.Item.Count == 2)
                appliedFence.TrySetResult();
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            if (sendNativeDrop)
                await session.Server.SendFrameAsync(entry.WireId, original, session.Token);
            ItemStack fenceItem = new(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], 2);
            var fence = new ClientboundContainerSetSlotPacket(0, FenceStateId, FenceSlot, fenceItem);
            var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, fence, context);
            var decoded = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(buffer.WrittenSpan, context));
            Assert.Equal(FenceStateId, decoded.StateId);
            Assert.Equal(FenceSlot, decoded.Slot);
            Assert.Equal(2, decoded.Item.Count);
            var roundTrip = new ArrayBufferWriter<byte>();
            var roundTripWriter = new PacketWriter(roundTrip);
            codec.Encode(ref roundTripWriter, decoded, context);
            Assert.Equal(buffer.WrittenSpan.ToArray(), roundTrip.WrittenSpan.ToArray());
            await session.Server.SendFrameAsync(entry.WireId, buffer.WrittenSpan.ToArray(), session.Token);
            await appliedFence.Task.WaitAsync(session.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Equal(sendNativeDrop ? 1 : 0, Volatile.Read(ref observedNativeFrames));
            Assert.Equal(0, Volatile.Read(ref decodedNativeUpdates));
            Assert.Equal(2, session.Client.State.Inventory.GetSlot(FenceSlot).Count);
            Assert.Equal("minecraft:stone", session.Client.State.Inventory.GetSlot(FenceSlot).Item.Id.ToString());
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static void AssertStartAndSwings(List<IPacket> packets)
        => Assert.Collection(packets,
            packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), Protocol, 0),
            AssertSwing,
            AssertSwing);

    private static void AssertSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.True(swing.HasHand);
        Assert.Equal(0, swing.Hand);
    }
}
