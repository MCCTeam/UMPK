using System.Buffers;
using System.Security.Cryptography;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Session = Umpk.Client.Tests.ComponentAdventureMiningSessionTests.Session;

namespace Umpk.Client.Tests;

/// <summary>Four managed wrapper callers using real bound frames and a separate scripted server witness. These do not run a native server.</summary>
public sealed class InventoryDecodeLossUseBlockVerifiedSessionTests
{
    private const int Protocol = 770;
    private const string NativeBase64 = "AAcAJAEBAQAMAQAAAAEaAQAAAAAA";
    private const string NativeSha256 = "578cc93204fbdc086a8c783fcc0442525635ef4cc4388311ed14bda69ff5e165";
    private const long SetupFence = 0x7772617001;
    private const long CompleteFence = 0x7772617002;
    private static readonly BlockPos Witness = new(1, 65, 2);

    [Theory]
    [InlineData("sneaking-full-loss-empty-main")]
    [InlineData("known-sneaking-axe-unrelated-loss")]
    [InlineData("known-sneaking-empty-button")]
    [InlineData("unsneaking-native-main-loss-log")]
    public async Task CachedPreflightCannotReplaceAnUnknownHandWithAnObservedRefusal(string scene)
    {
        await using Session session = await Session.Create(Protocol);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        Assert.Equal(0, session.Client.State.Self.HeldSlot);
        bool fullLoss = scene == "sneaking-full-loss-empty-main";
        bool blocked = scene == "known-sneaking-axe-unrelated-loss";
        bool knownEmpty = scene == "known-sneaking-empty-button";
        bool unsneaking = scene == "unsneaking-native-main-loss-log";
        ItemStack main = knownEmpty ? ItemStack.Empty
            : new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_axe")], 1);
        await Slot(session, 36, main);
        await Slot(session, 45, ItemStack.Empty);
        await SetBlock(session, Session.Target, unsneaking ? "oak_log" : "stone_button");
        await SetBlock(session, Witness, "iron_door");
        Assert.True(session.Client.State.World.GetBlock(Witness).TryGetProperty("open", out string initialOpen));
        Assert.Equal("false", initialOpen);
        int openState = StateWithProperty(session, "iron_door", "open", "true");
        await session.Client.Actions.Movement.SetSneakingAsync(!unsneaking, session.Token);
        Assert.Equal(!unsneaking, await session.Client.InvokeAsync(client => client.State.Self.Sneaking, session.Token));
        List<IPacket> setup = await DrainSetup(session);
        Assert.DoesNotContain(setup, packet => packet is ServerboundUseItemOnPacket or ServerboundUseItemPacket
            or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundPlayerActionPacket);
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36));
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(45));

        if (fullLoss)
            await DeliverLossAndFence(session, FullContentsLoss(session));
        else if (blocked)
            await DeliverLossAndFence(session, SlotLoss(10, originalNative: false));
        else if (unsneaking)
            await DeliverLossAndFence(session, SlotLoss(36, originalNative: true));

        Assert.Equal(main, session.Client.State.Inventory.PlayerSlots[36]);
        Assert.True(session.Client.State.Inventory.PlayerSlots[45].IsEmpty);
        Assert.Equal(blocked || knownEmpty, session.Client.State.Inventory.IsPlayerSlotAvailable(36));
        Assert.Equal(!fullLoss, session.Client.State.Inventory.IsPlayerSlotAvailable(45));
        if (blocked)
            Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(10));
        if (fullLoss || unsneaking)
        {
            Assert.True(await session.Client.InvokeAsync(client => client.State.EnabledFeatures.IsUnobservedItemEnabled(client.State.Registries, Protocol) is true, session.Token));
            Assert.Equal(BlockUsePrediction.Unknown, await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(
                Session.Target, Direction.Up, new Vec3d(.5, .5, .5), Hand.Main), session.Token));
        }
        if (knownEmpty)
            Assert.Equal(BlockUsePrediction.Success(localSwing: false), await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(
                Session.Target, Direction.Up, new Vec3d(.5, .5, .5), Hand.Main), session.Token));

        ItemStack[] cached = session.Client.State.Inventory.PlayerSlots.ToArray();
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        long tick = session.Client.State.SessionTick;
        Assert.False(await HasTargetShadow(session));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        var firstUse = new TaskCompletionSource<ServerboundUseItemOnPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<List<IPacket>> peer = ReadToFence(session, CompleteFence, firstUse);
        Task<UseBlockOutcome> action = session.Client.Actions.Interaction.UseBlockVerifiedAsync(
            Session.Target, Witness, "open", "true", confirmationWindow: TimeSpan.FromSeconds(1), ct: caller.Token);
        try
        {
            if (blocked)
                Assert.Equal(UseBlockOutcome.NotUsed, await action.WaitAsync(session.Token));
            else
            {
                // The peer never sends an ACK or witness until a real use arrives. An early wrapper result therefore wins this race on the stale-cache regression.
                Task winner = await Task.WhenAny(firstUse.Task, action, peer).WaitAsync(session.Token);
                if (winner == peer)
                    await peer;
                UseBlockOutcome? earlyOutcome = action.IsCompletedSuccessfully ? await action : null;
                Assert.True(winner == firstUse.Task, $"Wrapper completed before the peer received a use: {earlyOutcome}.");
                ServerboundUseItemOnPacket use = await firstUse.Task.WaitAsync(session.Token);
                Assert.False(action.IsCompleted);
                Assert.Equal((Session.Target, (int)Direction.Up, (int)Hand.Main), (use.Position, use.Face, use.Hand));
                Assert.Equal((.5f, .5f, .5f), (use.CursorX, use.CursorY, use.CursorZ));
                Assert.False(use.Inside);
                Assert.False(use.WorldBorderHit);
                Assert.Equal("false", OpenValue(session));
                if (!knownEmpty)
                    Assert.False(await HasTargetShadow(session));

                // This is an independent scripted confirmation of the wrapper contract. In particular, a log use is not claimed to open a native iron door.
                await SendKnownAndWait(session, new ClientboundBlockUpdatePacket(Witness, openState),
                    packet => packet is ClientboundBlockUpdatePacket update && update.Position == Witness && update.BlockStateId == openState);
                Assert.Equal("true", OpenValue(session));
                Assert.Equal(knownEmpty, await HasTargetShadow(session));
                if (knownEmpty)
                    Assert.Equal("true", await session.Client.InvokeAsync(client =>
                    {
                        var projected = client.State.JukeboxPrediction.GetBlock(client.State.World, Session.Target);
                        Assert.True(projected.TryGetProperty("powered", out string powered));
                        return powered;
                    }, session.Token));
                Assert.False(action.IsCompleted);
                await SendKnownAndWait(session, new ClientboundBlockChangedAckPacket(use.Sequence),
                    packet => packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == use.Sequence);
                Assert.Equal(UseBlockOutcome.Used, await action.WaitAsync(session.Token));
            }

            await SendKnown(session, new ClientboundPlayKeepAlivePacket(CompleteFence));
            List<IPacket> sent = await peer.WaitAsync(session.Token);
            if (blocked)
            {
                Assert.False(firstUse.Task.IsCompleted);
                Assert.Empty(sent);
                Assert.Equal("false", OpenValue(session));
            }
            else
            {
                Assert.Equal(knownEmpty ? 2 : 1, sent.Count);
                Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
                if (knownEmpty)
                {
                    var swing = Assert.IsType<ServerboundSwingPacket>(sent[1]);
                    Assert.True(swing.HasHand);
                    Assert.Equal((int)Hand.Main, swing.Hand);
                }
            }
            Assert.Equal(cached, session.Client.State.Inventory.PlayerSlots);
            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.False(await HasTargetShadow(session));
            Assert.Equal(tick, session.Client.State.SessionTick);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally
        {
            await caller.CancelAsync();
            try
            {
                await action.WaitAsync(session.Token);
            }
            catch (OperationCanceledException) when (caller.IsCancellationRequested)
            {
            }
            if (!peer.IsCompleted)
                await SendKnown(session, new ClientboundPlayKeepAlivePacket(CompleteFence));
            await peer.WaitAsync(session.Token);
        }
    }

    private static byte[] NativeBody()
    {
        byte[] body = Convert.FromBase64String(NativeBase64);
        Assert.Equal(21, body.Length);
        Assert.Equal(NativeSha256, Convert.ToHexStringLower(SHA256.HashData(body)));
        Assert.Equal(new byte[] { 0, 7, 0, 36 }, body[..4]);
        return body;
    }

    private static LossFrame FullContentsLoss(Session session)
    {
        // Only this embedded 17-byte stack is native. The complete 46-slot snapshot is constructed, with selected main36 and off45 explicitly EMPTY.
        byte[] stack = NativeBody()[4..];
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        writer.WriteVarInt(0);
        writer.WriteVarInt(7);
        writer.WriteVarInt(46);
        for (int slot = 0; slot < 46; slot++)
            if (slot == 10)
                writer.WriteBytes(stack);
            else
                writer.WriteVarInt(0);
        writer.WriteVarInt(0);
        byte[] body = output.WrittenSpan.ToArray();
        Assert.Equal(66, body.Length);
        Assert.Equal(new byte[] { 0, 7, 46 }, body[..3]);
        Assert.Equal(stack, body[13..30]);
        Assert.Equal(0, body[55]);
        Assert.Equal(0, body[64]);
        Assert.Equal(0, body[65]);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, "container_set_content");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        ItemStack[] empty = Enumerable.Repeat(ItemStack.Empty, 46).ToArray();
        byte[] valid = Encode(codec, new ClientboundContainerSetContentPacket(0, 7, empty, ItemStack.Empty), context);
        var decoded = Assert.IsType<ClientboundContainerSetContentPacket>(codec.Decode(valid, context));
        Assert.Equal(46, decoded.Items.Count);
        Assert.All(decoded.Items, item => Assert.True(item.IsEmpty));
        Assert.True(decoded.CarriedItem.IsEmpty);
        Assert.Equal(valid, Encode(codec, decoded, context));
        Assert.Equal(valid, body[..13].Concat(new byte[] { 0 }).Concat(body[30..]).ToArray());
        return new LossFrame(codec, body, packet => packet is ClientboundContainerSetContentPacket { ContainerId: 0, StateId: 7 });
    }

    private static LossFrame SlotLoss(short slot, bool originalNative)
    {
        byte[] body = NativeBody();
        if (originalNative)
            Assert.Equal(36, slot);
        else
        {
            body[2] = (byte)(slot >> 8);
            body[3] = (byte)slot;
            Assert.Equal(NativeBody()[4..], body[4..]);
        }
        return new LossFrame(BoundDescriptorCodec.Clientbound(Protocol, "container_set_slot"), body,
            packet => packet is ClientboundContainerSetSlotPacket update && update.ContainerId == 0 && update.StateId == 7 && update.Slot == slot);
    }

    private static async Task DeliverLossAndFence(Session session, LossFrame loss)
    {
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        Assert.Throws<UnmodeledItemComponentException>(() => loss.Codec.Decode(loss.Body, context));
        ItemStack[] cached = session.Client.State.Inventory.PlayerSlots.ToArray();
        int rawCount = 0;
        int decodedCount = 0;
        void Observe(PacketObservation frame)
        {
            if (frame.Phase == ProtocolPhase.Play && frame.Flow == PacketFlow.Clientbound
                && frame.WireId == loss.Codec.WireId && frame.RawPayload.SequenceEqual(loss.Body))
                Interlocked.Increment(ref rawCount);
        }
        using IDisposable subscription = session.Client.Events.Subscribe<PacketReceived>(received =>
        {
            if (loss.MatchesPacket(received.Packet))
                Interlocked.Increment(ref decodedCount);
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await session.Server.SendFrameAsync(loss.Codec.WireId, loss.Body, session.Token);
            ItemStack fence = new(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], 2);
            await SendKnownAndWait(session, new ClientboundContainerSetSlotPacket(0, 8, 9, fence),
                packet => packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 8, Slot: 9, Item.Count: 2 });
            Assert.Equal(1, Volatile.Read(ref rawCount));
            Assert.Equal(0, Volatile.Read(ref decodedCount));
            cached[9] = fence;
            Assert.Equal(cached, session.Client.State.Inventory.PlayerSlots);
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static Task Slot(Session session, short slot, ItemStack stack)
        => SendKnownAndWait(session, new ClientboundContainerSetSlotPacket(0, 6, slot, stack),
            packet => packet is ClientboundContainerSetSlotPacket update && update.ContainerId == 0 && update.StateId == 6 && update.Slot == slot);

    private static Task SetBlock(Session session, BlockPos position, string name)
    {
        int id = session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value.DefaultStateId;
        return SendKnownAndWait(session, new ClientboundBlockUpdatePacket(position, id),
            packet => packet is ClientboundBlockUpdatePacket update && update.Position == position && update.BlockStateId == id);
    }

    private static int StateWithProperty(Session session, string name, string property, string value)
    {
        BlockDefinition definition = session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value;
        IBlockDataSource source = session.Client.State.World.GetBlock(Witness).Source;
        return Assert.Single(Enumerable.Range(definition.MinStateId, definition.StateCount), id => source.TryGetPropertyValue(id, property, out string actual)
            && actual == value && definition.Properties.All(other => other.Name == property || source.TryGetPropertyValue(id, other.Name, out string candidate)
                && source.TryGetPropertyValue(definition.DefaultStateId, other.Name, out string original) && candidate == original));
    }

    private static string OpenValue(Session session)
    {
        Assert.True(session.Client.State.World.GetBlock(Witness).TryGetProperty("open", out string value));
        return value;
    }

    private static Task<bool> HasTargetShadow(Session session)
        => session.Client.InvokeAsync(client => client.State.JukeboxPrediction.TryProject(client.State.World, Session.Target,
            client.State.World.GetBlock(Session.Target), out _), session.Token);

    private static async Task<List<IPacket>> DrainSetup(Session session)
    {
        await SendKnown(session, new ClientboundPlayKeepAlivePacket(SetupFence));
        return await ReadToFence(session, SetupFence, firstUse: null);
    }

    private static async Task<List<IPacket>> ReadToFence(Session session, long fence, TaskCompletionSource<ServerboundUseItemOnPacket>? firstUse)
    {
        Assert.True(Umpk.Data.Java.JavaVersions.TryGetByProtocol(Protocol, out JavaVersion version));
        var registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        List<IPacket> sent = [];
        while (true)
        {
            InboundFrame frame = await session.Server.NextFrameAsync(session.Token);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            var packet = Assert.IsAssignableFrom<IPacket>(codec.Decode(frame.Payload, context));
            Assert.Equal(frame.Payload.ToArray(), Encode(codec, packet, context));
            if (packet is ServerboundPlayKeepAlivePacket { Id: var id } && id == fence)
                return sent;
            sent.Add(packet);
            if (packet is ServerboundUseItemOnPacket use)
                firstUse?.TrySetResult(use);
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
        byte[] body = Encode(codec, packet, context);
        object decoded = codec.Decode(body, context);
        Assert.Equal(body, Encode(codec, decoded, context));
        await session.Server.SendFrameAsync(codec.WireId, body, session.Token);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }

    private sealed record LossFrame(BoundPacketCodec Codec, byte[] Body, Func<object?, bool> MatchesPacket);
}
