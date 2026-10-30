using System.Buffers;
using System.Security.Cryptography;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;
using Session = Umpk.Client.Tests.ComponentAdventureMiningSessionTests.Session;

namespace Umpk.Client.Tests;

/// <summary>Eight managed placement callers with source-bound item participation. Main36 loss uses the original native payload; other slot headers are constructed witnesses.</summary>
public sealed class InventoryDecodeLossPlacementSessionTests
{
    private const int Protocol = 770;
    private const string NativeBase64 = "AAcAJAEBAQAMAQAAAAEaAQAAAAAA";
    private const string NativeSha256 = "578cc93204fbdc086a8c783fcc0442525635ef4cc4388311ed14bda69ff5e165";
    private const long UseBarrier = 0x706C616365;

    [Theory]
    [InlineData("selected-main-native-loss")]
    [InlineData("unknown-main-button-independent-activation")]
    [InlineData("selected-offhand-constructed-loss")]
    [InlineData("known-main-unrelated-slot-loss")]
    [InlineData("sneaking-unknown-other-hand")]
    [InlineData("known-main-axe-unknown-offhand-transform")]
    [InlineData("known-main-axe-sneaking-unknown-offhand-transform")]
    [InlineData("known-main-axe-unknown-offhand-nontransform")]
    public async Task InventoryKnowledgeControlsPredictionWithoutInventingAUseOutcome(string scene)
    {
        await using Session session = await Session.Create(Protocol);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        await Slot(session, 36, ItemStack.Empty);
        await Slot(session, 45, ItemStack.Empty);
        bool offhand = scene == "selected-offhand-constructed-loss";
        bool known = scene == "known-main-unrelated-slot-loss";
        bool independentButton = scene == "unknown-main-button-independent-activation";
        bool axeContext = scene.StartsWith("known-main-axe-", StringComparison.Ordinal);
        bool axeSneaking = scene == "known-main-axe-sneaking-unknown-offhand-transform";
        bool knownPass = scene == "known-main-axe-unknown-offhand-nontransform";
        bool knownSuccess = known || independentButton || axeSneaking;
        bool itemDependent = offhand || scene == "selected-main-native-loss" || axeContext;
        bool sneaking = scene == "sneaking-unknown-other-hand" || axeSneaking;
        Hand hand = offhand ? Hand.Off : Hand.Main;
        short lostSlot = offhand || sneaking || axeContext ? (short)45 : known ? (short)10 : (short)36;
        await session.Block(Session.Target, knownPass ? "stone" : itemDependent ? "oak_log" : "stone_button");
        Vec3d cursor = itemDependent ? new(.5, 1, .5) : new(.5, .625, .9375);
        if (itemDependent)
            await Slot(session, offhand ? (short)45 : (short)36, new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_axe")], 1));
        if (sneaking)
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.True(await session.Client.InvokeAsync(client => client.State.Self.Sneaking, session.Token));
        }
        Assert.Empty(await ReadThroughUseBarrier(session));
        if (!itemDependent)
        {
            var button = session.Client.State.World.GetBlock(Session.Target);
            Assert.True(button.TryGetProperty("face", out string face));
            Assert.True(button.TryGetProperty("facing", out string facing));
            Assert.True(button.TryGetProperty("powered", out string powered));
            Assert.Equal(("wall", "north", "false"), (face, facing, powered));
        }

        BlockUsePrediction before = await Preview(session, Session.Target, cursor, hand);
        Assert.Equal(knownPass ? BlockUsePrediction.Pass : BlockUsePrediction.Success(localSwing: false), before);
        ItemStack cachedSelected = session.Client.State.Inventory.PlayerSlots[hand == Hand.Main ? 36 : 45];
        if (itemDependent)
            Assert.Equal(Identifier.Minecraft("diamond_axe"), cachedSelected.Item.Id);
        else
            Assert.True(cachedSelected.IsEmpty);
        if (independentButton)
        {
            Assert.False(await session.Client.InvokeAsync(client => client.State.Self.Sneaking, session.Token));
            Assert.True(await session.Client.InvokeAsync(client => client.State.EnabledFeatures.IsUnobservedItemEnabled(client.State.Registries, Protocol) is true, session.Token));
        }
        await DeliverLossAndFence(session, lostSlot, originalNative: scene == "selected-main-native-loss" || independentButton);
        Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(lostSlot));
        Assert.Equal(known || sneaking || offhand || axeContext, session.Client.State.Inventory.IsPlayerSlotAvailable(36));
        Assert.Equal(!offhand && !sneaking && !axeContext, session.Client.State.Inventory.IsPlayerSlotAvailable(45));
        ItemStack[] stacks = session.Client.State.Inventory.PlayerSlots.ToArray();
        int food = session.Client.State.Self.Food;
        float saturation = session.Client.State.Self.Saturation;
        int observedState = session.Client.State.World.GetBlock(Session.Target).StateId;
        BlockUsePrediction prediction = await Preview(session, Session.Target, cursor, hand);
        Assert.Equal(knownPass ? BlockUsePrediction.Pass : knownSuccess ? BlockUsePrediction.Success(localSwing: false) : BlockUsePrediction.Unknown, prediction);
        Assert.False(await HasShadow(session));

        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, cursor, hand, ct: caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> sent = await ReadThroughUseBarrier(session);
            Assert.Equal(knownSuccess ? 2 : 1, sent.Count);
            var use = Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
            Assert.Equal((Session.Target, (int)Direction.Up, (int)hand), (use.Position, use.Face, use.Hand));
            Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.False(use.Inside);
            Assert.False(use.WorldBorderHit);
            Assert.False(action.IsCompleted);
            if (knownSuccess)
            {
                var swing = Assert.IsType<ServerboundSwingPacket>(sent[1]);
                Assert.True(swing.HasHand);
                Assert.Equal((int)Hand.Main, swing.Hand);
                if (!axeContext)
                {
                    Assert.True(await HasShadow(session));
                    Assert.Equal("true", await session.Client.InvokeAsync(client =>
                    {
                        var local = client.State.JukeboxPrediction.GetBlock(client.State.World, Session.Target);
                        Assert.True(local.TryGetProperty("powered", out string value));
                        return value;
                    }, session.Token));
                }
            }
            else
                Assert.False(await HasShadow(session));
            Assert.Equal(observedState, session.Client.State.World.GetBlock(Session.Target).StateId);
            if (!axeContext || !knownSuccess)
                Assert.Equal(stacks, session.Client.State.Inventory.PlayerSlots);
            Assert.Equal(food, session.Client.State.Self.Food);
            Assert.Equal(saturation, session.Client.State.Self.Saturation);
            await SendKnownAndWait(session, new ClientboundBlockChangedAckPacket(use.Sequence),
                packet => packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == use.Sequence);
            await action.WaitAsync(session.Token);
            Assert.Empty(await ReadThroughUseBarrier(session));
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await action.WaitAsync(session.Token));
        }

        if (scene == "selected-main-native-loss")
        {
            // This known native border rejection precedes the inventory-dependent prediction phase even while main36 remains unavailable.
            BlockPos outside = new(30_000_000, Session.Target.Y, Session.Target.Z);
            Assert.False(session.Client.State.World.Border.IsWithinBounds(outside.X, outside.Z,
                Umpk.Game.World.WorldBorderState.ContainmentEraForProtocol(Protocol)));
            Assert.Equal(BlockUsePrediction.Rejected, await Preview(session, outside, cursor, hand));
            await session.Client.Actions.Interaction.PlaceBlockAsync(outside, Direction.Up, cursor, hand, ct: session.Token);
            Assert.Empty(await ReadThroughUseBarrier(session));
        }
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }

    private static Task<BlockUsePrediction> Preview(Session session, BlockPos target, Vec3d cursor, Hand hand)
        => session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(target, Direction.Up, cursor, hand), session.Token);

    private static Task<bool> HasShadow(Session session)
        => session.Client.InvokeAsync(client => client.State.JukeboxPrediction.TryProject(client.State.World, Session.Target,
            client.State.World.GetBlock(Session.Target), out _), session.Token);

    private static Task Slot(Session session, short slot, ItemStack stack)
        => SendKnownAndWait(session, new ClientboundContainerSetSlotPacket(0, 6, slot, stack),
            packet => packet is ClientboundContainerSetSlotPacket update && update.ContainerId == 0 && update.StateId == 6 && update.Slot == slot);

    private static async Task DeliverLossAndFence(Session session, short slot, bool originalNative)
    {
        byte[] body = Convert.FromBase64String(NativeBase64);
        Assert.Equal(21, body.Length);
        Assert.Equal(NativeSha256, Convert.ToHexStringLower(SHA256.HashData(body)));
        if (originalNative)
            Assert.Equal(36, slot);
        else
        {
            body[2] = (byte)(slot >> 8);
            body[3] = (byte)slot;
            Assert.Equal(Convert.FromBase64String(NativeBase64)[4..], body[4..]);
        }
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, "container_set_slot");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        Assert.Throws<UnmodeledItemComponentException>(() => codec.Decode(body, context));
        int rawCount = 0;
        int decodedCount = 0;
        void Observe(PacketObservation frame)
        {
            if (frame.Phase == ProtocolPhase.Play && frame.Flow == PacketFlow.Clientbound
                && frame.WireId == codec.WireId && frame.RawPayload.SequenceEqual(body))
                Interlocked.Increment(ref rawCount);
        }
        using IDisposable subscription = session.Client.Events.Subscribe<PacketReceived>(received =>
        {
            if (received.Packet is ClientboundContainerSetSlotPacket update && update.ContainerId == 0 && update.StateId == 7 && update.Slot == slot)
                Interlocked.Increment(ref decodedCount);
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await session.Server.SendFrameAsync(codec.WireId, body, session.Token);
            ItemStack fence = new(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], 2);
            await SendKnownAndWait(session, new ClientboundContainerSetSlotPacket(0, 8, 9, fence),
                packet => packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 8, Slot: 9, Item.Count: 2 });
            Assert.Equal(1, Volatile.Read(ref rawCount));
            Assert.Equal(0, Volatile.Read(ref decodedCount));
            Assert.Equal(fence, session.Client.State.Inventory.GetSlot(9));
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    // Session's mining barrier discards use_item_on. This local collector reads that real frame, validates every decoded outbound frame, and keeps use/swing/action order through an ordinary keep-alive fence.
    private static async Task<List<IPacket>> ReadThroughUseBarrier(Session session)
    {
        await SendKnown(session, new ClientboundPlayKeepAlivePacket(UseBarrier));
        Assert.True(Umpk.Data.Java.JavaVersions.TryGetByProtocol(Protocol, out JavaVersion version));
        var registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        List<IPacket> result = [];
        while (true)
        {
            InboundFrame frame = await session.Server.NextFrameAsync(session.Token);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            var packet = Assert.IsAssignableFrom<IPacket>(codec.Decode(frame.Payload, context));
            Assert.Equal(frame.Payload.ToArray(), Encode(codec, packet, context));
            if (packet is ServerboundPlayKeepAlivePacket { Id: UseBarrier })
                return result;
            if (packet is ServerboundUseItemOnPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundPlayerActionPacket)
                result.Add(packet);
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
}
