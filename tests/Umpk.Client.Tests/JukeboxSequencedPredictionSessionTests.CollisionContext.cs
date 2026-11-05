using Umpk.Client.Actions;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Physics;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed partial class JukeboxSequencedPredictionSessionTests
{
    public static IEnumerable<object[]> ContextualAckCases()
    {
        foreach (int protocol in new[] { 761, 777 })
        {
            yield return [protocol, "powder_threshold", "powder_snow", false, false, -20000, 3.0, false, false];
            yield return [protocol, "powder_falling", "powder_snow", false, false, -20001, 3.0, false, true];
            yield return [protocol, "powder_boots_above", "powder_snow", true, false, 0, .999995, false, true];
            yield return [protocol, "powder_bare_above", "powder_snow", false, false, 0, .999995, false, false];
            yield return [protocol, "powder_boots_descending", "powder_snow", true, true, 0, .999995, false, false];
            yield return [protocol, "powder_boots_inside", "powder_snow", true, false, 0, .5, false, false];
            yield return [protocol, "scaffold_above", "scaffolding", false, false, 0, .999995, false, true];
            yield return [protocol, "scaffold_inside", "scaffolding", false, false, 0, .5, false, false];
            yield return [protocol, "scaffold_descending", "scaffolding", false, true, 0, .999995, false, false];
            yield return [protocol, "scaffold_bottom", "scaffolding", false, false, 0, .05, true, true];
            yield return [protocol, "scaffold_below_bottom", "scaffolding", false, false, 0, -.00002, true, false];
        }
    }

    [Theory]
    [MemberData(nameof(ContextualAckCases))]
    public async Task CumulativeAckRestoresPositionOnlyForSuppliedContextualShadow(int protocol, string scenario, string shadowName, bool boots, bool descending, int rawVelocityY, double initialOffsetY, bool bottom, bool collides)
    {
        Assert.NotEmpty(scenario);
        await using Session session = await Session.Create(protocol, "stone_button", physics: true);
        Vec3d captured = session.Client.State.Self.Position;
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        BlockDefinition shadowBlock = session.Client.State.Registries!.Blocks[Identifier.Minecraft(shadowName)].Value;
        int shadow = shadowBlock.DefaultStateId;
        if (shadowName == "scaffolding")
            shadow = Enumerable.Range(shadowBlock.MinStateId, shadowBlock.StateCount).First(state =>
                shadowBlock.TryGetPropertyValue(state, "bottom", out string value) && value == (bottom ? "true" : "false")
                && shadowBlock.TryGetPropertyValue(state, "distance", out string distance) && distance == "7"
                && shadowBlock.TryGetPropertyValue(state, "waterlogged", out string waterlogged) && waterlogged == "false");
        await SetState(session, Session.Target, shadow);
        Assert.Equal(BlockUseResult.Consume, await Predict(session));
        ItemStack equipment = boots ? new(session.Client.State.Registries!.Items[Identifier.Minecraft("leather_boots")], 1) : ItemStack.Empty;
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 8, equipment));
        Assert.Empty(await session.Barrier());
        Task sneak = await session.Client.InvokeAsync(client => client.Actions.Movement.SetSneakingAsync(descending, session.Token), session.Token);
        await sneak.WaitAsync(session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Equal(descending, session.Client.State.Self.Sneaking);
        Vec3d initial = new(Session.Target.X + .5, Session.Target.Y + initialOffsetY, Session.Target.Z + .5);
        Vec3d velocity = new(0, rawVelocityY / 8000D, 0);
        if (protocol >= 770 && rawVelocityY != 0)
        {
            // The modern player-position codec carries exact double velocity; avoid the unrelated quantized entity-velocity wire band.
            await session.Send(new ClientboundPlayerPositionPacket(initial.X, initial.Y, initial.Z, 0, 0, 0, 2, new PositionMoveRotation(initial, velocity, 0, 0)));
            Assert.Empty(await session.Barrier());
            Assert.Equal(initial, session.Client.State.Self.Position);
            Assert.Equal(velocity, session.Client.State.Self.Velocity);
        }
        else
        {
            await MoveSelf(session, initial);
            if (rawVelocityY != 0)
            {
                await session.Send(new ClientboundSetEntityMotionPacket(session.Client.State.Self.EntityId, 0, checked((short)rawVelocityY), 0));
                Assert.Empty(await session.Barrier());
                Assert.Equal(velocity, session.Client.State.Self.Velocity);
            }
        }
        // One real tick establishes the applied sneak/body context; a zero starting Y velocity keeps the non-fall scenarios at their exact boundary position.
        await session.Tick();
        Assert.Empty(await session.Barrier());
        PhysicsState before = await ReadBody(session);
        Assert.Equal(rawVelocityY == 0 ? 0 : -rawVelocityY / 8000D, before.FallDistance, precision: 10);
        Assert.Equal(initial.Y + rawVelocityY / 8000D, before.Position.Y, precision: 10);
        Assert.NotEqual(captured, before.Position);
        Assert.True(Aabb.BlockAt(Session.Target.X, Session.Target.Y, Session.Target.Z).Intersects(before.BoundingBox));
        int sentBefore = session.ObservedOutbound.Count;
        await session.Client.Actions.Interaction.UseItemAsync(ct: session.Token);
        Assert.Empty(await session.Barrier());
        ServerboundUseItemPacket airUse = Assert.Single(session.ObservedOutbound.Skip(sentBefore).OfType<ServerboundUseItemPacket>());
        Assert.Equal(2, airUse.Sequence);
        Assert.False(use.Action.IsCompleted);
        await SendAck(session, protocol, airUse.Sequence);
        await use.Action.WaitAsync(session.Token);
        PhysicsState after = await ReadBody(session);
        Vec3d expected = collides ? captured : before.Position;
        Assert.Equal(expected, session.Client.State.Self.Position);
        Assert.Equal(expected, after.Position);
        Assert.Equal(before.BoundingBox.Move(expected.Subtract(before.Position)), after.BoundingBox);
        Assert.Equal((before.Velocity, before.Yaw, before.Pitch, before.Pose, before.FallDistance, before.OnGround, before.HorizontalCollision, before.VerticalCollision),
            (after.Velocity, after.Yaw, after.Pitch, after.Pose, after.FallDistance, after.OnGround, after.HorizontalCollision, after.VerticalCollision));
        Assert.Equal(shadow, session.Client.State.World.GetBlock(Session.Target).StateId);
        Assert.Empty(await session.Barrier());
    }

    [Theory]
    [InlineData(761)]
    [InlineData(777)]
    public async Task AckUsesNavigationAppliedSneakWhenManualSelfSneakIsFalse(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone_button", physics: true);
        Vec3d captured = session.Client.State.Self.Position;
        await using PendingUse use = await BeginUse(session, protocol, 1, BlockUseResult.Success);
        BlockDefinition scaffold = session.Client.State.Registries!.Blocks[Identifier.Minecraft("scaffolding")].Value;
        int shadow = scaffold.DefaultStateId;
        Assert.True(scaffold.TryGetPropertyValue(shadow, "bottom", out string bottom) && bottom == "false");
        await SetState(session, Session.Target, shadow);
        await MoveSelf(session, new Vec3d(Session.Target.X + .5, Session.Target.Y + .999995, Session.Target.Z + .5));
        Assert.False(session.Client.State.Self.Sneaking);
        Vec3d initial = session.Client.State.Self.Position;
        using var moveCancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task movement = session.Client.Actions.Movement.MoveToAsync(initial.Add(new Vec3d(.06, 0, 0)), moveCancellation.Token);
        try
        {
            // Observe readiness only. The actual public navigator supplies and executes its normal sub-block approach on the next session tick.
            bool ready = false;
            for (int attempt = 0; attempt < 1000 && !ready; attempt++)
            {
                ready = await session.Client.InvokeAsync(client =>
                {
                    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    object navigator = typeof(UmpkClient).GetField("_navigator", flags)!.GetValue(client)!;
                    return navigator.GetType().GetField("_pendingTick", flags)!.GetValue(navigator) is not null;
                }, session.Token);
                if (!ready)
                    await Task.Delay(1, session.Token);
            }
            Assert.True(ready);
            await session.Tick();
            Assert.Empty(await session.Barrier());
            bool appliedSneak = await session.Client.InvokeAsync(client =>
            {
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var owner = Assert.IsType<Umpk.Client.Navigation.PhysicsEngineHolder>(typeof(UmpkClient).GetField("_physics", flags)!.GetValue(client));
                return owner.LastAppliedInput.Sneak;
            }, session.Token);
            Assert.True(appliedSneak);
            Assert.False(session.Client.State.Self.Sneaking);
            Assert.False(movement.IsCompleted);
            PhysicsState before = await ReadBody(session);
            Assert.Equal(initial.Y, before.Position.Y, precision: 10);
            Assert.NotEqual(captured, before.Position);
            int outboundBefore = session.ObservedOutbound.Count;
            await session.Client.Actions.Interaction.UseItemAsync(ct: session.Token);
            Assert.Empty(await session.Barrier());
            ServerboundUseItemPacket air = Assert.Single(session.ObservedOutbound.Skip(outboundBefore).OfType<ServerboundUseItemPacket>());
            Assert.Equal(2, air.Sequence);
            await SendAck(session, protocol, air.Sequence);
            await use.Action.WaitAsync(session.Token);
            PhysicsState after = await ReadBody(session);
            Assert.Equal(before.Position, session.Client.State.Self.Position);
            Assert.Equal(before.Position, after.Position);
            Assert.Equal(before.BoundingBox, after.BoundingBox);
            Assert.Equal(before.Velocity, after.Velocity);
            Assert.Equal(shadow, session.Client.State.World.GetBlock(Session.Target).StateId);
        }
        finally
        {
            moveCancellation.Cancel();
            try { await movement.WaitAsync(session.Token); }
            catch (OperationCanceledException) when (moveCancellation.IsCancellationRequested) { }
        }
    }

}
