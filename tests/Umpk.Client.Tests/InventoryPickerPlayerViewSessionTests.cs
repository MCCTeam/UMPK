using System.Buffers;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;
using Session = Umpk.Client.Tests.ComponentAdventureMiningSessionTests.Session;

namespace Umpk.Client.Tests;

/// <summary>Protocol774 player-view selection through real session callers and bound inventory codecs. Wooden spear uses its vanilla default prototype; no custom component patch is constructed.</summary>
public sealed class InventoryPickerPlayerViewSessionTests
{
    private const int Protocol = 774;
    private const int Window = 5;
    private const int ContainerSlots = 54;
    private const int PlayerMainInContainer = ContainerSlots + 27;

    [Theory]
    [InlineData("wooden_spear", "air", false)]
    [InlineData("diamond_pickaxe", "wooden_spear", true)]
    [InlineData("wooden_spear", "wooden_spear", false)]
    [InlineData("diamond_pickaxe", "air", true)]
    public async Task OpenContainerIndicesCannotReplaceThePlayerItemRangeInput(string playerItem, string containerItem, bool ordinaryPick)
    {
        await using Session session = await Create(playerItem, containerItem);
        await AssertViewCaller(session, ordinaryPick);
    }

    [Theory]
    [InlineData(36, false)]
    [InlineData(10, true)]
    public async Task ConstructedOwnerKnowledgePreconditionAffectsOnlyParticipatingPlayerInput(int lostPlayerSlot, bool ordinaryPick)
    {
        await using Session session = await Create("diamond_pickaxe", "air");
        // This is an explicit state-owner precondition, not a native774 undecodable frame or a connection/applier test. No retained native774 loss body exists; the real770 transport proof remains separate.
        await session.Client.InvokeAsync(client =>
        {
            client.State.Inventory.MarkSlotUnavailable(0, lostPlayerSlot);
            return true;
        }, session.Token);
        Assert.Equal(ordinaryPick, await session.Client.InvokeAsync(client => client.State.Inventory.IsPlayerSlotAvailable(36), session.Token));
        Assert.Equal(Identifier.Minecraft("diamond_pickaxe"), await session.Client.InvokeAsync(client => client.State.Inventory.PlayerSlots[36].Item.Id, session.Token));
        await AssertViewCaller(session, ordinaryPick);
    }

    private static async Task<Session> Create(string playerItem, string containerItem)
    {
        Session session = await Session.Create(Protocol);
        try
        {
            await session.Mode(GameMode.Survival, instantBuild: false);
            await session.Info(GameMode.Survival);
            await session.Block(Session.Target, "bedrock");
            ItemStack player = Stack(session, playerItem);
            ItemStack container = Stack(session, containerItem);
            await SendKnownAndWait(session, new ClientboundContainerSetSlotPacket(0, 1, 36, player), packet => packet is ClientboundContainerSetSlotPacket { ContainerId: 0, Slot: 36, StateId: 1 });
            await SendKnownAndWait(session, new ClientboundContainerSetSlotPacket(0, 1, 45, ItemStack.Empty), packet => packet is ClientboundContainerSetSlotPacket { ContainerId: 0, Slot: 45, StateId: 1 });
            int menu = session.Client.State.Registries!.MenuTypes[Identifier.Minecraft("generic_9x6")].NetworkId;
            await SendKnownAndWait(session, new ClientboundOpenScreenPacket(Window, menu, Component.Text("Player-view range"), null, 0, null), packet => packet is ClientboundOpenScreenPacket { ContainerId: Window });
            var items = new ItemStack[ContainerSlots + 36];
            Array.Fill(items, ItemStack.Empty);
            items[36] = container;
            items[PlayerMainInContainer] = player;
            await SendKnownAndWait(session, new ClientboundContainerSetContentPacket(Window, 2, items, ItemStack.Empty), packet => packet is ClientboundContainerSetContentPacket { ContainerId: Window, StateId: 2 });
            await session.Client.Actions.Movement.LookAtLocalAsync(new Vec3d(1.5, 65.5, 1.5), session.Token);
            await session.Client.InvokeAsync(client =>
            {
                Assert.Equal(Window, client.State.Inventory.OpenWindowId);
                Assert.Equal(0, client.State.Self.HeldSlot);
                Assert.Equal(player, client.State.Inventory.PlayerSlots[36]);
                Assert.Equal(player, client.State.Inventory.GetSlot(PlayerMainInContainer));
                Assert.Equal(container, client.State.Inventory.GetSlot(36));
                Assert.True(client.State.Inventory.PlayerSlots[45].IsEmpty);
                Assert.True(client.State.Inventory.GetSlot(45).IsEmpty);
                Assert.True(client.State.Inventory.IsPlayerSlotAvailable(36));
                Assert.True(client.State.Inventory.IsPlayerSlotAvailable(45));
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

    private static async Task AssertViewCaller(Session session, bool ordinaryPick)
    {
        // Bedrock's outline does not depend on the held item. The public cached block-only raycast remains available in every row; this test changes no raycast contract.
        var hit = Assert.IsType<Umpk.Game.World.BlockHitResult>(await session.Client.Snapshots.RaycastViewAsync(ct: session.Token));
        Assert.True(hit.Hit);
        Assert.Equal(Session.Target, hit.BlockPos);
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockInViewAsync(Session.Target, ct: caller.Token);
        bool capturedStart = false;
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            if (!ordinaryPick)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.WaitAsync(TimeSpan.FromSeconds(2), session.Token));
                Assert.True(action.IsCompleted, "An unsupported or unavailable player item-range input must end the view request before a player tick.");
                Assert.Empty(await session.Tick());
                Assert.Empty(await session.Tick());
            }
            else
            {
                List<IPacket> sent = await session.Tick();
                capturedStart = sent.OfType<ServerboundPlayerActionPacket>().Any(packet => packet.Action == 0 && packet.Position == Session.Target);
                Assert.Collection(sent,
                    packet =>
                    {
                        var start = Assert.IsType<ServerboundPlayerActionPacket>(packet);
                        Assert.Equal(0, start.Action);
                        Assert.Equal(Session.Target, start.Position);
                        Assert.Equal((byte)hit.Face, start.Direction);
                    },
                    AssertMainSwing,
                    AssertMainSwing);
                Assert.False(action.IsCompleted);
                AssertMainSwing(Assert.Single(await session.Tick()));
            }
            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally
        {
            await caller.CancelAsync();
            List<IPacket> cleanup = await session.Tick();
            if (capturedStart)
            {
                var abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(cleanup));
                Assert.Equal(1, abort.Action);
                Assert.Equal(Session.Target, abort.Position);
                Assert.Equal((byte)Direction.Down, abort.Direction);
            }
            else
                Assert.Empty(cleanup);
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) { }
            Assert.Empty(await session.Tick());
        }
    }

    private static ItemStack Stack(Session session, string name)
        => name == "air" ? ItemStack.Empty : new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft(name)], 1);

    private static void AssertMainSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.True(swing.HasHand);
        Assert.Equal(0, swing.Hand);
    }

    private static async Task SendKnownAndWait(Session session, IPacket packet, Func<object?, bool> matches)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] body = Encode(codec, packet, context);
        object decoded = codec.Decode(body, context);
        Assert.Equal(body, Encode(codec, decoded, context));
        int rawFrames = 0;
        void Observe(PacketObservation observation)
        {
            if (observation.Flow == PacketFlow.Clientbound && observation.Phase == ProtocolPhase.Play && observation.WireId == codec.WireId && observation.RawPayload.SequenceEqual(body))
                Interlocked.Increment(ref rawFrames);
        }
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = session.Client.Events.Subscribe<PacketReceived>(received =>
        {
            if (received.Phase == ProtocolPhase.Play && received.WireId == codec.WireId && matches(received.Packet))
                applied.TrySetResult();
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await session.Server.SendFrameAsync(codec.WireId, body, session.Token);
            await applied.Task.WaitAsync(session.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Equal(1, Volatile.Read(ref rawFrames));
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
