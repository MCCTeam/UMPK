using Umpk.Client.Actions;
using Umpk.Client.Snapshots;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed partial class JukeboxSequencedPredictionSessionTests
{
    [Theory]
    [InlineData(47, false)]
    [InlineData(340, false)]
    [InlineData(393, false)]
    [InlineData(404, false)]
    [InlineData(477, false)]
    [InlineData(761, false)]
    [InlineData(777, false)]
    [InlineData(47, true)]
    [InlineData(340, true)]
    [InlineData(393, true)]
    [InlineData(404, true)]
    [InlineData(477, true)]
    [InlineData(761, true)]
    [InlineData(777, true)]
    public async Task ProjectedStaticOutlinePreservesNativeClipAndHeldMiningWhilePublicViewKeepsReceivedState(int protocol, bool outwardBoundary)
    {
        bool button = protocol == 777;
        await using Session session = await Session.Create(protocol, button ? "stone_button" : "jukebox");
        int received = session.Client.State.World.GetBlock(Session.Target).StateId;
        if (!button)
        {
            BlockDefinition definition = session.Client.State.Registries!.Blocks[Identifier.Minecraft("jukebox")].Value;
            received = protocol <= 340 ? definition.MinStateId | 1 : Filled(session);
            await SetState(session, Session.Target, received);
        }

        PendingUse? pending = null;
        try
        {
            if (protocol >= 759)
                pending = await BeginUse(session, protocol, 1, BlockUseResult.Success);
            else
            {
                List<IPacket> use = await session.Use(Hand.Main);
                Assert.Equal(2, use.Count);
                if (protocol == 47)
                {
                    var placed = Assert.IsType<ServerboundLegacyBlockPlacePacket>(use[0]);
                    Assert.Equal((Session.Target, (int)Direction.Up), (placed.Position, placed.Face));
                    Assert.Equal(((byte)8, (byte)16, (byte)8), (placed.CursorX, placed.CursorY, placed.CursorZ));
                    Assert.True(placed.HeldItem.IsEmpty);
                }
                else
                {
                    var placed = Assert.IsType<ServerboundUseItemOnPacket>(use[0]);
                    Assert.Equal((Session.Target, (int)Direction.Up, (int)Hand.Main, 0), (placed.Position, placed.Face, placed.Hand, placed.Sequence));
                    Assert.Equal((.5F, 1F, .5F), (placed.CursorX, placed.CursorY, placed.CursorZ));
                    Assert.False(placed.Inside);
                }
                Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(use[1]).Hand);
            }

            Assert.Equal(button ? BlockUseResult.Consume : BlockUseResult.Pass, await Predict(session));
            Assert.Equal(received, session.Client.State.World.GetBlock(Session.Target).StateId);
            int current = await session.Client.InvokeAsync(client => client.State.JukeboxPrediction.GetBlock(client.State.World, Session.Target).StateId, session.Token);
            Assert.NotEqual(received, current);

            // Both participating families have static, zero-offset outlines. The powered north-wall button is thinner along Z; its X bounds remain 5/16..11/16.
            double eyeX = outwardBoundary ? button ? 1.6875 : 2 : 1.5;
            double eyeZ = button ? 1.96875 : 1.5;
            var eye = new Vec3d(eyeX, 65.5, eyeZ);
            await SetProjectedClipEye(session, protocol, eye);
            BlockHitResult? receivedBefore = await session.Client.Snapshots.RaycastViewAsync(4.5, ct: session.Token);
            BlockHitResult? nativeBefore = await session.Client.InvokeAsync(client => ClientSnapshots.CastView(client, 4.5, includeFluids: false, nativeCurrent: true), session.Token);
            bool expectedHit = !outwardBoundary || protocol <= 340;
            Direction face = protocol < 477 ? Direction.East : Direction.West;

            using var caller = new CancellationTokenSource();
            Task mining = session.Client.Actions.Interaction.DigBlockInViewAsync(Session.Target, 4.5, caller.Token);
            bool started = false;
            try
            {
                await session.Client.InvokeAsync(_ => true, session.Token);
                int beforeTick = session.ObservedOutbound.Count;
                await session.Tick();
                List<IPacket> animations = await session.Barrier();
                IPacket[] group = session.ObservedOutbound.Skip(beforeTick).Where(packet => packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundUseItemOnPacket or ServerboundLegacyBlockPlacePacket).ToArray();
                started = group.OfType<ServerboundPlayerActionPacket>().Any(packet => packet.Action == 0);
                if (!expectedHit)
                {
                    Assert.Empty(group);
                    Assert.Empty(animations);
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
                }
                else
                {
                    Assert.Equal(3, group.Length);
                    int startIndex = protocol == 47 ? 1 : 0;
                    var start = Assert.IsType<ServerboundPlayerActionPacket>(group[startIndex]);
                    Assert.Equal((0, Session.Target, (byte)face, protocol >= 759 ? (int?)2 : null), (start.Action, start.Position, start.Direction, start.Sequence));
                    if (protocol == 777)
                    {
                        Assert.IsType<ServerboundPunchPacket>(group[1]);
                        Assert.IsType<ServerboundPunchPacket>(group[2]);
                    }
                    else
                    {
                        Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(group[protocol == 47 ? 0 : 1]).Hand);
                        Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(group[2]).Hand);
                    }
                    Assert.Equal(2, animations.Count);
                    Assert.False(mining.IsCompleted);
                }
            }
            finally
            {
                int beforeRelease = session.ObservedOutbound.Count;
                caller.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
                await session.Tick();
                Assert.Empty(await session.Barrier());
                IPacket[] released = session.ObservedOutbound.Skip(beforeRelease).Where(packet => packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundUseItemOnPacket or ServerboundLegacyBlockPlacePacket).ToArray();
                if (started)
                {
                    var abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(released));
                    Assert.Equal((protocol == 777 ? 2 : 1, Session.Target, (byte)Direction.Down, protocol >= 759 ? (int?)0 : null), (abort.Action, abort.Position, abort.Direction, abort.Sequence));
                }
                else
                    Assert.Empty(released);
            }

            AssertProjectedClip(receivedBefore, protocol, eye, outwardBoundary, expectedHit, face);
            AssertProjectedClip(nativeBefore, protocol, eye, outwardBoundary, expectedHit, face);
            Assert.Equal(received, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(current, await session.Client.InvokeAsync(client => client.State.JukeboxPrediction.GetBlock(client.State.World, Session.Target).StateId, session.Token));
            Assert.Equal(receivedBefore, await session.Client.Snapshots.RaycastViewAsync(4.5, ct: session.Token));

            if (pending is not null)
            {
                await SendAck(session, protocol, pending.Sequence);
                await pending.Action.WaitAsync(session.Token);
            }
            else
                await SetState(session, Session.Target, received);
            Assert.Equal(received, await session.Client.InvokeAsync(client => client.State.JukeboxPrediction.GetBlock(client.State.World, Session.Target).StateId, session.Token));
            Assert.Equal(receivedBefore, await session.Client.InvokeAsync(client => ClientSnapshots.CastView(client, 4.5, includeFluids: false, nativeCurrent: true), session.Token));
        }
        finally
        {
            if (pending is not null)
                await pending.DisposeAsync();
        }
    }

    private static void AssertProjectedClip(BlockHitResult? result, int protocol, Vec3d eye, bool outwardBoundary, bool expectedHit, Direction expectedFace)
    {
        if (!expectedHit)
        {
            Assert.Null(result);
            return;
        }
        BlockHitResult hit = Assert.IsType<BlockHitResult>(result);
        Assert.True(hit.Hit);
        Assert.Equal(Session.Target, hit.BlockPos);
        Assert.Equal(expectedFace, hit.Face);
        Assert.Equal(protocol >= 393, hit.InsideBlock);
        double expectedX = protocol <= 340 ? 2 : eye.X + .0045;
        Assert.Equal(expectedX, hit.Point.X, precision: 9);
        Assert.Equal(eye.Y, hit.Point.Y, precision: 9);
        Assert.Equal(eye.Z, hit.Point.Z, precision: 9);
        Assert.Equal(protocol <= 340 ? outwardBoundary ? 0 : .5 : .0045, hit.Distance, precision: 9);
    }

    private static async Task SetProjectedClipEye(Session session, int protocol, Vec3d eye)
    {
        double eyeHeight = await session.Client.InvokeAsync(client => client.SelfEyeHeight, session.Token);
        var position = new Vec3d(eye.X, eye.Y - eyeHeight, eye.Z);
        var packet = new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, -90, 0, 0, 3, new PositionMoveRotation(position, Vec3d.Zero, -90, 0));
        await RoundTripSend(session, protocol, packet, "player_position", decoded =>
        {
            var actual = Assert.IsType<ClientboundPlayerPositionPacket>(decoded);
            Assert.Equal(position, actual.ModernValues?.Position ?? new Vec3d(actual.X, actual.Y, actual.Z));
            Assert.Equal((-90F, 0F), actual.ModernValues is { } modern ? (modern.YRot, modern.XRot) : (actual.Yaw, actual.Pitch));
            Assert.Equal(0, actual.RelativeFlags);
            Assert.Equal<int?>(protocol == 47 ? null : 3, actual.TeleportId);
        });
        Assert.Empty(await session.Barrier());
        Vec3d actualEye = await session.Client.InvokeAsync(client => client.SelfEyePosition, session.Token);
        Assert.Equal(eye.X, actualEye.X, precision: 12);
        Assert.Equal(eye.Y, actualEye.Y, precision: 12);
        Assert.Equal(eye.Z, actualEye.Z, precision: 12);
        Assert.Equal((-90F, 0F), (session.Client.State.Self.Yaw, session.Client.State.Self.Pitch));
    }
}
