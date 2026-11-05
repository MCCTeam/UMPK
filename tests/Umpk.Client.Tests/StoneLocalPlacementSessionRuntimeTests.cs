using Umpk.Client.Actions;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class StoneLocalPlacementSessionRuntimeTests
{
    private static readonly int[] Protocols = [766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777];
    private static readonly Direction[] Faces = [Direction.Down, Direction.Up, Direction.North, Direction.South, Direction.West, Direction.East];
    public static IEnumerable<object[]> LocalCases() => Protocols.SelectMany(protocol => new[] { "main", "offhand", "creative-no-ability", "survival-infinite", "repeat-same", "repeat-two", "server-air", "server-stone" }.Select(scene => new object[] { protocol, scene }));
    public static IEnumerable<object[]> SafetyCases() => Protocols.SelectMany(protocol => new[] { "other-neighbor", "void-destination", "missing-neighbor", "custom", "clone", "no-abilities" }.Select(scene => new object[] { protocol, scene }));
    public static IEnumerable<object[]> LifetimeCases() => Protocols.SelectMany(protocol => new[] { "cancel-before", "cancel-after", "unload", "respawn" }.Select(scene => new object[] { protocol, scene }));

    private sealed class Pending(Task action, CancellationTokenSource cancellation, long interactionLifetime, long playerLifetime)
    {
        internal Task Action { get; } = action;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal long InteractionLifetime { get; } = interactionLifetime;
        internal long PlayerLifetime { get; } = playerLifetime;
        internal int Sequence { get; set; } = -1;
        internal bool ExpectedCancellation { get; set; }
        internal bool CleanupCancellation { get; set; }
    }

    [Theory]
    [MemberData(nameof(LocalCases))]
    public async Task OriginalStoneWriteAndConsumptionPrecedeUseAndShareTheExistingSequenceOwner(int protocol, string scene)
    {
        await using Session session = await Ready(protocol, abilities: true);
        Hand hand = scene == "offhand" ? Hand.Off : Hand.Main;
        if (scene == "creative-no-ability") await session.Mode(GameMode.Creative, instantBuild: false);
        if (scene == "survival-infinite") await session.Mode(GameMode.Survival, instantBuild: true);
        if (hand == Hand.Off) { await session.Hold(null); await Stack(session, Hand.Off, 32); }
        BlockPos destination = Session.Target.Offset(Direction.East);
        int air = State(session, "air"), stone = State(session, "stone");
        int consumed = scene == "survival-infinite" || scene == "creative-no-ability" && protocol <= 769 ? 0 : 1;
        var pending = new List<Pending>();
        try
        {
            Assert.Equal(air, await Current(session, destination));
            Assert.Equal(32, Held(session, hand).Count);
            Assert.Equal(BlockUseResult.Success, await Result(session, Session.Target, Direction.East, hand));
            Assert.Equal(air, await Current(session, destination));
            Assert.Equal(32, Held(session, hand).Count);
            await Start(session, pending, Session.Target, Direction.East, hand, stone, 32 - consumed, BlockUseResult.Success);
            Assert.Equal(air, session.Client.State.World.GetBlock(destination).StateId);
            if (scene == "repeat-same")
            {
                Assert.Equal(BlockUseResult.Fail, await Result(session, Session.Target, Direction.East, hand));
                await Start(session, pending, Session.Target, Direction.East, hand, stone, 31, BlockUseResult.Fail);
                await Ack(session, pending[0].Sequence);
                Assert.Equal(air, await Current(session, destination));
                Assert.False(pending[1].Action.IsCompleted);
            }
            else if (scene == "repeat-two")
            {
                BlockPos second = destination.Above();
                await Start(session, pending, destination, Direction.Up, hand, stone, 30, BlockUseResult.Success);
                Assert.Equal(stone, await Current(session, second));
                await Ack(session, pending[0].Sequence);
                Assert.Equal(air, await Current(session, destination));
                Assert.Equal(stone, await Current(session, second));
                Assert.False(pending[1].Action.IsCompleted);
            }
            else if (scene.StartsWith("server-", StringComparison.Ordinal))
            {
                int authoritative = scene == "server-air" ? air : stone;
                await session.Send(new ClientboundBlockUpdatePacket(destination, authoritative));
                Assert.Empty(await session.Barrier());
                Assert.Equal(authoritative, session.Client.State.World.GetBlock(destination).StateId);
                Assert.Equal(stone, await Current(session, destination));
                await Ack(session, pending[0].Sequence);
                Assert.Equal(authoritative, await Current(session, destination));
                Assert.Equal(31, Held(session, hand).Count);
                await Stack(session, hand, 29);
                Assert.Equal(29, Held(session, hand).Count);
            }
        }
        finally { await Finish(session, pending); }
        if (!scene.StartsWith("server-", StringComparison.Ordinal))
            Assert.Equal(air, await Current(session, destination));
        Assert.Equal(scene.StartsWith("server-", StringComparison.Ordinal) ? 29 : scene == "repeat-two" ? 30 : 32 - consumed, Held(session, hand).Count);
    }

    [Theory]
    [MemberData(nameof(SafetyCases))]
    public async Task UnsupportedMutationContextRetainsItsSeparateResultAndReceivedState(int protocol, string scene)
    {
        await using Session session = await Ready(protocol, abilities: scene != "no-abilities");
        BlockPos clicked = scene == "missing-neighbor" ? new(14, Session.Target.Y, 1) : Session.Target;
        BlockPos destination = clicked.Offset(Direction.East);
        await session.Block(clicked, "stone");
        await session.Block(destination, scene == "void-destination" ? "void_air" : "air");
        if (scene == "other-neighbor") await session.Block(destination.Above(), "cobblestone");
        if (scene == "missing-neighbor") await session.Block(destination.Offset(Direction.East), "air");
        // A literal stone anchor in the same section excludes the native LevelChunk all-air fast path; a palette-only VOID_AIR is not sufficient evidence for a native VOID_AIR getter.
        await session.Block(new(4, Session.Target.Y, 4), "stone");
        if (scene == "custom") { await session.Hold("stone", custom: true); await Stack(session, Hand.Main, 32, Held(session, Hand.Main)); }
        if (scene == "clone")
        {
            ItemStack original = Held(session, Hand.Main);
            var builder = new RegistryBuilder<ItemDefinition>(RegistryIds.Item);
            builder.Add(original.Item.NetworkId, original.Item.Id, original.Item.Value with { });
            var modeled = new ItemStack(builder.Build()[original.Item.Id], 32);
            await session.Client.InvokeAsync(client => { client.State.Inventory.SetSlot(0, 36, modeled); return true; }, session.Token);
            Assert.NotSame(original.Item.Value, Held(session, Hand.Main).Item.Value);
        }
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.EntityMembership.HasColumn(destination));
        if (scene == "missing-neighbor")
        {
            Assert.NotNull(session.Client.State.World.GetColumn(destination.Offset(Direction.East)));
            Assert.False(session.Client.State.EntityMembership.HasColumn(destination.Offset(Direction.East)));
        }
        if (scene == "no-abilities") Assert.NotEqual(session.Client.State.PlayerLifetime, session.Client.State.Self.AbilitiesObservationLifetime);
        int originalState = session.Client.State.World.GetBlock(destination).StateId;
        var pending = new List<Pending>();
        try { await Start(session, pending, clicked, Direction.East, Hand.Main, originalState, 32, scene == "custom" ? BlockUseResult.Unknown : BlockUseResult.Success); }
        finally { await Finish(session, pending); }
        Assert.Equal(originalState, await Current(session, destination));
        Assert.Equal(originalState, session.Client.State.World.GetBlock(destination).StateId);
        Assert.Equal(32, Held(session, Hand.Main).Count);
    }

    [Theory]
    [MemberData(nameof(LifetimeCases))]
    public async Task CancellationUnloadAndPlayerReplacementUseExistingMutationLifetimes(int protocol, string scene)
    {
        await using Session session = await Ready(protocol, abilities: true);
        BlockPos destination = Session.Target.Offset(Direction.East);
        var pending = new List<Pending>();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        if (scene == "cancel-before")
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.East, new(1, .5, .5), ct: caller.Token));
            Assert.Empty(await session.Barrier());
            Assert.Equal(State(session, "air"), await Current(session, destination));
            Assert.Equal(32, Held(session, Hand.Main).Count);
            return;
        }
        try
        {
            await Start(session, pending, Session.Target, Direction.East, Hand.Main, State(session, "stone"), 31, BlockUseResult.Success, caller.Token);
            if (scene == "cancel-after")
            {
                pending[0].ExpectedCancellation = true;
                await caller.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending[0].Action.WaitAsync(session.Token));
                Assert.Equal(State(session, "stone"), await Current(session, destination));
                await Ack(session, pending[0].Sequence);
                Assert.Equal(State(session, "air"), await Current(session, destination));
                Assert.Equal(31, Held(session, Hand.Main).Count);
            }
            else if (scene == "unload")
            {
                await session.Forget();
                Assert.False(session.Client.State.EntityMembership.HasColumn(destination));
                Assert.Equal(State(session, "void_air"), await Current(session, destination));
                await Ack(session, pending[0].Sequence);
                Assert.Equal(31, Held(session, Hand.Main).Count);
            }
            else
            {
                var oldWorld = session.Client.State.World;
                pending[0].ExpectedCancellation = true;
                await session.Respawn();
                Assert.False(session.Client.State.JukeboxPrediction.TryProject(oldWorld, destination, oldWorld.GetBlock(destination), out _));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending[0].Action.WaitAsync(session.Token));
            }
        }
        finally { await Finish(session, pending); }
    }

    [Theory]
    [InlineData(765)]
    public async Task UnadmittedProfilesKeepTheExistingUnmutatedState(int protocol)
    {
        await using Session session = await Ready(protocol, abilities: true);
        var pending = new List<Pending>();
        try { await Start(session, pending, Session.Target, Direction.East, Hand.Main, State(session, "air"), 32, BlockUseResult.Success); }
        finally { await Finish(session, pending); }
        Assert.Equal(32, Held(session, Hand.Main).Count);
    }

    [Theory]
    [InlineData(770)]
    public async Task NewlyAdmittedProfileUsesTheExistingMutationOwner(int protocol)
    {
        await using Session session = await Ready(protocol, abilities: true);
        var pending = new List<Pending>();
        try { await Start(session, pending, Session.Target, Direction.East, Hand.Main, State(session, "stone"), 31, BlockUseResult.Success); }
        finally { await Finish(session, pending); }
        Assert.Equal(31, Held(session, Hand.Main).Count);
    }

    private static async Task<Session> Ready(int protocol, bool abilities)
    {
        Session session = await Session.Create(protocol, "stone", physics: true, observeWrites: true);
        try
        {
            if (abilities) await session.Mode(GameMode.Survival, instantBuild: false);
            await Stack(session, Hand.Main, 32);
            BlockPos destination = Session.Target.Offset(Direction.East);
            await session.Block(destination, "air");
            foreach (Direction face in Faces)
                if (destination.Offset(face) != Session.Target) await session.Block(destination.Offset(face), "air");
            Assert.Empty(await session.Barrier());
            foreach (Direction face in Faces) Assert.True(session.Client.State.EntityMembership.HasColumn(destination.Offset(face)));
            Assert.Equal(32, Held(session, Hand.Main).Count);
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    private static ItemStack Held(Session session, Hand hand) => session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
    private static int State(Session session, string name) => session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].Value.DefaultStateId;
    private static Task<int> Current(Session session, BlockPos position) => session.Client.InvokeAsync(client => client.State.JukeboxPrediction.GetBlock(client.State.World, position).StateId, session.Token);
    private static Task<BlockUseResult> Result(Session session, BlockPos clicked, Direction face, Hand hand) => session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(clicked, face, face == Direction.Up ? new(.5, 1, .5) : new(1, .5, .5), hand).Result, session.Token);
    private static async Task Stack(Session session, Hand hand, int count, ItemStack? prototype = null)
    {
        ItemStack value = prototype ?? new ItemStack(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], count);
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, hand == Hand.Main ? 36 : 45, new ItemStack(value.Item, count, value.Components)));
        Assert.Empty(await session.Barrier());
        Assert.Equal(count, Held(session, hand).Count);
    }

    private static async Task Start(Session session, List<Pending> pending, BlockPos clicked, Direction face, Hand hand, int expectedState, int expectedCount, BlockUseResult result, CancellationToken? caller = null)
    {
        BlockPos destination = clicked.Offset(face);
        (int State, int Count)? atWrite = null;
        session.BeforeClientWrite = () => atWrite ??= (session.Client.State.JukeboxPrediction.GetBlock(session.Client.State.World, destination).StateId, Held(session, hand).Count);
        Vec3d cursor = face == Direction.Up ? new(.5, 1, .5) : new(1, .5, .5);
        BlockUsePrediction preview = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(clicked, face, cursor, hand), session.Token);
        var ownedCancellation = CancellationTokenSource.CreateLinkedTokenSource(caller ?? session.Token);
        Task action;
        try { action = session.Client.Actions.Interaction.PlaceBlockAsync(clicked, face, cursor, hand, ct: ownedCancellation.Token); }
        catch { ownedCancellation.Dispose(); throw; }
        var owned = new Pending(action, ownedCancellation, session.Client.State.InteractionLifetime, session.Client.State.PlayerLifetime);
        pending.Add(owned);
        await session.Client.InvokeAsync(_ => true, session.Token);
        session.BeforeClientWrite = null;
        List<IPacket> packets = await session.Barrier();
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        owned.Sequence = use.Sequence;
        Assert.Equal((clicked, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
        Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside || use.WorldBorderHit);
        Assert.False(action.IsCompleted);
        Assert.NotNull(atWrite);
        // The first causal assertion is after the actual public write has been retained for bounded ACK cleanup.
        (int State, int Count)? expectedAtWrite = (expectedState, expectedCount);
        Assert.Equal(expectedAtWrite, atWrite);
        Assert.Equal(result, preview.Result);
        BlockUseSwing swing = result == BlockUseResult.Success ? session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(swing, preview.Swing);
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        if (swing == BlockUseSwing.Packet) Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
    }

    private static async Task Ack(Session session, int sequence)
    {
        await session.Send(new ClientboundBlockChangedAckPacket(sequence));
        Assert.Empty(await session.Barrier());
    }

    private static async Task Finish(Session session, List<Pending> pending)
    {
        session.BeforeClientWrite = null;
        try
        {
            bool SameLifetime(Pending action) => session.Client.Status == ClientStatus.Playing
                && action.InteractionLifetime == session.Client.State.InteractionLifetime && action.PlayerLifetime == session.Client.State.PlayerLifetime;
            Pending[] waiting = pending.Where(action => !action.Action.IsCompleted && SameLifetime(action)).ToArray();
            int[] knownSequences = waiting.Where(action => action.Sequence >= 0).Select(action => action.Sequence).ToArray();
            if (knownSequences.Length != 0) await Ack(session, knownSequences.Max());
            foreach (Pending action in pending)
            {
                if (!action.Action.IsCompleted && (action.Sequence < 0 || !SameLifetime(action)))
                {
                    action.CleanupCancellation = true;
                    await action.Cancellation.CancelAsync();
                }
                if (action.ExpectedCancellation || action.CleanupCancellation)
                {
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.Action.WaitAsync(session.Token));
                    Assert.True(action.Action.IsCanceled);
                }
                else
                {
                    await action.Action.WaitAsync(session.Token);
                    Assert.True(action.Action.IsCompletedSuccessfully);
                }
            }
        }
        finally
        {
            foreach (Pending action in pending) action.Cancellation.Dispose();
        }
    }
}
