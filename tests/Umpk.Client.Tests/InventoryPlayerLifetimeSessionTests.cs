using System.Buffers;
using System.Security.Cryptography;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = ComponentAdventureMiningSessionTests.Session;

/// <summary>Two controlled protocol770 player-construction witnesses with the player inventory closed. The native21-byte loss payload is unchanged; the other frames are constructed and round-tripped through the bound codecs. Carried-index packet synchronization and external-menu callbacks are outside this slice.</summary>
public sealed class InventoryPlayerLifetimeSessionTests
{
    private const int Protocol = 770;
    private const int SelectedSlot = 5;
    private const int NativeLostSlot = 36;
    private const string NativeLossBase64 = "AAcAJAEBAQAMAQAAAAEaAQAAAAAA";
    private const string NativeLossSha256 = "578cc93204fbdc086a8c783fcc0442525635ef4cc4388311ed14bda69ff5e165";
    private static readonly CommonPlayerSpawnInfo Spawn = new(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);

    [Fact]
    public async Task RespawnKeepAllBuildsFreshClosedPlayerInventory()
    {
        await using Session session = await Session.Create(Protocol);
        await SeedClosedInventoryAndLoss(session);
        var priorWorld = session.Client.State.World;

        var respawn = new ClientboundRespawnPacket(Spawn, 3, null);
        await SendKnownAndWait(session, respawn, actual =>
        {
            Assert.Equal(Spawn, actual.SpawnInfo);
            Assert.Equal((byte)3, actual.DataToKeep);
            Assert.Null(actual.Legacy);
        });

        // The same dimension retains the world; the native Respawn path still constructs a new player and Inventory.
        Assert.Same(priorWorld, session.Client.State.World);
        Assert.True(session.Client.State.Inventory.PlayerSlots[NativeLostSlot].IsEmpty,
            "A freshly constructed native player must not retain the cached pre-respawn stack.");
        Assert.Equal(46, session.Client.State.Inventory.PlayerSlots.Count);
        Assert.All(session.Client.State.Inventory.PlayerSlots, item => Assert.True(item.IsEmpty));
        Assert.Equal(0, session.Client.State.Self.HeldSlot);
        for (int slot = 0; slot < 46; slot++)
            Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(slot));
        Assert.True(session.Client.State.Inventory.IsActiveSnapshotAvailable);
        Assert.False(session.Client.State.Inventory.HasOpenContainer);
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }

    [Fact]
    public async Task RepeatedJoinWithExistingPlayerPreservesClosedPlayerInventory()
    {
        await using Session session = await Session.Create(Protocol);
        await SeedClosedInventoryAndLoss(session);
        ItemStack[] priorItems = session.Client.State.Inventory.PlayerSlots.ToArray();
        var priorState = session.Client.State;
        var priorInventory = session.Client.State.Inventory;

        // This controlled path has an existing player and no external menu or unrelated screen/progress teardown callback.
        var join = new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8,
            false, true, false, Spawn, false, false, null);
        await SendKnownAndWait(session, join, actual =>
        {
            Assert.Equal(join.PlayerId, actual.PlayerId);
            Assert.Equal(join.Dimensions.ToArray(), actual.Dimensions.ToArray());
            Assert.Equal(join.MaxPlayers, actual.MaxPlayers);
            Assert.Equal(join.ViewDistance, actual.ViewDistance);
            Assert.Equal(join.SimulationDistance, actual.SimulationDistance);
            Assert.Equal(Spawn, actual.SpawnInfo);
            Assert.Equal(join.Hardcore, actual.Hardcore);
            Assert.Equal(join.ReducedDebugInfo, actual.ReducedDebugInfo);
            Assert.Equal(join.ShowDeathScreen, actual.ShowDeathScreen);
            Assert.Equal(join.DoLimitedCrafting, actual.DoLimitedCrafting);
            Assert.Equal(join.EnforcesSecureChat, actual.EnforcesSecureChat);
            Assert.Null(actual.Legacy);
        });

        Assert.Same(priorState, session.Client.State);
        Assert.Same(priorInventory, session.Client.State.Inventory);
        Assert.Equal(priorItems, session.Client.State.Inventory.PlayerSlots.ToArray());
        Assert.Equal(SelectedSlot, session.Client.State.Self.HeldSlot);
        Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(NativeLostSlot));
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36 + SelectedSlot));
        Assert.False(session.Client.State.Inventory.IsActiveSnapshotAvailable);
        Assert.False(session.Client.State.Inventory.HasOpenContainer);
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }

    private static async Task SeedClosedInventoryAndLoss(Session session)
    {
        Assert.False(session.Client.State.Inventory.HasOpenContainer);
        var items = new ItemStack[46];
        Array.Fill(items, ItemStack.Empty);
        items[NativeLostSlot] = new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_sword")], 1);
        items[36 + SelectedSlot] = new ItemStack(session.Client.State.Registries.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        var contents = new ClientboundContainerSetContentPacket(0, 6, items, ItemStack.Empty);
        await SendKnownAndWait(session, contents, actual =>
        {
            Assert.Equal(0, actual.ContainerId);
            Assert.Equal(6, actual.StateId);
            Assert.Equal(items, actual.Items.ToArray());
            Assert.True(actual.CarriedItem.IsEmpty);
        });
        var held = new ClientboundSetHeldSlotPacket(SelectedSlot);
        await SendKnownAndWait(session, held, actual => Assert.Equal(held, actual));
        Assert.Equal(SelectedSlot, session.Client.State.Self.HeldSlot);
        Assert.Equal(items, session.Client.State.Inventory.PlayerSlots.ToArray());
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(NativeLostSlot));

        byte[] loss = Convert.FromBase64String(NativeLossBase64);
        Assert.Equal(21, loss.Length);
        Assert.Equal(NativeLossSha256, Convert.ToHexStringLower(SHA256.HashData(loss)));
        Assert.Equal(new byte[] { 0, 7, 0, NativeLostSlot }, loss[..4]);
        BoundPacketCodec lossCodec = BoundDescriptorCodec.Clientbound(Protocol, "container_set_slot");
        int observedLossFrames = 0;
        int decodedLossPackets = 0;
        void Observe(PacketObservation observation)
        {
            if (observation.Flow == PacketFlow.Clientbound && observation.Phase == ProtocolPhase.Play
                && observation.WireId == lossCodec.WireId && observation.RawPayload.SequenceEqual(loss))
                Interlocked.Increment(ref observedLossFrames);
        }

        using IDisposable received = session.Client.Events.Subscribe<PacketReceived>(packet =>
        {
            if (packet.Packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 7, Slot: NativeLostSlot })
                Interlocked.Increment(ref decodedLossPackets);
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            // The unsupported native payload cannot round-trip through this codec; preserve it and prove its actual loss reaches the owning session.
            await session.Server.SendFrameAsync(lossCodec.WireId, loss, session.Token);
            var fence = new ClientboundGameEventPacket(3, 0);
            await SendKnownAndWait(session, fence, actual => Assert.Equal(fence, actual));
            Assert.Equal(1, Volatile.Read(ref observedLossFrames));
            Assert.Equal(0, Volatile.Read(ref decodedLossPackets));
            Assert.Equal(items, session.Client.State.Inventory.PlayerSlots.ToArray());
            Assert.Equal(SelectedSlot, session.Client.State.Self.HeldSlot);
            Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(NativeLostSlot));
            Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36 + SelectedSlot));
            Assert.False(session.Client.State.Inventory.IsActiveSnapshotAvailable);
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static async Task SendKnownAndWait<TPacket>(Session session, TPacket packet, Action<TPacket> assertDecoded)
        where TPacket : class, IPacket
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<TPacket>(codec.Decode(bytes, context));
        assertDecoded(decoded);
        Assert.Equal(bytes, Encode(codec, decoded, context));

        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable received = session.Client.Events.Subscribe<PacketReceived>(observation =>
        {
            if (observation.Phase == ProtocolPhase.Play && observation.WireId == codec.WireId && observation.Packet is TPacket)
                applied.TrySetResult();
        });
        await session.Server.SendFrameAsync(codec.WireId, bytes, session.Token);
        await applied.Task.WaitAsync(session.Token);
        // PacketReceived follows application; this additional owner-loop barrier prevents the query from racing a shared frame.
        await session.Client.InvokeAsync(_ => true, session.Token);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        codec.Encode(ref writer, packet, context);
        return bytes.WrittenSpan.ToArray();
    }
}
