using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class CakeActivationSessionRuntimeTests
{
    // Literal result rows come from each registered original cake method, not the production profile.
    public static IEnumerable<object[]> NativeProfiles()
    {
        yield return [47, "Success"];
        yield return [107, "Success"];
        yield return [108, "Success"];
        yield return [109, "Success"];
        yield return [110, "Success"];
        yield return [210, "Success"];
        yield return [315, "Success"];
        yield return [316, "Success"];
        yield return [335, "Success"];
        yield return [338, "Success"];
        yield return [340, "Success"];
        yield return [393, "Success"];
        yield return [401, "Success"];
        yield return [404, "Success"];
        yield return [477, "Success"];
        yield return [480, "Success"];
        yield return [485, "Success"];
        yield return [490, "Success"];
        yield return [498, "Success"];
        yield return [573, "Consume"];
        yield return [575, "Consume"];
        yield return [578, "Consume"];
        yield return [735, "Consume"];
        yield return [736, "Consume"];
        yield return [751, "Consume"];
        yield return [753, "Consume"];
        yield return [754, "Consume"];
        yield return [755, "Consume"];
        yield return [756, "Consume"];
        yield return [757, "Consume"];
        yield return [758, "Consume"];
        yield return [759, "Consume"];
        yield return [760, "Consume"];
        yield return [761, "Consume"];
        yield return [762, "Consume"];
        yield return [763, "Consume"];
        yield return [764, "Consume"];
        yield return [765, "Consume"];
        yield return [766, "Consume"];
        yield return [767, "Consume"];
        yield return [768, "Consume"];
        yield return [769, "Consume"];
        yield return [770, "Consume"];
        yield return [771, "Consume"];
        yield return [772, "Consume"];
        yield return [773, "Consume"];
        yield return [774, "Consume"];
        yield return [775, "Consume"];
        yield return [776, "Consume"];
        yield return [777, "Consume"];
    }

    [Theory]
    [MemberData(nameof(NativeProfiles))]
    public async Task EmptyHandUsesNativeResultAndMutatesOnceBeforeTheUseWrite(int protocol, string fullResult)
    {
        await using Session session = await Session.Create(protocol, "cake");
        await Prime(session, 18, 0, flags: 0);
        int original = CakeState(session, 0);
        await SetCake(session, 0);
        BlockUsePrediction preview = await Preview(session);
        Assert.Equal(18, session.Client.State.Self.Food);
        Assert.Equal(original, Local(session).StateId);
        await Use(session, BlockUseResult.Success, 20, .4F, CakeState(session, 1), confirm: true);
        Assert.Equal(BlockUseResult.Success, preview.Result);
        await Use(session, Enum.Parse<BlockUseResult>(fullResult), 20, .4F, CakeState(session, 1));
        Assert.True(session.Client.State.Inventory.GetSlot(36).IsEmpty);
        Assert.True(session.Client.State.Inventory.GetSlot(45).IsEmpty);
    }

    public static IEnumerable<object[]> Contexts()
    {
        yield return [498, "invulnerable-hungry", "Success", false];
        yield return [573, "invulnerable-hungry", "Success", true];
        yield return [498, "invulnerable-full", "Success", false];
        yield return [573, "invulnerable-full", "Success", true];
        yield return [498, "creative-full-not-invulnerable", "Success", false];
        yield return [573, "creative-full-not-invulnerable", "Consume", false];
        yield return [210, "default-stack-full", "Success", false];
        yield return [315, "default-stack-full", "Pass", false];
        yield return [573, "default-stack-full", "Pass", false];
        yield return [755, "default-stack-hungry", "Success", true];
        yield return [777, "default-stack-hungry", "Success", true];
        yield return [573, "cooled-stack", "Success", true];
        yield return [777, "cooled-stack", "Success", true];
        yield return [573, "adventure-stack", "Success", true];
        yield return [777, "adventure-stack", "Success", true];
        yield return [47, "sneak-empty", "Success", true];
        yield return [777, "sneak-empty", "Success", true];
        yield return [315, "sneak-stack", "Pass", false];
        yield return [777, "sneak-stack", "Pass", false];
        yield return [765, "offhand-empty", "Success", true];
        yield return [766, "offhand-empty", "Pass", false];
        yield return [777, "offhand-stack", "Pass", false];
        yield return [765, "sneak-opposite-stack", "Pass", false];
        yield return [777, "sneak-opposite-stack", "Pass", false];
        yield return [765, "spectator", "Success", false];
        yield return [768, "spectator", "Consume", false];
        yield return [777, "spectator", "Consume", false];
        yield return [47, "custom-stack", "Unknown", false];
        yield return [777, "custom-stack", "Unknown", false];
        yield return [755, "candle", "Unknown", false];
        yield return [777, "candle", "Unknown", false];
        yield return [315, "storage-only", "Unknown", false];
        yield return [777, "storage-only", "Unknown", false];
        yield return [47, "missing-health", "Unknown", false];
        yield return [777, "missing-health", "Unknown", false];
        yield return [315, "missing-abilities", "Unknown", false];
        yield return [777, "missing-abilities", "Unknown", false];
        yield return [777, "missing-inventory", "Unknown", false];
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task PacketOwnedContextKeepsNativePrecedenceAndExplicitUnknowns(int protocol, string context, string expected, bool eats)
    {
        await using Session session = await Session.Create(protocol, "cake", terrain: context != "storage-only");
        int food = context.Contains("full", StringComparison.Ordinal) ? 20 : 18;
        if (context != "missing-health")
            await Health(session, food, 0);
        if (context != "missing-abilities")
            await Abilities(session, context.StartsWith("invulnerable", StringComparison.Ordinal) ? (byte)1 : (byte)0);
        await SetCake(session, 0);
        if (context is "default-stack-full" or "default-stack-hungry" or "cooled-stack" or "adventure-stack" or "sneak-stack" or "custom-stack")
            await session.Hold("stick", custom: context == "custom-stack");
        if (context is "offhand-stack" or "sneak-opposite-stack")
            await session.Offhand("stick");
        if (context == "offhand-empty")
        {
            await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 45, ItemStack.Empty));
            Assert.Empty(await session.Barrier());
        }
        if (context == "candle")
            await session.Hold("candle");
        if (context == "missing-inventory")
            session.Client.State.Features.Inventory = false;
        if (context == "cooled-stack")
            await session.Cooldown();
        if (context == "adventure-stack")
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context == "creative-full-not-invulnerable")
        {
            await session.Mode(GameMode.Creative, instantBuild: true);
            Assert.False(session.Client.State.Self.Invulnerable);
            Assert.True(session.Client.State.Self.InstantBuild);
        }
        if (context == "spectator")
            await session.Mode(GameMode.Spectator, instantBuild: false);
        if (context.StartsWith("sneak", StringComparison.Ordinal))
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.Empty(await session.Barrier());
        }
        int beforeFood = session.Client.State.Self.Food;
        Hand hand = context.StartsWith("offhand", StringComparison.Ordinal) ? Hand.Off : Hand.Main;
        await Use(session, Enum.Parse<BlockUseResult>(expected), eats ? 20 : beforeFood, eats ? .4F : 0, context == "storage-only" ? null : CakeState(session, eats ? 1 : 0), hand);
    }

    [Theory]
    [InlineData(766, -3, -3F)]
    [InlineData(767, 0, 0F)]
    public async Task NutritionUsesTheNativeLowerClampBoundary(int protocol, int expectedFood, float expectedSaturation)
    {
        await using Session session = await Session.Create(protocol, "cake");
        await Prime(session, -5, -1F, 0);
        await Use(session, BlockUseResult.Success, expectedFood, expectedSaturation, CakeState(session, 1));
    }

    [Fact]
    public async Task NutritionPreservesNativeSinglePrecisionAddition()
    {
        await using Session session = await Session.Create(777, "cake");
        await Prime(session, 19, .05F, 0);
        await Use(session, BlockUseResult.Success, 20, BitConverter.Int32BitsToSingle(0x3EE66667), CakeState(session, 1));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(315)]
    [InlineData(498)]
    [InlineData(573)]
    [InlineData(759)]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(777)]
    public async Task RepeatedUseRemovesBiteSixAndKeepsBlockAckSeparateFromNutritionCorrection(int protocol)
    {
        await using Session session = await Session.Create(protocol, "cake");
        await Prime(session, 0, 0, 0);
        await SetCake(session, 5);
        Pending first = await Use(session, BlockUseResult.Success, 2, .4F, CakeState(session, 6), deferAck: true);
        Pending second = await Use(session, BlockUseResult.Success, 4, .8F, Air(session), deferAck: true);
        await SetCake(session, 5);
        if (protocol >= 759)
        {
            Assert.True(Local(session).IsAir);
            await session.Send(new ClientboundBlockChangedAckPacket(second.Sequence!.Value));
        }
        await Task.WhenAll(first.Action, second.Action).WaitAsync(session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Equal(CakeState(session, 5), Local(session).StateId);
        Assert.Equal(4, session.Client.State.Self.Food);
        await Health(session, 1, 0);
        await Use(session, BlockUseResult.Success, 3, .4F, CakeState(session, 6), confirm: true);
    }

    [Theory]
    [InlineData(765)]
    [InlineData(777)]
    public async Task RespawnRequiresCurrentPlayerHealthAndAbilitiesInEitherArrivalOrder(int protocol)
    {
        await using Session session = await Session.Create(protocol, "cake");
        await Prime(session, 18, 0, 1);
        await Use(session, BlockUseResult.Success, 20, .4F, CakeState(session, 1), confirm: true);
        var oldWorld = session.Client.State.World;
        await session.Respawn();
        await session.Terrain();
        await SetCake(session, 0);
        Assert.NotSame(oldWorld, session.Client.State.World);
        await Use(session, BlockUseResult.Unknown, 20, .4F, CakeState(session, 0));
        await Health(session, 18, 0);
        await Use(session, BlockUseResult.Unknown, 18, 0, CakeState(session, 0));
        await Abilities(session, 0);
        await Use(session, BlockUseResult.Success, 20, .4F, CakeState(session, 1), confirm: true);
        await session.Respawn();
        await session.Terrain();
        await SetCake(session, 0);
        await Abilities(session, 0);
        await Use(session, BlockUseResult.Unknown, 20, .4F, CakeState(session, 0));
        await Health(session, 18, 0);
        await Use(session, BlockUseResult.Success, 20, .4F, CakeState(session, 1), confirm: true);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(315)]
    [InlineData(777)]
    public async Task CanceledInputDoesNotMutateAndReconnectDoesNotReuseOldPacketObservations(int protocol)
    {
        await using Session session = await Session.Create(protocol, "cake");
        await Prime(session, 18, 0, 0);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), ct: canceled.Token));
        Assert.Empty(await session.Barrier());
        Assert.Equal(18, session.Client.State.Self.Food);
        Assert.Equal(CakeState(session, 0), Local(session).StateId);
        await session.Client.DisconnectAsync(session.Token);
        await session.Restart("cake");
        await Use(session, BlockUseResult.Unknown, 18, 0, CakeState(session, 0));
        await Prime(session, 18, 0, 0);
        await Use(session, BlockUseResult.Success, 20, .4F, CakeState(session, 1), confirm: true);
    }

    [Theory]
    [InlineData(759)]
    [InlineData(777)]
    public async Task CancellationAfterTheCommittedUseDoesNotRepeatNutrition(int protocol)
    {
        await using Session session = await Session.Create(protocol, "cake");
        await Prime(session, 18, 0, 0);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Pending use = await Use(session, BlockUseResult.Success, 20, .4F, CakeState(session, 1), deferAck: true, caller: caller.Token);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => use.Action.WaitAsync(session.Token));
        await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence!.Value));
        Assert.Empty(await session.Barrier());
        await Use(session, BlockUseResult.Consume, 20, .4F, CakeState(session, 0));
    }

    [Theory]
    [InlineData(47)]
    [InlineData(777)]
    public async Task NativeMutationPrecedesTheActualOwnedPipeWriteFaultAndIsNotRetried(int protocol)
    {
        await using Session session = await Session.Create(protocol, "cake");
        await Prime(session, 18, 0, 0);
        await session.Server.ClientPipe.Output.CompleteAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), ct: session.Token));
        Assert.Equal(20, session.Client.State.Self.Food);
        Assert.Equal(.4F, session.Client.State.Self.Saturation);
        Assert.Equal(CakeState(session, 1), Local(session).StateId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), ct: session.Token));
        Assert.Equal(20, session.Client.State.Self.Food);
        Assert.Equal(.4F, session.Client.State.Self.Saturation);
    }

    [Theory]
    [InlineData(47, 7)]
    [InlineData(315, 15)]
    public async Task UnmeasuredLegacyBitesDoNotInventAnEatingResult(int protocol, int bites)
    {
        await using Session session = await Session.Create(protocol, "cake");
        await Prime(session, 18, 0, 0);
        await SetCake(session, bites);
        await Use(session, BlockUseResult.Unknown, 18, 0, CakeState(session, bites));
    }

    [Fact]
    public async Task LegacyOffhandRejectsBeforeAnyCakeMutation()
    {
        await using Session session = await Session.Create(47, "cake");
        await Prime(session, 18, 0, 0);
        await Assert.ThrowsAsync<ActionNotSupportedException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Off, ct: session.Token));
        Assert.Empty(await session.Barrier());
        Assert.Equal(18, session.Client.State.Self.Food);
        Assert.Equal(CakeState(session, 0), Local(session).StateId);
    }

    private static async Task Prime(Session session, int food, float saturation, byte flags)
    {
        await Health(session, food, saturation);
        await Abilities(session, flags);
    }

    private static async Task Health(Session session, int food, float saturation)
    {
        JavaVersion version = JavaVersions.All.First(v => v.Version.Protocol == session.Protocol);
        var registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        var entry = Assert.Single(registry.Packets, pair => pair.Type.Id == EntityPackets.Clientbound.SetHealth.Id);
        Assert.True(registry.TryGetInbound(entry.WireId, out BoundPacketCodec codec));
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var expected = new ClientboundSetHealthPacket(20, food, saturation);
        byte[] bytes = Encode(codec, expected, context);
        Assert.Equal(expected, Assert.IsType<ClientboundSetHealthPacket>(codec.Decode(bytes, context)));
        Assert.Equal(bytes, Encode(codec, codec.Decode(bytes, context), context));
        await session.Server.SendFrameAsync(entry.WireId, bytes, session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Equal((food, saturation, true), (session.Client.State.Self.Food, session.Client.State.Self.Saturation, session.Client.State.Self.HealthObserved));
    }

    private static async Task Abilities(Session session, byte flags)
    {
        await session.Send(new ClientboundPlayerAbilitiesPacket(flags, .05F, .1F));
        Assert.Empty(await session.Barrier());
        Assert.Equal((flags & 1) != 0, session.Client.State.Self.Invulnerable);
        Assert.Equal((flags & 8) != 0, session.Client.State.Self.InstantBuild);
    }

    private static async Task SetCake(Session session, int bites)
    {
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, CakeState(session, bites)));
        Assert.Empty(await session.Barrier());
    }
    private static int Air(Session session) => session.Client.State.Registries!.Blocks[Identifier.Minecraft("air")].Value.DefaultStateId;
    private static BlockState Local(Session session) => session.Client.State.JukeboxPrediction.GetBlock(session.Client.State.World, Session.Target);
    private static int CakeState(Session session, int bites)
    {
        BlockDefinition cake = session.Client.State.Registries!.Blocks[Identifier.Minecraft("cake")].Value;
        return session.Protocol <= 340 ? (cake.DefaultStateId & ~15) | bites : Enumerable.Range(cake.MinStateId, cake.StateCount).Single(id => cake.TryGetPropertyValue(id, "bites", out string value) && value == bites.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private static Task<BlockUsePrediction> Preview(Session session, Hand hand = Hand.Main) => session.Client.InvokeAsync(c => c.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), hand), session.Token);
    private sealed record Pending(Task Action, int? Sequence);

    private static async Task<Pending> Use(Session session, BlockUseResult expected, int food, float saturation, int? state, Hand hand = Hand.Main, bool confirm = false, bool deferAck = false, CancellationToken? caller = null)
    {
        Assert.Empty(await session.Barrier());
        ItemStack stack = session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
        int observed = session.Client.State.World.GetBlock(Session.Target).StateId;
        BlockUsePrediction preview = await Preview(session, hand);
        var atUse = new List<(int Food, float Saturation, int State)>();
        void Observe(PacketObservation frame)
        {
            if (frame.Flow == PacketFlow.Serverbound && frame.DecodedPacket is ServerboundLegacyBlockPlacePacket or ServerboundUseItemOnPacket)
                atUse.Add((session.Client.State.Self.Food, session.Client.State.Self.Saturation, state is null ? -1 : Local(session).StateId));
        }
        session.Client.PacketFrameObserved += Observe;
        Task action;
        List<IPacket> packets;
        try
        {
            action = session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), hand, ct: caller ?? session.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            packets = await session.Barrier();
        }
        finally { session.Client.PacketFrameObserved -= Observe; }
        bool packetSwing = expected == BlockUseResult.Success && session.Protocol != 777;
        Assert.Equal(packetSwing ? 2 : 1, packets.Count);
        int? sequence = null;
        if (session.Protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
            Assert.Equal((Session.Target, (int)Direction.Up, (byte)8, (byte)16, (byte)8), (use.Position, use.Face, use.CursorX, use.CursorY, use.CursorZ));
            Assert.Equal(stack, use.HeldItem);
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal((Session.Target, (int)Direction.Up, (int)hand, .5F, 1F, .5F, false, false), (use.Position, use.Face, use.Hand, use.CursorX, use.CursorY, use.CursorZ, use.Inside, use.WorldBorderHit));
            if (session.Protocol >= 759)
            {
                Assert.True(use.Sequence > 0);
                sequence = use.Sequence;
            }
        }
        if (packetSwing)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, p => p is ServerboundPunchPacket or ServerboundPlayerActionPacket);
        Assert.True(preview.SendPacket);
        Assert.Equal(expected, preview.Result);
        Assert.Equal(expected == BlockUseResult.Success ? session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None, preview.Swing);
        Assert.Equal((food, saturation, state ?? -1), Assert.Single(atUse));
        Assert.Equal((food, saturation), (session.Client.State.Self.Food, session.Client.State.Self.Saturation));
        Assert.Equal(observed, session.Client.State.World.GetBlock(Session.Target).StateId);
        if (state is int local)
            Assert.Equal(local, Local(session).StateId);
        if (confirm && state is int accepted)
            await session.Send(new ClientboundBlockUpdatePacket(Session.Target, accepted));
        if (!deferAck)
        {
            if (sequence is int ack)
                await session.Send(new ClientboundBlockChangedAckPacket(ack));
            await action.WaitAsync(session.Token);
            Assert.Empty(await session.Barrier());
        }
        return new(action, sequence);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
