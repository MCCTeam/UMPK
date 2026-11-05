using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

/// <summary>Real interaction entry through framed session state. These are caller/ownership tests, not native input or server acceptance.</summary>
public sealed partial class ImmediateCarriedItemSynchronizationSessionTests
{
    private static readonly Vec3d InteractionCowPosition = new(2.5, 66, 4.5);
    private static readonly Vec3d InteractionRelativePoint = new(0, PlayerReach.StandingEyeHeight - 1, -(double)(.9F / 2));

    [Theory]
    [InlineData(47)]
    [InlineData(315)]
    [InlineData(404)]
    [InlineData(774)]
    public Task ExplicitEntityInteractionSynchronizesBeforeItsExistingLegacyPacket(int protocol)
        => WithEntityInteraction(protocol, spawnCow: false, async session =>
        {
            int start = session.ObservedOutbound.Count;
            List<byte[]> frames = [];
            session.BeforeFramedWrite = frame => frames.Add(frame.ToArray());
            await session.Client.Actions.Interaction.InteractEntityAsync(EntityId, Hand.Main, session.Token);
            session.BeforeFramedWrite = null;
            await session.Barrier();
            IPacket[] sent = Packets(session, start);
            Assert.Equal(2, sent.Length);
            AssertCarriedFrame(session, frames[0], Selected);
            var carried = Assert.IsType<ServerboundSetCarriedItemPacket>(sent[0]);
            Assert.Equal(Selected, carried.Slot);
            var interact = Assert.IsType<ServerboundInteractPacket>(sent[1]);
            Assert.Equal(EntityId, interact.EntityId);
            Assert.Equal(0, interact.Action);
            Assert.Equal(protocol == 47 ? null : (int?)0, interact.Hand);
            Assert.Null(interact.InteractAt);
            Assert.Equal(protocol < 735 ? null : (bool?)false, interact.UsingSecondaryAction);
            byte[] body = protocol == 47 ? [EntityId, 0] : protocol < 735 ? [EntityId, 0, 0] : [EntityId, 0, 0, 0];
            AssertInteractionFrame(session, frames[1], body);
            Assert.Equal(2, frames.Count);
            int tickStart = session.ObservedOutbound.Count;
            await session.Tick();
            await session.Barrier();
            Assert.Empty(Packets(session, tickStart).OfType<ServerboundSetCarriedItemPacket>());
        });

    [Theory]
    [InlineData(775, 0)]
    [InlineData(775, 1)]
    [InlineData(776, 0)]
    [InlineData(776, 1)]
    [InlineData(777, 0)]
    [InlineData(777, 1)]
    public Task NativeLocationInteractionUsesTheRealOwnedViewHit(int protocol, int hand)
        => WithEntityInteraction(protocol, spawnCow: true, async session =>
        {
            int start = session.ObservedOutbound.Count;
            List<byte[]> frames = [];
            session.BeforeFramedWrite = frame => frames.Add(frame.ToArray());
            await session.Client.Actions.Interaction.InteractEntityAsync(EntityId, (Hand)hand, session.Token);
            session.BeforeFramedWrite = null;
            await session.Barrier();
            IPacket[] sent = Packets(session, start);
            Assert.Equal(2, sent.Length);
            AssertCarriedFrame(session, frames[0], Selected);
            var carried = Assert.IsType<ServerboundSetCarriedItemPacket>(sent[0]);
            Assert.Equal(Selected, carried.Slot);
            var interact = Assert.IsType<ServerboundInteractPacket>(sent[1]);
            Assert.Equal((EntityId, 0, (int?)hand, (bool?)false), (interact.EntityId, interact.Action, interact.Hand, interact.UsingSecondaryAction));
            // Observation decodes the LP wire value, so compare to independently quantized bits rather than to the unquantized caller point.
            Vec3d point = Assert.IsType<Vec3d>(interact.InteractAt);
            Assert.Equal(0, point.X);
            Assert.Equal(0.6199719221143869, point.Y);
            Assert.Equal(-0.44997863639138136, point.Z);
            byte[] body = [EntityId, (byte)hand, 0xF9, 0xFF, 0x46, 0x67, 0x9E, 0xB1, 0];
            Assert.Equal(2, frames.Count);
            AssertInteractionFrame(session, frames[1], body);
            Assert.Equal(body, BoundDescriptorCodec.EncodeServerbound(protocol, interact));
        });

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("unloaded")]
    [InlineData("blocked")]
    public Task MissingModernHitContextRejectsBeforeThePacketGroup(string context)
        => WithEntityInteraction(775, spawnCow: context != "missing", async session =>
        {
            if (context == "unloaded")
                await Known(session, new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)), decoded => Assert.Equal(new ChunkPos(0, 0), decoded.Chunk));
            if (context == "blocked")
            {
                var obstruction = new BlockPos(2, 66, 3);
                await session.Block(obstruction, "stone");
                await session.Barrier();
                Assert.Equal(obstruction, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
            }
            int start = session.ObservedOutbound.Count;
            int requested = context == "wrong" ? EntityId + 1 : EntityId;
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.InteractEntityAsync(requested, Hand.Main, session.Token));
            await session.Barrier();
            Assert.Empty(Packets(session, start));
            int tickStart = session.ObservedOutbound.Count;
            await session.Tick();
            await session.Barrier();
            Assert.Equal(new[] { Selected }, Packets(session, tickStart).OfType<ServerboundSetCarriedItemPacket>().Select(packet => (int)packet.Slot));
            Assert.Empty(Packets(session, tickStart).OfType<ServerboundInteractPacket>());
        });

    [Fact]
    public async Task QueuedOldSessionInteractCannotWriteThroughAReconnectedPeer()
    {
        var scheduler = new ChannelSessionScheduler();
        Session session = await Prepared(Protocol, Selected, scheduler: scheduler);
        Exception? primaryFailure = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? blocker = null;
        Task? disconnected = null;
        Task? action = null;
        try
        {
            blocker = scheduler.InvokeAsync((Func<ValueTask>)(async () =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(session.Token);
            }), session.Token);
            await entered.Task.WaitAsync(session.Token);
            action = StartAction(session, "interact", session.Token);
            FakeJavaServer oldPeer = session.Server;
            disconnected = session.Client.DisconnectAsync(session.Token);
            await PeerEof(oldPeer, session.Token);
            release.TrySetResult();
            await blocker.WaitAsync(session.Token);
            await disconnected.WaitAsync(session.Token);
            Exception? ended = await Record.ExceptionAsync(() => action.WaitAsync(session.Token));
            Assert.True(ended is OperationCanceledException or ConnectionClosedException, ended?.ToString());
            await session.Restart("lever");
            int start = session.ObservedOutbound.Count;
            await session.Barrier();
            Assert.Empty(Packets(session, start));
            Assert.True(action.IsCompleted);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            release.TrySetResult();
            await DisconnectAndObserveEof(session, primaryFailure, [blocker, disconnected], scheduler);
        }
    }

    private static async Task WithEntityInteraction(int protocol, bool spawnCow, Func<Session, Task> body)
    {
        Session session = await Prepared(protocol, Selected, block: "air", physics: protocol >= 775);
        Exception? primaryFailure = null;
        try
        {
            if (spawnCow)
                await PrepareInteractionCow(session);
            await body(session);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            session.BeforeFramedWrite = null;
            await DisconnectAndObserveEof(session, primaryFailure);
        }
    }

    private static async Task PrepareInteractionCow(Session session)
    {
        int type = session.Client.State.Registries!.EntityTypes[Identifier.Minecraft("cow")].NetworkId;
        var point = InteractionCowPosition;
        await Known(session, new ClientboundAddEntityPacket(EntityId, Guid.Empty, type, point.X, point.Y, point.Z, 0, 0, 0, 0, 0, 0, 0, [0]), decoded =>
        {
            Assert.Equal(EntityId, decoded.EntityId);
            Assert.Equal(type, decoded.TypeId);
            Assert.Equal(point, new Vec3d(decoded.X, decoded.Y, decoded.Z));
            Assert.Equal(new byte[] { 0 }, decoded.ModernVelocityRaw);
        });
        await session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.Features.Entities);
            Assert.True(client.State.Features.Physics);
            Assert.Equal(new Vec3d(2.5, 65, 2.5), client.State.Self.Position);
            Assert.Equal(0F, client.State.Self.Yaw);
            Assert.Equal(0F, client.State.Self.Pitch);
            var entity = Assert.IsType<Umpk.Game.Entities.Entity>(client.State.Entities.Get(EntityId));
            Assert.Equal(InteractionCowPosition, entity.Position);
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(entity));
            Assert.True(client.State.EntityGeometry.IsPositionKnown(entity));
            Assert.True(client.State.EntityGeometry.TryGet(entity, JavaGameData.EntityGeometryData(session.Protocol), out Aabb bounds, out _, session.Protocol));
            Assert.Equal(InteractionCowPosition.Z - (double)(.9F / 2), bounds.MinZ);
            Assert.InRange(65 + PlayerReach.StandingEyeHeight, bounds.MinY, bounds.MaxY);
            Assert.Equal(new Vec3d(0, PlayerReach.StandingEyeHeight - 1, -(double)(.9F / 2)), InteractionRelativePoint);
            return true;
        }, session.Token);
    }

    private static void AssertInteractionFrame(Session session, byte[] frame, byte[] expectedBody)
    {
        Assert.True(JavaVersions.TryGetByProtocol(session.Protocol, out JavaVersion version));
        Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound)
            .TryGetOutbound(EntityPackets.Serverbound.Interact, out int expectedId, out BoundPacketCodec codec));
        var reader = new PacketReader(frame);
        int length = reader.ReadVarInt();
        int payloadStart = reader.Position;
        Assert.Equal(expectedId, reader.ReadVarInt());
        byte[] body = reader.ReadBytes(reader.Remaining).ToArray();
        Assert.Equal(frame.Length - payloadStart, length);
        Assert.Equal(expectedBody, body);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var decoded = Assert.IsType<ServerboundInteractPacket>(codec.Decode(body, context));
        Assert.Equal(body, Encode(codec, decoded, context));
    }
}
