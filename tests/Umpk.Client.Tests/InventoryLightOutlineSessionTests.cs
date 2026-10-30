using System.Buffers;
using System.Security.Cryptography;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;
using Session = Umpk.Client.Tests.ComponentAdventureMiningSessionTests.Session;

namespace Umpk.Client.Tests;

/// <summary>Protocol770 light-block outline knowledge through framed inventory updates and the existing view-mining caller. Native755/777 support is source-only and outside these rows. Public cached block-only raycasts remain best-effort; no original-client input is executed.</summary>
public sealed class InventoryLightOutlineSessionTests
{
    private const int Protocol = 770;
    private const int Window = 5;
    private const int ContainerSlots = 54;
    private const int PlayerMainInContainer = ContainerSlots + 27;
    private const string NativeLossBase64 = "AAcAJAEBAQAMAQAAAAEaAQAAAAAA";
    private const string NativeLossSha256 = "578cc93204fbdc086a8c783fcc0442525635ef4cc4388311ed14bda69ff5e165";
    private static readonly BlockPos Front = new(2, 66, 1);
    private static readonly BlockPos Behind = new(2, 66, 0);
    private static readonly BlockPos OffRay = new(4, 66, 1);

    [Theory]
    [InlineData("diamond_pickaxe", false)]
    [InlineData("light", true)]
    public async Task UnknownMainCannotChooseEitherCachedLightOutline(string cachedMain, bool cachedFrontHit)
    {
        await using Session session = await CreateAsync(cachedMain, "diamond_pickaxe");
        await SendLossAsync(session, 36);
        await AssertCachedViewAsync(session, cachedFrontHit ? Front : Behind);
        await AssertViewRequestAsync(session, cachedFrontHit ? Front : Behind, allowed: false);
    }

    [Theory]
    [InlineData("diamond_pickaxe", "light", true)]
    [InlineData("light", "diamond_pickaxe", false)]
    public async Task KnownPlayerHandOutlineIgnoresContainerLocalSlot(string playerItem, string containerItem, bool seesBehind)
    {
        await using Session session = await CreateAsync(playerItem, containerItem);
        await AssertCachedViewAsync(session, seesBehind ? Behind : Front);
        // Request only the unbreakable rear block: the other known hand context must release the view request without attacking the front light block.
        await AssertViewRequestAsync(session, Behind, seesBehind);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(45)]
    public async Task NonparticipatingLossDoesNotInvalidateKnownMainOutline(int lostPlayerSlot)
    {
        await using Session session = await CreateAsync("diamond_pickaxe", "light");
        await SendLossAsync(session, lostPlayerSlot);
        Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36));
        Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(lostPlayerSlot));
        await AssertCachedViewAsync(session, Behind);
        await AssertViewRequestAsync(session, Behind, allowed: true);
    }

    [Fact]
    public async Task OrdinaryFrontHitIgnoresOccludedAndOffRayLightDespiteUnknownMain()
    {
        await using Session session = await CreateAsync("diamond_pickaxe", "light", ordinaryFront: true);
        await SendLossAsync(session, 36);
        await AssertCachedViewAsync(session, Front);
        // A block-only view can be definite while mining still requires an available tool. Restore that separate input after queue admission, before supplying any player tick.
        await AssertViewRequestAsync(session, Front, allowed: true, restoreMainBeforeTick: true);
    }

    [Fact]
    public async Task ActiveInvisibleViewLossEndsCapturedMiningWithOneAbort()
    {
        await using Session session = await CreateAsync("diamond_pickaxe", "light");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockInViewAsync(Behind, ct: caller.Token);
        bool started = false;
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.False(action.IsCompleted);
            AssertStartGroup(await session.Tick(), Behind);
            started = true;
            await SendLossAsync(session, 36);
            var abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(await session.Tick()));
            AssertAbort(abort, Behind);
            started = false;
            InvalidOperationException failure = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => action.WaitAsync(session.Token));
            Assert.Equal("The held-item state is unavailable.", failure.Message);
            Assert.Empty(await session.Tick());
            Assert.Empty(await session.Tick());
            AssertWorldUnchanged(session, ordinaryFront: false);
        }
        finally
        {
            await caller.CancelAsync();
            List<IPacket> cleanup = await session.Tick();
            if (started)
                AssertAbort(Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(cleanup)), Behind);
            else
                Assert.Empty(cleanup);
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException error) when (error.Message == "The held-item state is unavailable.") { }
        }
    }

    private static async Task<Session> CreateAsync(string playerItem, string containerItem, bool ordinaryFront = false)
    {
        Session session = await Session.Create(Protocol);
        try
        {
            await session.Mode(GameMode.Survival, instantBuild: false);
            await session.Info(GameMode.Survival);
            await session.Block(Front.Below(), "stone");
            await session.Block(Front, ordinaryFront ? "bedrock" : "light");
            await session.Block(Behind, ordinaryFront ? "light" : "bedrock");
            await session.Block(OffRay, "light");
            ItemStack player = Stack(session, playerItem), container = Stack(session, containerItem);
            await SendKnownAndWaitAsync(session, new ClientboundContainerSetSlotPacket(0, 1, 36, player), p => p is ClientboundContainerSetSlotPacket { ContainerId: 0, Slot: 36, StateId: 1 });
            await SendKnownAndWaitAsync(session, new ClientboundContainerSetSlotPacket(0, 1, 45, ItemStack.Empty), p => p is ClientboundContainerSetSlotPacket { ContainerId: 0, Slot: 45, StateId: 1 });
            int menu = session.Client.State.Registries!.MenuTypes[Identifier.Minecraft("generic_9x6")].NetworkId;
            await SendKnownAndWaitAsync(session, new ClientboundOpenScreenPacket(Window, menu, Component.Text("Light outline"), null, 0, null), p => p is ClientboundOpenScreenPacket { ContainerId: Window });
            var items = new ItemStack[ContainerSlots + 36];
            Array.Fill(items, ItemStack.Empty);
            items[36] = container;
            items[PlayerMainInContainer] = player;
            await SendKnownAndWaitAsync(session, new ClientboundContainerSetContentPacket(Window, 2, items, ItemStack.Empty), p => p is ClientboundContainerSetContentPacket { ContainerId: Window, StateId: 2 });
            var self = await session.Client.Snapshots.SelfAsync(session.Token);
            Assert.InRange(self.EyePosition.Y - Front.Y, .25, .75);
            Assert.Equal(2.5, self.EyePosition.X);
            Assert.Equal(2.5, self.EyePosition.Z);
            await session.Client.Actions.Movement.LookAtLocalAsync(new Vec3d(2.5, self.EyePosition.Y, .5), session.Token);
            await session.Client.InvokeAsync(client =>
            {
                Assert.Equal(0, client.State.Self.HeldSlot);
                Assert.Equal(player, client.State.Inventory.PlayerSlots[36]);
                Assert.Equal(player, client.State.Inventory.GetSlot(PlayerMainInContainer));
                Assert.Equal(container, client.State.Inventory.GetSlot(36));
                Assert.True(client.State.Inventory.PlayerSlots[45].IsEmpty);
                Assert.True(client.State.Inventory.IsPlayerSlotAvailable(36));
                Assert.True(client.State.Inventory.IsPlayerSlotAvailable(45));
                BlockDefinition light = client.State.Registries!.Blocks[Identifier.Minecraft("light")].Value;
                Assert.True(light.TryGetPropertyValue(light.DefaultStateId, "level", out string level) && level == "15");
                Assert.True(light.TryGetPropertyValue(light.DefaultStateId, "waterlogged", out string waterlogged) && waterlogged == "false");
                Assert.False(client.State.World.GetBlock(ordinaryFront ? Behind : Front).IsAir);
                var eye = client.SelfEyePosition;
                var end = new Vec3d(eye.X, eye.Y, eye.Z - 4.5);
                var shapes = JavaGameData.BlockShapes(Protocol);
                BlockHitResult ordinary = Raycast.CastBlockWithContext(client.State.World, eye, end, false, shapes, Identifier.Minecraft("air"));
                BlockHitResult holding = Raycast.CastBlockWithContext(client.State.World, eye, end, false, shapes, Identifier.Minecraft("light"));
                Assert.True(ordinary.Hit && holding.Hit);
                Assert.Equal(ordinaryFront ? Front : Behind, ordinary.BlockPos);
                Assert.Equal(Front, holding.BlockPos);
                Assert.Equal(Direction.South, ordinary.Face);
                Assert.Equal(Direction.South, holding.Face);
                return true;
            }, session.Token);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task AssertViewRequestAsync(Session session, BlockPos target, bool allowed, bool restoreMainBeforeTick = false, bool scaffoldingBehind = false)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockInViewAsync(target, ct: caller.Token);
        bool started = false;
        Exception? primaryFailure = null;
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            if (!allowed)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.WaitAsync(TimeSpan.FromSeconds(2), session.Token));
                Assert.True(action.IsCompleted, "An uncertain light outline must end the view request before a player tick rather than treating cached item knowledge as current.");
                Assert.Empty(await session.Tick());
                Assert.Empty(await session.Tick());
            }
            else
            {
                Assert.False(action.IsCompleted, "An item-independent first hit must remain usable even while a later mining input needs authoritative recovery.");
                if (restoreMainBeforeTick)
                {
                    Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(36));
                    await SendKnownAndWaitAsync(session, new ClientboundContainerSetSlotPacket(0, 3, 36, Stack(session, "diamond_pickaxe")), p => p is ClientboundContainerSetSlotPacket { ContainerId: 0, Slot: 36, StateId: 3 });
                    Assert.True(session.Client.State.Inventory.IsPlayerSlotAvailable(36));
                }
                AssertStartGroup(await session.Tick(), target);
                started = true;
                Assert.False(action.IsCompleted);
                AssertMainSwing(Assert.Single(await session.Tick()));
            }
            AssertWorldUnchanged(session, ordinaryFront: restoreMainBeforeTick, scaffoldingBehind: scaffoldingBehind);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
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
                await caller.CancelAsync();
                List<IPacket> cleanup = await session.Tick();
                if (started)
                    AssertAbort(Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(cleanup)), target);
                else
                    Assert.Empty(cleanup);
                try { await action.WaitAsync(session.Token); }
                catch (OperationCanceledException) { }
                Assert.Empty(await session.Tick());
            }
            catch (Exception cleanupFailure) when (primaryFailure is not null)
            {
                throw new AggregateException("Both the outline assertion and owned request cleanup failed.", primaryFailure, cleanupFailure);
            }
        }
    }

    private static async Task AssertCachedViewAsync(Session session, BlockPos expected)
    {
        var hit = Assert.IsType<BlockHitResult>(await session.Client.Snapshots.RaycastViewAsync(ct: session.Token));
        Assert.True(hit.Hit);
        Assert.Equal(expected, hit.BlockPos);
        Assert.Equal(Direction.South, hit.Face);
    }

    private static void AssertWorldUnchanged(Session session, bool ordinaryFront, bool scaffoldingBehind = false)
    {
        Assert.Equal(Identifier.Minecraft(ordinaryFront ? "bedrock" : "light"), session.Client.State.World.GetBlock(Front).Block.Id);
        Assert.Equal(Identifier.Minecraft(scaffoldingBehind ? "scaffolding" : ordinaryFront ? "light" : "bedrock"), session.Client.State.World.GetBlock(Behind).Block.Id);
        Assert.Equal(Identifier.Minecraft("light"), session.Client.State.World.GetBlock(OffRay).Block.Id);
    }

    private static void AssertStartGroup(List<IPacket> sent, BlockPos target)
        => Assert.Collection(sent, packet =>
        {
            var start = Assert.IsType<ServerboundPlayerActionPacket>(packet);
            Assert.Equal(0, start.Action);
            Assert.Equal(target, start.Position);
            Assert.Equal((byte)Direction.South, start.Direction);
            Assert.True(Assert.IsType<int>(start.Sequence) > 0);
        }, AssertMainSwing, AssertMainSwing);

    private static void AssertAbort(ServerboundPlayerActionPacket abort, BlockPos target)
    {
        Assert.Equal(1, abort.Action);
        Assert.Equal(target, abort.Position);
        Assert.Equal((byte)Direction.Down, abort.Direction);
        Assert.Equal(0, abort.Sequence);
    }

    private static void AssertMainSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.True(swing.HasHand);
        Assert.Equal(0, swing.Hand);
    }

    private static ItemStack Stack(Session session, string name)
        => new(session.Client.State.Registries!.Items[Identifier.Minecraft(name)], 1);

    private static async Task SendLossAsync(Session session, int playerSlot)
    {
        byte[] native = Convert.FromBase64String(NativeLossBase64);
        Assert.Equal(21, native.Length);
        Assert.Equal(NativeLossSha256, Convert.ToHexStringLower(SHA256.HashData(native)));
        Assert.Equal(new byte[] { 0, 7, 0, 36 }, native[..4]);
        byte[] body = native.ToArray();
        if (playerSlot != 36)
        {
            // Constructed header witness: only the menu-slot header changes; the original native component payload remains byte-identical. It is not attributed to a native offhand/unrelated-slot capture.
            body[2] = (byte)(playerSlot >> 8);
            body[3] = (byte)playerSlot;
            Assert.Equal(native[4..], body[4..]);
        }
        else
            Assert.Equal(native, body);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, "container_set_slot");
        int raw = 0, applied = 0;
        void Observe(PacketObservation frame)
        {
            if (frame.Flow == PacketFlow.Clientbound && frame.Phase == ProtocolPhase.Play && frame.WireId == codec.WireId && frame.RawPayload.SequenceEqual(body))
                Interlocked.Increment(ref raw);
        }
        using IDisposable decoded = session.Client.Events.Subscribe<PacketReceived>(message =>
        {
            if (message.Packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 7 } slot && slot.Slot == playerSlot)
                Interlocked.Increment(ref applied);
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await session.Server.SendFrameAsync(codec.WireId, body, session.Token);
            var fence = new ClientboundGameEventPacket(3, 0);
            await SendKnownAndWaitAsync(session, fence, p => Equals(p, fence));
            Assert.Equal(1, Volatile.Read(ref raw));
            Assert.Equal(0, Volatile.Read(ref applied));
            Assert.False(session.Client.State.Inventory.IsPlayerSlotAvailable(playerSlot));
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static async Task SendKnownAndWaitAsync(Session session, IPacket packet, Func<object?, bool> matches)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        object decoded = codec.Decode(bytes, context);
        Assert.True(matches(decoded));
        Assert.Equal(bytes, Encode(codec, decoded, context));
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable received = session.Client.Events.Subscribe<PacketReceived>(message =>
        {
            if (message.Phase == ProtocolPhase.Play && message.WireId == codec.WireId && matches(message.Packet))
                applied.TrySetResult();
        });
        await session.Server.SendFrameAsync(codec.WireId, bytes, session.Token);
        await applied.Task.WaitAsync(session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        codec.Encode(ref writer, packet, context);
        return bytes.WrittenSpan.ToArray();
    }

    [Fact]
    public async Task MixedLightFrontAndScaffoldingPostRequiresTheLightContext()
    {
        await using Session session = await CreateAsync("diamond_pickaxe", "light");
        await session.Block(Behind, "scaffolding");
        var feet = new Vec3d(2.0625, 65, 2.5);
        var correction = new ClientboundPlayerPositionPacket(feet.X, feet.Y, feet.Z, 0, 0, 0, 99, new PositionMoveRotation(feet, Vec3d.Zero, 0, 0));
        await SendKnownAndWaitAsync(session, correction, packet => packet is ClientboundPlayerPositionPacket { TeleportId: 99, RelativeFlags: 0, ModernValues: { } values }
            && values.Position.Equals(feet) && values.DeltaMovement.Equals(Vec3d.Zero) && values.XRot == 0 && values.YRot == 0);
        var self = await session.Client.Snapshots.SelfAsync(session.Token);
        Assert.Equal(feet.X, self.EyePosition.X);
        Assert.Equal(feet.Z, self.EyePosition.Z);
        await session.Client.Actions.Movement.LookAtLocalAsync(new Vec3d(feet.X, self.EyePosition.Y, .5), session.Token);
        await session.Client.InvokeAsync(client =>
        {
            var eye = client.SelfEyePosition;
            Assert.InRange(eye.X - Behind.X, .03125, .09375);
            Assert.InRange(eye.Y - Behind.Y, .25, .75);
            var end = new Vec3d(eye.X, eye.Y, eye.Z - 4.5);
            var shapes = JavaGameData.BlockShapes(Protocol);
            BlockHitResult ordinary = Raycast.CastBlockWithContext(client.State.World, eye, end, false, shapes, Identifier.Minecraft("air"));
            BlockHitResult scaffolding = Raycast.CastBlockWithContext(client.State.World, eye, end, false, shapes, Identifier.Minecraft("scaffolding"));
            BlockHitResult light = Raycast.CastBlockWithContext(client.State.World, eye, end, false, shapes, Identifier.Minecraft("light"));
            Assert.True(ordinary.Hit && scaffolding.Hit && light.Hit);
            Assert.Equal(Behind, ordinary.BlockPos);
            Assert.Equal(ordinary, scaffolding);
            Assert.Equal(Front, light.BlockPos);
            Assert.NotEqual(ordinary, light);
            Assert.Equal(Direction.South, ordinary.Face);
            Assert.Equal(Direction.South, light.Face);
            return true;
        }, session.Token);
        // The corner-post ordinary/scaffolding results are equal. A nearer LIGHT context must still make the view unknown; this is an expected third RED on the current owner, not an already-unknown mixed control.
        await SendLossAsync(session, 36);
        await AssertCachedViewAsync(session, Behind);
        await AssertViewRequestAsync(session, Behind, allowed: false, scaffoldingBehind: true);
    }
}
