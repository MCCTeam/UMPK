using Umpk.Client.Actions;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

/// <summary>Additional caller acceptance through framed state and public input. Constructed scenarios do not claim native input or live server acceptance.</summary>
public sealed partial class ImmediateCarriedItemSynchronizationSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RemovedOrReplacedEntityCannotReuseAnEarlierInteractionPoint(bool offRayReplacement)
        => WithEntityInteraction(775, spawnCow: true, async session =>
        {
            await RemoveInteractionCow(session);
            Assert.Null(await session.Client.InvokeAsync(client => client.State.Entities.Get(EntityId), session.Token));
            if (offRayReplacement)
            {
                await SpawnInteractionCowAt(session, new Vec3d(8.5, 66, 4.5), new Guid("10000000-0000-0000-0000-000000000001"));
                Assert.Equal(new Vec3d(8.5, 66, 4.5), await session.Client.InvokeAsync(client => client.State.Entities.Get(EntityId)!.Position, session.Token));
            }
            int start = session.ObservedOutbound.Count;
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.InteractEntityAsync(EntityId, Hand.Main, session.Token));
            await session.Barrier();
            Assert.Empty(Packets(session, start));

            if (offRayReplacement)
                await RemoveInteractionCow(session);
            var replacementId = new Guid("20000000-0000-0000-0000-000000000002");
            var replacementPosition = new Vec3d(2.5, 65.5, 4.5);
            await SpawnInteractionCowAt(session, replacementPosition, replacementId);
            Assert.Equal(replacementId, await session.Client.InvokeAsync(client => client.State.Entities.Get(EntityId)!.Uuid, session.Token));
            Assert.Equal(replacementPosition, await session.Client.InvokeAsync(client => client.State.Entities.Get(EntityId)!.Position, session.Token));
            await AssertModernInteractionGroup(session, carry: true, secondary: false,
                [0xFA, 0xFF, 0x63, 0x33, 0x8F, 0x55], new Vec3d(0, 1.1199414026735028, -0.44997863639138136));
        });

    [Theory]
    [InlineData(775)]
    [InlineData(777)]
    public Task SneakInputUsesThePhysicsEyeAndTheCapturedSecondaryFlag(int protocol)
        => WithEntityInteraction(protocol, spawnCow: true, async session =>
        {
            await AssertModernInteractionGroup(session, carry: true, secondary: false,
                [0xF9, 0xFF, 0x46, 0x67, 0x9E, 0xB1], new Vec3d(0, 0.6199719221143869, -0.44997863639138136));
            await (await session.Client.InvokeAsync(client => client.Actions.Movement.SetSneakingAsync(true, session.Token), session.Token));
            await session.Tick();
            await session.Barrier();
            Assert.Equal(PhysicsConstants.PlayerCrouchEyeHeight, await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token));
            Assert.Equal(new Vec3d(2.5, 65 + PhysicsConstants.PlayerCrouchEyeHeight, 2.5), await session.Client.InvokeAsync(client => client.SelfEyePosition, session.Token));
            await AssertModernInteractionGroup(session, carry: false, secondary: true,
                [0xF9, 0xFF, 0x46, 0x67, 0x45, 0x19], new Vec3d(0, 0.26997497405847515, -0.44997863639138136));

            await (await session.Client.InvokeAsync(client => client.Actions.Movement.SetSneakingAsync(false, session.Token), session.Token));
            await session.Tick();
            await session.Barrier();
            Assert.Equal(PlayerReach.StandingEyeHeight, await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token));
            await AssertModernInteractionGroup(session, carry: false, secondary: false,
                [0xF9, 0xFF, 0x46, 0x67, 0x9E, 0xB1], new Vec3d(0, 0.6199719221143869, -0.44997863639138136));
        });

    [Theory]
    [InlineData("short")]
    [InlineData("exact")]
    [InlineData("inside")]
    public Task FramedEntityReachUsesItsStrictNativeBoundary(string boundary)
        => WithEntityInteraction(775, spawnCow: true, async session =>
        {
            double distance = (InteractionCowPosition.Z - (double)(.9F / 2)) - 2.5;
            double reach = boundary == "short" ? 1 : boundary == "exact" ? distance : Math.BitIncrement(distance);
            await InteractionReach(session, reach);
            if (boundary != "inside")
            {
                int start = session.ObservedOutbound.Count;
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.InteractEntityAsync(EntityId, Hand.Main, session.Token));
                await session.Barrier();
                Assert.Empty(Packets(session, start));
                await InteractionReach(session, 3);
            }
            await AssertModernInteractionGroup(session, carry: true, secondary: false,
                [0xF9, 0xFF, 0x46, 0x67, 0x9E, 0xB1], new Vec3d(0, 0.6199719221143869, -0.44997863639138136));
        });

    [Theory]
    [InlineData(47, 1)]
    [InlineData(47, -1)]
    [InlineData(47, 2)]
    [InlineData(775, -1)]
    [InlineData(775, 2)]
    public Task UnsupportedOrInvalidHandRejectsBeforeCarriedSynchronization(int protocol, int hand)
        => WithEntityInteraction(protocol, spawnCow: protocol >= 775, async session =>
        {
            int start = session.ObservedOutbound.Count;
            if (protocol == 47 && hand == 1)
                await Assert.ThrowsAsync<ActionNotSupportedException>(() => session.Client.Actions.Interaction.InteractEntityAsync(EntityId, (Hand)hand, session.Token));
            else
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.Client.Actions.Interaction.InteractEntityAsync(EntityId, (Hand)hand, session.Token));
            await session.Barrier();
            Assert.Empty(Packets(session, start));
            int tickStart = session.ObservedOutbound.Count;
            await session.Tick();
            await session.Barrier();
            Assert.Equal(new[] { Selected }, Packets(session, tickStart).OfType<ServerboundSetCarriedItemPacket>().Select(packet => (int)packet.Slot));
            Assert.Empty(Packets(session, tickStart).OfType<ServerboundInteractPacket>());
        });

    [Fact]
    public Task ModernCallerCancellationAtCarriedWriteFinishesTheKnownPointGroup()
        => WithEntityInteraction(775, spawnCow: true, async session =>
        {
            using var caller = new CancellationTokenSource();
            List<byte[]> frames = [];
            int start = session.ObservedOutbound.Count;
            session.BeforeFramedWrite = frame =>
            {
                frames.Add(frame.ToArray());
                if (frames.Count == 1)
                    caller.Cancel();
            };
            Task action = session.Client.Actions.Interaction.InteractEntityAsync(EntityId, Hand.Main, caller.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            session.BeforeFramedWrite = null;
            await session.Barrier();
            try { await action.WaitAsync(session.Token); }
            catch (OperationCanceledException) when (caller.IsCancellationRequested && !session.Token.IsCancellationRequested) { }
            Assert.True(caller.IsCancellationRequested);
            AssertModernInteractionGroup(session, start, frames, carry: true, secondary: false,
                [0xF9, 0xFF, 0x46, 0x67, 0x9E, 0xB1], new Vec3d(0, 0.6199719221143869, -0.44997863639138136));
            int tickStart = session.ObservedOutbound.Count;
            await session.Tick();
            await session.Barrier();
            Assert.Empty(Packets(session, tickStart).OfType<ServerboundSetCarriedItemPacket>());
        });

    [Fact]
    public async Task ModernTeardownAfterCarriedCompletionCannotWriteToTheNewPeer()
    {
        Session session = await Prepared(775, Selected, block: "air", physics: true);
        Exception? primaryFailure = null;
        Task? disconnected = null;
        try
        {
            await PrepareInteractionCow(session);
            List<byte[]> frames = [];
            session.BeforeFramedWrite = frame =>
            {
                frames.Add(frame.ToArray());
                if (frames.Count == 2)
                    disconnected = session.Client.DisconnectAsync(session.Token);
            };
            Task action = session.Client.Actions.Interaction.InteractEntityAsync(EntityId, Hand.Main, session.Token);
            Exception? ended = await Record.ExceptionAsync(() => action.WaitAsync(session.Token));
            session.BeforeFramedWrite = null;
            Assert.NotNull(disconnected);
            Assert.Equal(2, frames.Count);
            AssertCarriedFrame(session, frames[0], Selected);
            AssertInteractionFrame(session, frames[1], [EntityId, 0, 0xF9, 0xFF, 0x46, 0x67, 0x9E, 0xB1, 0]);
            Assert.True(ended is OperationCanceledException or ConnectionClosedException, ended?.ToString() ?? "The send completed across the controlled connection close.");
            FakeJavaServer oldPeer = session.Server;
            await disconnected!.WaitAsync(session.Token);
            await PeerEof(oldPeer, session.Token);
            await session.Restart("air");
            int start = session.ObservedOutbound.Count;
            await session.Barrier();
            Assert.Empty(Packets(session, start));
            Assert.True(action.IsCompleted);
            Assert.Empty(await session.Client.InvokeAsync(client => client.State.Entities.All.ToArray(), session.Token));
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            session.BeforeFramedWrite = null;
            await DisconnectAndObserveEof(session, primaryFailure, [disconnected]);
        }
    }

    [Fact]
    public Task ModernNoSessionFailureDoesNotBecomeAnUnknownHitFailure()
        => WithEntityInteraction(775, spawnCow: true, async session =>
        {
            await session.Client.DisconnectAsync(session.Token);
            await PeerEof(session.Server, session.Token);
            int start = session.ObservedOutbound.Count;
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.InteractEntityAsync(EntityId, Hand.Main, session.Token));
            Assert.Equal("The client is not connected.", failure.Message);
            Assert.Empty(Packets(session, start));
        });

    [Fact]
    public Task SpectatorStillSendsTheNativeEntityInteractionPacket()
        => WithEntityInteraction(775, spawnCow: true, async session =>
        {
            await session.Mode(GameMode.Spectator, instantBuild: false);
            await AssertModernInteractionGroup(session, carry: true, secondary: false,
                [0xF9, 0xFF, 0x46, 0x67, 0x9E, 0xB1], new Vec3d(0, 0.6199719221143869, -0.44997863639138136));
        });

    [Fact]
    public Task ReplacedWorldCannotReuseItsEntityButFreshTerrainCanRestoreTheCaller()
        => WithEntityInteraction(775, spawnCow: true, async session =>
        {
            var spawn = new CommonPlayerSpawnInfo(0, "minecraft:the_nether", 0, 0, -1, false, false, null, 0, 63);
            await Known(session, new ClientboundRespawnPacket(spawn, 0, null), decoded => Assert.Equal(spawn, decoded.SpawnInfo));
            Assert.Empty(await session.Client.InvokeAsync(client => client.State.Entities.All.ToArray(), session.Token));
            Assert.Null(await session.Client.InvokeAsync(client => client.State.World.GetColumn(new ChunkPos(0, 0)), session.Token));
            int start = session.ObservedOutbound.Count;
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.InteractEntityAsync(EntityId, Hand.Main, session.Token));
            await session.Barrier();
            Assert.Empty(Packets(session, start));

            await session.RejoinWorld("air");
            await Slot(session, Selected, "stone");
            await Known(session, new ClientboundSetHeldSlotPacket(Selected), decoded => Assert.Equal(Selected, decoded.Slot));
            await PrepareInteractionCow(session);
            await AssertModernInteractionGroup(session, carry: true, secondary: false,
                [0xF9, 0xFF, 0x46, 0x67, 0x9E, 0xB1], new Vec3d(0, 0.6199719221143869, -0.44997863639138136));
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NativeItemRangeInputStaysUnknownUntilOrdinaryHandsAreRestored(bool offHand)
        => WithEntityInteraction(775, spawnCow: true, async session =>
        {
            int slot = offHand ? 45 : 36 + Selected;
            await InteractionItemSlot(session, slot, "wooden_spear");
            Assert.True(await session.Client.InvokeAsync(client => client.State.Inventory.IsPlayerSlotAvailable(slot), session.Token));
            int start = session.ObservedOutbound.Count;
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.InteractEntityAsync(EntityId, Hand.Main, session.Token));
            await session.Barrier();
            Assert.Empty(Packets(session, start));
            await InteractionItemSlot(session, slot, "stone");
            await AssertModernInteractionGroup(session, carry: true, secondary: false,
                [0xF9, 0xFF, 0x46, 0x67, 0x9E, 0xB1], new Vec3d(0, 0.6199719221143869, -0.44997863639138136));
        });

    private static Task RemoveInteractionCow(Session session)
        => Known(session, new ClientboundRemoveEntitiesPacket([EntityId]), decoded => Assert.Equal(new[] { EntityId }, decoded.EntityIds));

    private static Task SpawnInteractionCowAt(Session session, Vec3d point, Guid uuid)
    {
        int type = session.Client.State.Registries!.EntityTypes[Identifier.Minecraft("cow")].NetworkId;
        return Known(session, new ClientboundAddEntityPacket(EntityId, uuid, type, point.X, point.Y, point.Z, 0, 0, 0, 0, 0, 0, 0, [0]), decoded =>
        {
            Assert.Equal(EntityId, decoded.EntityId);
            Assert.Equal(uuid, decoded.Uuid);
            Assert.Equal(type, decoded.TypeId);
            Assert.Equal(point, new Vec3d(decoded.X, decoded.Y, decoded.Z));
            Assert.Equal(new byte[] { 0 }, decoded.ModernVelocityRaw);
        });
    }

    private static async Task InteractionReach(Session session, double range)
    {
        var id = Identifier.Minecraft("entity_interaction_range");
        int networkId = session.Client.State.Registries!.Attributes[id].NetworkId;
        int selfId = session.Client.State.Self.EntityId;
        await Known(session, new ClientboundUpdateAttributesPacket(selfId, [new(null, networkId, range, [])]), decoded =>
        {
            Assert.Equal(selfId, decoded.EntityId);
            AttributeSnapshot attribute = Assert.Single(decoded.Attributes);
            Assert.Equal(networkId, attribute.ModernId);
            Assert.Equal(range, attribute.BaseValue);
            Assert.Empty(attribute.Modifiers);
        });
        Assert.Equal(range, await session.Client.InvokeAsync(client => client.State.Self.Attributes.Value(id, -1), session.Token));
    }

    private static Task InteractionItemSlot(Session session, int slot, string name)
    {
        var stack = new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft(name)], 1);
        return Known(session, new ClientboundContainerSetSlotPacket(0, 7, checked((short)slot), stack), decoded =>
        {
            Assert.Equal(0, decoded.ContainerId);
            Assert.Equal(7, decoded.StateId);
            Assert.Equal(slot, decoded.Slot);
            Assert.Equal(stack.Item.Id, decoded.Item.Item.Id);
            Assert.Equal(1, decoded.Item.Count);
        });
    }

    private static async Task AssertModernInteractionGroup(Session session, bool carry, bool secondary, byte[] locationBytes, Vec3d decodedPoint)
    {
        List<byte[]> frames = [];
        int start = session.ObservedOutbound.Count;
        session.BeforeFramedWrite = frame => frames.Add(frame.ToArray());
        try
        {
            await session.Client.Actions.Interaction.InteractEntityAsync(EntityId, Hand.Main, session.Token);
            session.BeforeFramedWrite = null;
            await session.Barrier();
        }
        finally
        {
            session.BeforeFramedWrite = null;
        }
        AssertModernInteractionGroup(session, start, frames, carry, secondary, locationBytes, decodedPoint);
    }

    private static void AssertModernInteractionGroup(Session session, int start, List<byte[]> frames, bool carry, bool secondary, byte[] locationBytes, Vec3d decodedPoint)
    {
        IPacket[] sent = Packets(session, start);
        int offset = carry ? 1 : 0;
        Assert.Equal(offset + 1, sent.Length);
        Assert.Equal(offset + 1, frames.Count);
        if (carry)
        {
            Assert.Equal(Selected, Assert.IsType<ServerboundSetCarriedItemPacket>(sent[0]).Slot);
            AssertCarriedFrame(session, frames[0], Selected);
        }
        var interact = Assert.IsType<ServerboundInteractPacket>(sent[offset]);
        Assert.Equal((EntityId, 0, (int?)0, (bool?)secondary), (interact.EntityId, interact.Action, interact.Hand, interact.UsingSecondaryAction));
        Assert.Equal(decodedPoint, Assert.IsType<Vec3d>(interact.InteractAt));
        AssertInteractionFrame(session, frames[offset], [EntityId, 0, .. locationBytes, secondary ? (byte)1 : (byte)0]);
    }
}
