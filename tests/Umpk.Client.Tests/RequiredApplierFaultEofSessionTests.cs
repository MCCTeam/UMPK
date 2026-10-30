using System.Buffers;
using System.Security.Cryptography;
using Umpk.Client.Events;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Umpk.Game.World;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

/// <summary>Protocol770 framed session closure with a real captured peer. The required-applier-fault row must observe EOF before public disconnect, disposal, or another manually supplied player tick.</summary>
public sealed class RequiredApplierFaultEofSessionTests
{
    private const int Protocol = 770;
    private const int SelectedSlot = 5;
    private const int LostSlot = 36;
    private const int SelectedMenuSlot = 36 + SelectedSlot;
    private const string NativeLossBase64 = "AAcAJAEBAQAMAQAAAAEaAQAAAAAA";
    private const string NativeLossSha256 = "578cc93204fbdc086a8c783fcc0442525635ef4cc4388311ed14bda69ff5e165";
    private static readonly CommonPlayerSpawnInfo Spawn = new(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);

    [Fact]
    public Task RequiredRespawnApplierFaultClosesCapturedPeerBeforePublicDisconnect()
        => AssertClosureAsync(EndTrigger.RequiredApplierFault);

    [Fact]
    public Task PeerClosePublishesSocketEofAndPreservesCachedInventory()
        => AssertClosureAsync(EndTrigger.PeerClose);

    [Fact]
    public Task LocalDisconnectClosesCapturedPeerAndPreservesCachedInventory()
        => AssertClosureAsync(EndTrigger.LocalDisconnect);

    private static async Task AssertClosureAsync(EndTrigger trigger)
    {
        await using Session session = await Session.Create(Protocol, "stone");
        FakeJavaServer peer = session.Server;
        InventorySnapshot before = await SeedClosedInventoryAndLossAsync(session);
        long tickBefore = session.Client.State.SessionTick;
        var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = new TaskCompletionSource<ClientStatusChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        int disconnectEvents = 0;
        int terminalTransitions = 0;
        int respawns = 0;
        using IDisposable ended = session.Client.Events.Subscribe<Disconnected>(message =>
        {
            Interlocked.Increment(ref disconnectEvents);
            disconnected.TrySetResult(message.Info);
        });
        using IDisposable respawned = session.Client.Events.Subscribe<Respawned>(_ => Interlocked.Increment(ref respawns));
        void ObserveStatus(object? sender, ClientStatusChangedEventArgs change)
        {
            if (change.Current == ClientStatus.Disconnected)
            {
                Interlocked.Increment(ref terminalTransitions);
                terminal.TrySetResult(change);
            }
        }
        session.Client.StatusChanged += ObserveStatus;
        // This reader owns all remaining serverbound frames until the captured peer actually closes. It is awaited again during cleanup, including after a primary assertion failure.
        Task<ConnectionClosedException> peerEof = ObservePeerEndAsync(peer, session.Token);
        DisconnectInfo? observedDisconnect = null;
        Exception? primaryFailure = null;
        try
        {
            switch (trigger)
            {
                case EndTrigger.RequiredApplierFault:
                    await SendInvalidRequiredRespawnAsync(session, peer);
                    break;
                case EndTrigger.PeerClose:
                    await peer.ServerConnection.CloseAsync(CloseReason.Local, session.Token);
                    break;
                case EndTrigger.LocalDisconnect:
                    await session.Client.DisconnectAsync(session.Token);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(trigger));
            }

            observedDisconnect = await disconnected.Task.WaitAsync(session.Token);
            ClientStatusChangedEventArgs status = await terminal.Task.WaitAsync(session.Token);
            Assert.Same(observedDisconnect, status.Disconnect);
            Assert.Same(observedDisconnect, session.Client.LastDisconnect);
            Assert.Equal(ClientStatus.Disconnected, session.Client.Status);
            Assert.Equal(0, Volatile.Read(ref respawns));
            AssertCachedSnapshot(before, Capture(session.Client));
            Assert.Equal(tickBefore, session.Client.State.SessionTick);
            switch (trigger)
            {
                case EndTrigger.RequiredApplierFault:
                    Assert.Equal(CloseReason.ProtocolViolation, observedDisconnect.Reason);
                    var fault = Assert.IsType<InvalidDataException>(observedDisconnect.Fault);
                    Assert.Contains("2147483647", fault.Message, StringComparison.Ordinal);
                    Assert.Contains("umpk_test:missing_player_lifetime_type", fault.Message, StringComparison.Ordinal);
                    break;
                case EndTrigger.PeerClose:
                    Assert.Equal(CloseReason.SocketEof, observedDisconnect.Reason);
                    Assert.Equal(CloseReason.SocketEof, Assert.IsType<ConnectionClosedException>(observedDisconnect.Fault).Reason);
                    break;
                case EndTrigger.LocalDisconnect:
                    Assert.Equal(CloseReason.Local, observedDisconnect.Reason);
                    Assert.Null(observedDisconnect.Fault);
                    break;
            }

            ConnectionClosedException peerEnd;
            try
            {
                peerEnd = await peerEof.WaitAsync(TimeSpan.FromSeconds(2), session.Token);
            }
            catch (TimeoutException)
            {
                if (trigger == EndTrigger.RequiredApplierFault)
                    Assert.Fail("The required-applier fault must close the captured peer before test cleanup, public DisconnectAsync, disposal, or another player tick; a Disconnected event alone is insufficient.");
                Assert.Fail("The close control must end the captured peer before test cleanup; cancellation is not peer EOF.");
                throw;
            }
            Assert.Equal(trigger == EndTrigger.PeerClose ? CloseReason.Local : CloseReason.SocketEof, peerEnd.Reason);
            Assert.Equal(tickBefore, session.Client.State.SessionTick);
            AssertCachedSnapshot(before, Capture(session.Client));
            Assert.Equal(1, Volatile.Read(ref disconnectEvents));
            Assert.Equal(1, Volatile.Read(ref terminalTransitions));
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            try
            {
                using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await session.Client.DisconnectAsync(cleanupBudget.Token);
                await peerEof.WaitAsync(cleanupBudget.Token);
                if (observedDisconnect is not null)
                {
                    Assert.Same(observedDisconnect, session.Client.LastDisconnect);
                    AssertCachedSnapshot(before, Capture(session.Client));
                    Assert.Equal(1, Volatile.Read(ref disconnectEvents));
                    Assert.Equal(1, Volatile.Read(ref terminalTransitions));
                }
            }
            catch (Exception cleanupFailure) when (primaryFailure is not null)
            {
                // Keep both failures observable instead of replacing the causal EOF assertion with a cleanup timeout.
                throw new AggregateException("Both the session assertion and owned test cleanup failed.", primaryFailure, cleanupFailure);
            }
            finally
            {
                session.Client.StatusChanged -= ObserveStatus;
            }
        }
    }

    private static async Task SendInvalidRequiredRespawnAsync(Session session, FakeJavaServer peer)
    {
        var invalid = new ClientboundRespawnPacket(Spawn with
        {
            Dimension = "umpk_test:missing_player_lifetime_type",
            DimensionTypeId = int.MaxValue,
        }, 3, null);
        // Constructed valid wire: decoding and byte-identical re-encoding succeed; the required WorldSetup registry lookup is the intended fault boundary.
        byte[] bytes = EncodeAndAssert(session.Client, invalid, actual => Assert.Equal(invalid, actual));
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, invalid.Type.Id.Path);
        int rawFrames = 0;
        int appliedPackets = 0;
        void Observe(PacketObservation frame)
        {
            if (frame.Flow == PacketFlow.Clientbound && frame.Phase == ProtocolPhase.Play
                && frame.WireId == codec.WireId && frame.RawPayload.SequenceEqual(bytes))
                Interlocked.Increment(ref rawFrames);
        }
        using IDisposable received = session.Client.Events.Subscribe<PacketReceived>(message =>
        {
            if (message.Packet is ClientboundRespawnPacket)
                Interlocked.Increment(ref appliedPackets);
        });
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable disconnected = session.Client.Events.Subscribe<Disconnected>(_ => ended.TrySetResult());
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await peer.SendFrameAsync(codec.WireId, bytes, session.Token);
            await ended.Task.WaitAsync(session.Token);
            Assert.Equal(1, Volatile.Read(ref rawFrames));
            Assert.Equal(0, Volatile.Read(ref appliedPackets));
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static async Task<InventorySnapshot> SeedClosedInventoryAndLossAsync(Session session)
    {
        Assert.False(session.Client.State.Inventory.HasOpenContainer);
        var items = new ItemStack[46];
        Array.Fill(items, ItemStack.Empty);
        items[LostSlot] = new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_sword")], 1);
        items[SelectedMenuSlot] = new ItemStack(session.Client.State.Registries.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        var contents = new ClientboundContainerSetContentPacket(0, 6, items, ItemStack.Empty);
        await SendKnownAndWaitAsync(session, contents, actual =>
        {
            Assert.Equal(0, actual.ContainerId);
            Assert.Equal(6, actual.StateId);
            Assert.Equal(items, actual.Items.ToArray());
            Assert.True(actual.CarriedItem.IsEmpty);
        });
        var held = new ClientboundSetHeldSlotPacket(SelectedSlot);
        await SendKnownAndWaitAsync(session, held, actual => Assert.Equal(held, actual));

        byte[] loss = Convert.FromBase64String(NativeLossBase64);
        Assert.Equal(21, loss.Length);
        Assert.Equal(NativeLossSha256, Convert.ToHexStringLower(SHA256.HashData(loss)));
        Assert.Equal(new byte[] { 0, 7, 0, LostSlot }, loss[..4]);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, "container_set_slot");
        int rawLossFrames = 0;
        int appliedLossPackets = 0;
        void Observe(PacketObservation frame)
        {
            if (frame.Flow == PacketFlow.Clientbound && frame.Phase == ProtocolPhase.Play
                && frame.WireId == codec.WireId && frame.RawPayload.SequenceEqual(loss))
                Interlocked.Increment(ref rawLossFrames);
        }
        using IDisposable received = session.Client.Events.Subscribe<PacketReceived>(message =>
        {
            if (message.Packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 7, Slot: LostSlot })
                Interlocked.Increment(ref appliedLossPackets);
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await session.Server.SendFrameAsync(codec.WireId, loss, session.Token);
            var fence = new ClientboundGameEventPacket(3, 0);
            await SendKnownAndWaitAsync(session, fence, actual => Assert.Equal(fence, actual));
            Assert.Equal(1, Volatile.Read(ref rawLossFrames));
            Assert.Equal(0, Volatile.Read(ref appliedLossPackets));
            InventorySnapshot snapshot = await session.Client.InvokeAsync(Capture, session.Token);
            Assert.Equal(items, snapshot.Items);
            Assert.Equal(SelectedSlot, snapshot.HeldSlot);
            Assert.False(snapshot.Available[LostSlot]);
            Assert.True(snapshot.Available[SelectedMenuSlot]);
            return snapshot;
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static async Task SendKnownAndWaitAsync<TPacket>(Session session, TPacket packet, Action<TPacket> assertDecoded)
        where TPacket : class, IPacket
    {
        byte[] bytes = EncodeAndAssert(session.Client, packet, assertDecoded);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, packet.Type.Id.Path);
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable received = session.Client.Events.Subscribe<PacketReceived>(message =>
        {
            if (message.Phase == ProtocolPhase.Play && message.WireId == codec.WireId && message.Packet is TPacket)
                applied.TrySetResult();
        });
        await session.Server.SendFrameAsync(codec.WireId, bytes, session.Token);
        await applied.Task.WaitAsync(session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
    }

    private static byte[] EncodeAndAssert<TPacket>(UmpkClient client, TPacket packet, Action<TPacket> assertDecoded)
        where TPacket : class, IPacket
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<TPacket>(codec.Decode(bytes, context));
        assertDecoded(decoded);
        Assert.Equal(bytes, Encode(codec, decoded, context));
        return bytes;
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        codec.Encode(ref writer, packet, context);
        return bytes.WrittenSpan.ToArray();
    }

    private static async Task<ConnectionClosedException> ObservePeerEndAsync(FakeJavaServer peer, CancellationToken token)
    {
        try
        {
            while (true)
                await peer.NextFrameAsync(token);
        }
        catch (ConnectionClosedException closed)
        {
            return closed;
        }
    }

    private static InventorySnapshot Capture(UmpkClient client)
    {
        InventoryState inventory = client.State.Inventory;
        return new(client.State, inventory, client.State.World, inventory.PlayerSlots.ToArray(),
            Enumerable.Range(0, 46).Select(inventory.IsPlayerSlotAvailable).ToArray(),
            client.State.Self.HeldSlot, inventory.HasOpenContainer, client.State.PlayerLifetime);
    }

    private static void AssertCachedSnapshot(InventorySnapshot before, InventorySnapshot after)
    {
        Assert.Same(before.State, after.State);
        Assert.Same(before.Inventory, after.Inventory);
        Assert.Same(before.World, after.World);
        Assert.Equal(before.Items, after.Items);
        Assert.Equal(before.Available, after.Available);
        Assert.Equal(before.HeldSlot, after.HeldSlot);
        Assert.Equal(before.HasOpenContainer, after.HasOpenContainer);
        Assert.Equal(before.PlayerLifetime, after.PlayerLifetime);
    }

    private sealed record InventorySnapshot(ClientState State, InventoryState Inventory, World World,
        ItemStack[] Items, bool[] Available, int HeldSlot, bool HasOpenContainer, long PlayerLifetime);

    private enum EndTrigger
    {
        RequiredApplierFault,
        PeerClose,
        LocalDisconnect,
    }
}
