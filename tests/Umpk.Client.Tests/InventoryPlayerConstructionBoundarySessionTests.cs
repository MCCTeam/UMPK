using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using Umpk.Client.Events;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
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

/// <summary>Four protocol770 construction boundaries beyond the closed-inventory Respawn/repeated-Join pair. Snapshots run in JoinedGame before the unchanged helper can refill inventory. These managed framed witnesses do not assert automatic carried-index synchronization or all-era behavior.</summary>
public sealed class InventoryPlayerConstructionBoundarySessionTests
{
    private const int Protocol = 770;
    private const int SelectedSlot = 5;
    private const int LostSlot = 36;
    private const int SelectedMenuSlot = 36 + SelectedSlot;
    private const string NativeLossBase64 = "AAcAJAEBAQAMAQAAAAEaAQAAAAAA";
    private const string NativeLossSha256 = "578cc93204fbdc086a8c783fcc0442525635ef4cc4388311ed14bda69ff5e165";
    private static readonly CommonPlayerSpawnInfo Spawn = new(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);

    [Fact]
    public async Task ReconnectConstructsInventoryBeforeNewResends()
    {
        await using Session session = await Session.Create(Protocol, "stone");
        bool peerEofObserved = false;
        try
        {
            InventorySnapshot before = await SeedClosedInventoryAndLoss(session);
            await DisconnectAndObserveEof(session.Client, session.Server, session.Token);
            peerEofObserved = true;
            Assert.Equal(ClientStatus.Disconnected, session.Client.Status);
            AssertCachedSnapshot(before, Capture(session.Client));

            // Register before Restart creates its own JoinedGame waiter. The synchronous callback freezes state before PrepareWorld sends Hold(null).
            peerEofObserved = false;
            InventorySnapshot constructed = await CaptureNextJoin(session, () => session.Restart("stone"));
            Assert.Same(before.State, constructed.State);
            Assert.Same(before.Inventory, constructed.Inventory);
            Assert.NotSame(before.World, constructed.World);
            Assert.True(constructed.Items[SelectedMenuSlot].IsEmpty,
                "Reconnect must discard the cached item before the helper sends any inventory refill.");
            AssertFreshSnapshot(constructed);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally
        {
            if (!peerEofObserved)
                await DisconnectAndObserveEof(session.Client, session.Server, session.Token);
        }
    }

    [Fact]
    public async Task ConfigurationReentryRecreatesSelectionBeforeNewResends()
    {
        await using Session session = await Session.Create(Protocol, "stone");
        try
        {
            InventorySnapshot before = await SeedClosedInventoryAndLoss(session);
            await session.Reconfigure(null);
            Assert.False(session.Client.State.Self.HasSpawned);
            Assert.False(session.Client.State.HasWorld);

            InventorySnapshot constructed = await CaptureNextJoin(session, () => session.RejoinWorld("stone"));
            Assert.Same(before.State, constructed.State);
            Assert.Same(before.Inventory, constructed.Inventory);
            Assert.NotSame(before.World, constructed.World);
            Assert.Equal(0, constructed.HeldSlot);
            AssertFreshSnapshot(constructed);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally
        {
            await DisconnectAndObserveEof(session.Client, session.Server, session.Token);
        }
    }

    [Fact]
    public async Task InvalidRequiredRespawnPreservesCachedInventory()
    {
        await using Session session = await Session.Create(Protocol, "stone");
        try
        {
            InventorySnapshot before = await SeedClosedInventoryAndLoss(session);
            int respawns = 0;
            var disconnected = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable respawned = session.Client.Events.Subscribe<Respawned>(_ => Interlocked.Increment(ref respawns));
            using IDisposable ended = session.Client.Events.Subscribe<Disconnected>(e => disconnected.TrySetResult(e.Info));
            var invalid = new ClientboundRespawnPacket(Spawn with
            {
                Dimension = "umpk_test:missing_player_lifetime_type",
                DimensionTypeId = int.MaxValue,
            }, 3, null);
            // This codec reads the id as a VarInt. Decode and round-trip must succeed before the required WorldSetup lookup fails.
            byte[] bytes = EncodeAndAssert(session.Client, invalid, actual => Assert.Equal(invalid, actual));
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, invalid.Type.Id.Path);
            await session.Server.SendFrameAsync(codec.WireId, bytes, session.Token);
            DisconnectInfo failure = await disconnected.Task.WaitAsync(session.Token);

            Assert.Equal(CloseReason.ProtocolViolation, failure.Reason);
            Assert.IsType<InvalidDataException>(failure.Fault);
            Assert.Equal(0, Volatile.Read(ref respawns));
            AssertCachedSnapshot(before, Capture(session.Client));
        }
        finally
        {
            using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await DisconnectAndObserveEof(session.Client, session.Server, cleanupBudget.Token);
        }
        Assert.Equal(ClientStatus.Disconnected, session.Client.Status);
    }

    [Fact]
    public async Task InventoryDisabledRespawnStillResetsSelection()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken token = budget.Token;
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion version));
        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "InvLifeOff"))
            .UseConnectionFactory(new PipeFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(Protocol))
            .ConfigureFeatures(features =>
            {
                features.Inventory = false;
                features.Physics = false;
                features.Pathfinding = false;
            }).Build();
        try
        {
            await Task.WhenAll(client.ConnectAsync(new ServerEndpoint("test", 25565), token),
                ScriptedServer.DriveToPlayAsync(server, version, token));
            var join = new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8,
                false, true, false, Spawn, false, false, null);
            await SendKnownAndWait(client, server, join, AssertJoin, token);
            Assert.Null(client.State.InventoryOrNull);
            Assert.Throws<FeatureDisabledException>(() => { _ = client.State.Inventory; });

            var held = new ClientboundSetHeldSlotPacket(SelectedSlot);
            await SendKnownAndWait(client, server, held, actual => Assert.Equal(held, actual), token);
            Assert.Equal(SelectedSlot, client.State.Self.HeldSlot);
            var respawn = new ClientboundRespawnPacket(Spawn, 3, null);
            await SendKnownAndWait(client, server, respawn, actual => Assert.Equal(respawn, actual), token);
            Assert.Equal(0, client.State.Self.HeldSlot);
            Assert.Null(client.State.InventoryOrNull);
            Assert.Throws<FeatureDisabledException>(() => { _ = client.State.Inventory; });
            Assert.Equal(ClientStatus.Playing, client.Status);
        }
        finally
        {
            await DisconnectAndObserveEof(client, server, token);
        }
    }

    private static async Task<InventorySnapshot> SeedClosedInventoryAndLoss(Session session)
    {
        Assert.False(session.Client.State.Inventory.HasOpenContainer);
        var items = new ItemStack[46];
        Array.Fill(items, ItemStack.Empty);
        items[LostSlot] = new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_sword")], 1);
        items[SelectedMenuSlot] = new ItemStack(session.Client.State.Registries.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        var contents = new ClientboundContainerSetContentPacket(0, 6, items, ItemStack.Empty);
        await SendKnownAndWait(session.Client, session.Server, contents, actual =>
        {
            Assert.Equal(0, actual.ContainerId);
            Assert.Equal(6, actual.StateId);
            Assert.Equal(items, actual.Items.ToArray());
            Assert.True(actual.CarriedItem.IsEmpty);
        }, session.Token);
        var held = new ClientboundSetHeldSlotPacket(SelectedSlot);
        await SendKnownAndWait(session.Client, session.Server, held, actual => Assert.Equal(held, actual), session.Token);
        Assert.Equal(SelectedSlot, session.Client.State.Self.HeldSlot);
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(LostSlot));

        byte[] loss = Convert.FromBase64String(NativeLossBase64);
        Assert.Equal(21, loss.Length);
        Assert.Equal(NativeLossSha256, Convert.ToHexStringLower(SHA256.HashData(loss)));
        Assert.Equal(new byte[] { 0, 7, 0, LostSlot }, loss[..4]);
        BoundPacketCodec lossCodec = BoundDescriptorCodec.Clientbound(Protocol, "container_set_slot");
        int rawLossFrames = 0;
        int decodedLossPackets = 0;
        void Observe(PacketObservation frame)
        {
            if (frame.Flow == PacketFlow.Clientbound && frame.Phase == ProtocolPhase.Play
                && frame.WireId == lossCodec.WireId && frame.RawPayload.SequenceEqual(loss))
                Interlocked.Increment(ref rawLossFrames);
        }
        using IDisposable received = session.Client.Events.Subscribe<PacketReceived>(packet =>
        {
            if (packet.Packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 7, Slot: LostSlot })
                Interlocked.Increment(ref decodedLossPackets);
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await session.Server.SendFrameAsync(lossCodec.WireId, loss, session.Token);
            var fence = new ClientboundGameEventPacket(3, 0);
            await SendKnownAndWait(session.Client, session.Server, fence, actual => Assert.Equal(fence, actual), session.Token);
            Assert.Equal(1, Volatile.Read(ref rawLossFrames));
            Assert.Equal(0, Volatile.Read(ref decodedLossPackets));
            InventorySnapshot before = await session.Client.InvokeAsync(Capture, session.Token);
            Assert.Equal(items, before.Items);
            Assert.Equal(SelectedSlot, before.HeldSlot);
            Assert.False(before.Available[LostSlot]);
            Assert.True(before.Available[SelectedMenuSlot]);
            return before;
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static async Task<InventorySnapshot> CaptureNextJoin(Session session, Func<Task> drive)
    {
        var snapshot = new TaskCompletionSource<InventorySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, "login");
        int joins = 0;
        int frames = 0;
        using IDisposable joined = session.Client.Events.Subscribe<JoinedGame>(_ =>
        {
            Interlocked.Increment(ref joins);
            try { snapshot.TrySetResult(Capture(session.Client)); }
            catch (Exception error) { snapshot.TrySetException(error); }
        });
        void Observe(PacketObservation frame)
        {
            if (frame.Flow == PacketFlow.Clientbound && frame.Phase == ProtocolPhase.Play && frame.WireId == codec.WireId)
            {
                Interlocked.Increment(ref frames);
                raw.TrySetResult(frame.RawPayload.ToArray());
            }
        }
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await drive();
            InventorySnapshot constructed = await snapshot.Task.WaitAsync(session.Token);
            byte[] bytes = await raw.Task.WaitAsync(session.Token);
            var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
            var decoded = Assert.IsType<ClientboundLoginPacket>(codec.Decode(bytes, context));
            AssertJoin(decoded);
            Assert.Equal(bytes, Encode(codec, decoded, context));
            Assert.Equal(1, Volatile.Read(ref joins));
            Assert.Equal(1, Volatile.Read(ref frames));
            await session.Client.InvokeAsync(_ => true, session.Token);
            return constructed;
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static InventorySnapshot Capture(UmpkClient client)
    {
        InventoryState inventory = client.State.Inventory;
        return new(client.State, inventory, client.State.World, inventory.PlayerSlots.ToArray(),
            Enumerable.Range(0, 46).Select(inventory.IsPlayerSlotAvailable).ToArray(),
            client.State.Self.HeldSlot, inventory.HasOpenContainer);
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
    }

    private static void AssertFreshSnapshot(InventorySnapshot snapshot)
    {
        Assert.Equal(46, snapshot.Items.Length);
        Assert.All(snapshot.Items, item => Assert.True(item.IsEmpty));
        Assert.Equal(0, snapshot.HeldSlot);
        Assert.All(snapshot.Available, available => Assert.True(available));
        Assert.False(snapshot.HasOpenContainer);
    }

    private static void AssertJoin(ClientboundLoginPacket packet)
    {
        Assert.Equal(1, packet.PlayerId);
        Assert.Equal(new[] { "minecraft:overworld" }, packet.Dimensions.ToArray());
        Assert.Equal(20, packet.MaxPlayers);
        Assert.Equal(8, packet.ViewDistance);
        Assert.Equal(8, packet.SimulationDistance);
        Assert.Equal(Spawn, packet.SpawnInfo);
        Assert.False(packet.Hardcore);
        Assert.False(packet.ReducedDebugInfo);
        Assert.True(packet.ShowDeathScreen);
        Assert.False(packet.DoLimitedCrafting);
        Assert.False(packet.EnforcesSecureChat);
        Assert.Null(packet.Legacy);
    }

    private static async Task SendKnownAndWait<TPacket>(UmpkClient client, FakeJavaServer server, TPacket packet, Action<TPacket> assertDecoded, CancellationToken token)
        where TPacket : class, IPacket
    {
        byte[] bytes = EncodeAndAssert(client, packet, assertDecoded);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, packet.Type.Id.Path);
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable received = client.Events.Subscribe<PacketReceived>(frame =>
        {
            if (frame.Phase == ProtocolPhase.Play && frame.WireId == codec.WireId && frame.Packet is TPacket)
                applied.TrySetResult();
        });
        await server.SendFrameAsync(codec.WireId, bytes, token);
        await applied.Task.WaitAsync(token);
        await client.InvokeAsync(_ => true, token);
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

    private static async Task DisconnectAndObserveEof(UmpkClient client, FakeJavaServer server, CancellationToken token)
    {
        await client.DisconnectAsync(token);
        await ObserveEof(server, token);
    }

    private static Task ObserveEof(FakeJavaServer server, CancellationToken token)
        => Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await server.NextFrameAsync(token);
        });

    private sealed record InventorySnapshot(ClientState State, InventoryState Inventory, World World,
        ItemStack[] Items, bool[] Available, int HeldSlot, bool HasOpenContainer);

    private sealed class PipeFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }
}
