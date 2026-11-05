using System.Buffers;
using System.Security.Cryptography;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = ComponentAdventureMiningSessionTests.Session;

/// <summary>Constructed wire witnesses for connection-to-session inventory loss routing. The original 21-byte native held update is separately pinned and sent unchanged where named; modified headers and bundle composition are managed test inputs, not native execution.</summary>
public sealed class InventoryDecodeLossRoutingSessionTests
{
    private const int Protocol = 770;
    private const int LostRevision = 7;
    private const int FenceSlot = 9;
    private const string NativeHeldBase64 = "AAcAJAEBAQAMAQAAAAEaAQAAAAAA";
    private const string NativeHeldSha256 = "578cc93204fbdc086a8c783fcc0442525635ef4cc4388311ed14bda69ff5e165";

    [Theory]
    [InlineData("non-held-slot")]
    [InlineData("closed-window")]
    [InlineData("cursor-container")]
    [InlineData("cursor-packet")]
    public async Task ConstructedUnrelatedLossDoesNotDisableHeldMining(string kind)
    {
        await using Session session = await Create();
        if (kind == "closed-window")
        {
            int menuTypeId = session.Client.State.Registries!.MenuTypes[Identifier.Minecraft("generic_9x3")].NetworkId;
            await SendKnownAndWait(session,
                new ClientboundOpenScreenPacket(5, menuTypeId, Umpk.Text.Component.Text("Scoped loss"), null, 0, null),
                packet => packet is ClientboundOpenScreenPacket { ContainerId: 5 });
            Assert.Equal(5, session.Client.State.Inventory.OpenWindowId);
            Assert.Equal(Identifier.Minecraft("generic_9x3"), session.Client.State.Inventory.OpenContainerMenuType!.Value.Id);
            var oldContents = new ItemStack[63];
            Array.Fill(oldContents, ItemStack.Empty);
            oldContents[54] = HeldItem(session);
            await SendKnownAndWait(session, new ClientboundContainerSetContentPacket(5, 6, oldContents, ItemStack.Empty),
                packet => packet is ClientboundContainerSetContentPacket { ContainerId: 5, StateId: 6 });
            Assert.Equal(Identifier.Minecraft("diamond_pickaxe"), session.Client.State.Inventory.GetSlot(54).Item.Id);
            await SendKnownAndWait(session, new ClientboundContainerClosePacket(5),
                packet => packet is ClientboundContainerClosePacket { ContainerId: 5 });
            Assert.Equal(0, session.Client.State.Inventory.OpenWindowId);
        }

        await DeliverLossAndFence(session, ConstructedLoss(kind), fenceRevision: 8);
        AssertCachedHeld(session);
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36 + session.Client.State.Self.HeldSlot));
        await AssertMiningOutcome(session, permitted: true);
    }

    [Theory]
    [InlineData("contents-non-held")]
    [InlineData("direct-inventory-zero")]
    public async Task ConstructedContainingUpdateLossMakesHeldMiningUnavailable(string kind)
    {
        await using Session session = await Create();
        await DeliverLossAndFence(session, ConstructedLoss(kind), fenceRevision: 8);

        // The lost full snapshot fails at entry9, before held36; a later slot9 fence cannot restore held36. Direct player-inventory0 addresses that same held menu slot.
        AssertCachedHeld(session);
        Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(36 + session.Client.State.Self.HeldSlot));
        await AssertMiningOutcome(session, permitted: false);
    }

    [Theory]
    [InlineData(7, 7)]
    [InlineData(8, 7)]
    [InlineData(32767, 0)]
    public async Task AcceptedHeldUpdateRestoresMiningAtRepeatedReusedOrWrappedRevision(int fenceRevision, int restoredRevision)
    {
        await using Session session = await Create();
        await DeliverLossAndFence(session, OriginalNativeHeldLoss(), fenceRevision);
        Assert.Equal(fenceRevision, session.Client.State.Inventory.StateId);
        AssertCachedHeld(session);
        Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(36 + session.Client.State.Self.HeldSlot));
        await AssertMiningOutcome(session, permitted: false);

        var restored = new ClientboundContainerSetSlotPacket(0, restoredRevision, 36, HeldItem(session));
        await SendKnownAndWait(session, restored,
            packet => packet is ClientboundContainerSetSlotPacket slot && slot.ContainerId == 0
                && slot.Slot == 36 && slot.StateId == restoredRevision
                && slot.Item.Item.Id == Identifier.Minecraft("diamond_pickaxe"));
        Assert.Equal(restoredRevision, session.Client.State.Inventory.StateId);
        AssertCachedHeld(session);
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36 + session.Client.State.Self.HeldSlot));
        await AssertMiningOutcome(session, permitted: true);
    }

    [Fact]
    public async Task BundleHeldBeforeLossAndUnrelatedAfterLossCannotRestoreHeldMining()
    {
        await using Session session = await Create();
        await DeliverLossAndFence(session, OriginalNativeHeldLoss(), fenceRevision: 8, bundled: true);

        // The bundle's first packet refreshes held36 BEFORE the loss. Applying all loss sidecars at the front would let that earlier packet restore knowledge incorrectly.
        AssertCachedHeld(session);
        Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(36 + session.Client.State.Self.HeldSlot));
        await AssertMiningOutcome(session, permitted: false);
    }

    private static async Task<Session> Create()
    {
        Session session = await Session.Create(Protocol);
        try
        {
            await session.Mode(GameMode.Survival, instantBuild: false);
            await session.Info(GameMode.Survival);
            await session.Block(Session.Target, "bedrock");
            await session.Hold("diamond_pickaxe");
            Assert.Equal(0, session.Client.State.Self.HeldSlot);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task AssertMiningOutcome(Session session, bool permitted)
    {
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> sent = await session.Tick();
            if (permitted)
            {
                Assert.Collection(sent,
                    packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), Protocol, 0),
                    AssertMainHandSwing,
                    AssertMainHandSwing);
                Assert.False(action.IsCompleted);
                AssertMainHandSwing(Assert.Single(await session.Tick()));
            }
            else
            {
                Assert.Empty(sent);
                await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
                Assert.Empty(await session.Tick());
                Assert.Empty(await session.Tick());
            }

            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally
        {
            await caller.CancelAsync();
            await session.Tick();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (!permitted) { }
        }
    }

    private static void AssertMainHandSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.True(swing.HasHand);
        Assert.Equal(0, swing.Hand);
    }

    private static void AssertCachedHeld(Session session)
    {
        ItemStack cached = session.Client.State.Inventory.GetSlot(36);
        Assert.Equal(Identifier.Minecraft("diamond_pickaxe"), cached.Item.Id);
        Assert.Equal(1, cached.Count);
    }

    private static ItemStack HeldItem(Session session)
        => new(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_pickaxe")], 1);

    private static async Task DeliverLossAndFence(Session session, LossFrame loss, int fenceRevision, bool bundled = false)
    {
        int rawLossFrames = 0;
        int decodedLossPackets = 0;
        var appliedFence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(PacketObservation observation)
        {
            if (observation.Flow == PacketFlow.Clientbound && observation.Phase == ProtocolPhase.Play
                && observation.WireId == loss.Codec.WireId && observation.RawPayload.SequenceEqual(loss.Body))
                Interlocked.Increment(ref rawLossFrames);
        }

        using IDisposable subscription = session.Client.Events.Subscribe<PacketReceived>(packet =>
        {
            if (packet.Phase == ProtocolPhase.Play && packet.WireId == loss.Codec.WireId && loss.MatchesPacket(packet.Packet))
                Interlocked.Increment(ref decodedLossPackets);
            if (packet.Packet is ClientboundContainerSetSlotPacket slot && slot.ContainerId == 0
                && slot.Slot == FenceSlot && slot.StateId == fenceRevision && slot.Item.Count == 2)
                appliedFence.TrySetResult();
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            if (bundled)
            {
                await SendKnown(session, new ClientboundBundleDelimiterPacket());
                await SendKnown(session, new ClientboundContainerSetSlotPacket(0, 6, 36, HeldItem(session)));
            }
            await session.Server.SendFrameAsync(loss.Codec.WireId, loss.Body, session.Token);
            ItemStack fence = new(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], 2);
            await SendKnown(session, new ClientboundContainerSetSlotPacket(0, fenceRevision, FenceSlot, fence));
            if (bundled)
                await SendKnown(session, new ClientboundBundleDelimiterPacket());
            await appliedFence.Task.WaitAsync(session.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);

            Assert.Equal(1, Volatile.Read(ref rawLossFrames));
            Assert.Equal(0, Volatile.Read(ref decodedLossPackets));
            ItemStack applied = session.Client.State.Inventory.GetSlot(FenceSlot);
            Assert.Equal(Identifier.Minecraft("stone"), applied.Item.Id);
            Assert.Equal(2, applied.Count);
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
        await SendKnown(session, packet);
        await applied.Task.WaitAsync(session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
    }

    private static async Task SendKnown(Session session, IPacket packet)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        object decoded = codec.Decode(bytes, context);
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Server.SendFrameAsync(codec.WireId, bytes, session.Token);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        codec.Encode(ref writer, packet, context);
        return bytes.WrittenSpan.ToArray();
    }

    private static LossFrame OriginalNativeHeldLoss()
        => new(BoundDescriptorCodec.Clientbound(Protocol, "container_set_slot"), OriginalNativeBody(),
            packet => packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: LostRevision, Slot: 36 });

    private static byte[] OriginalNativeBody()
    {
        byte[] original = Convert.FromBase64String(NativeHeldBase64);
        Assert.Equal(21, original.Length);
        Assert.Equal(NativeHeldSha256, Convert.ToHexStringLower(SHA256.HashData(original)));
        Assert.Equal(new byte[] { 0, LostRevision, 0, 36 }, original[..4]);
        return original;
    }

    private static LossFrame ConstructedLoss(string kind)
    {
        // Reuse only the original valid native component stack. Every changed header, full-content composition and target below is a constructed managed witness.
        byte[] stack = OriginalNativeBody()[4..];
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        string identifier;
        Func<object?, bool> matches;
        if (kind is "non-held-slot" or "closed-window" or "cursor-container")
        {
            int container = kind == "closed-window" ? 5 : kind == "cursor-container" ? -1 : 0;
            int slot = kind == "non-held-slot" ? 10 : kind == "cursor-container" ? 12 : 54;
            writer.WriteVarInt(container);
            writer.WriteVarInt(LostRevision);
            writer.WriteShort((short)slot);
            writer.WriteBytes(stack);
            identifier = "container_set_slot";
            matches = packet => packet is ClientboundContainerSetSlotPacket update
                && update.ContainerId == container && update.StateId == LostRevision && update.Slot == slot;
        }
        else if (kind == "contents-non-held")
        {
            writer.WriteVarInt(0);
            writer.WriteVarInt(LostRevision);
            writer.WriteVarInt(46);
            for (int slot = 0; slot < 46; slot++)
                if (slot == FenceSlot)
                    writer.WriteBytes(stack);
                else
                    writer.WriteVarInt(0);
            writer.WriteVarInt(0);
            identifier = "container_set_content";
            matches = packet => packet is ClientboundContainerSetContentPacket { ContainerId: 0, StateId: LostRevision };
        }
        else if (kind == "direct-inventory-zero")
        {
            writer.WriteVarInt(0);
            writer.WriteBytes(stack);
            identifier = "set_player_inventory";
            matches = packet => packet is ClientboundSetPlayerInventoryPacket { Slot: 0 };
        }
        else
        {
            Assert.Equal("cursor-packet", kind);
            writer.WriteBytes(stack);
            identifier = "set_cursor_item";
            matches = packet => packet is ClientboundSetCursorItemPacket;
        }
        return new LossFrame(BoundDescriptorCodec.Clientbound(Protocol, identifier), bytes.WrittenSpan.ToArray(), matches);
    }

    private sealed record LossFrame(BoundPacketCodec Codec, byte[] Body, Func<object?, bool> MatchesPacket);
}
