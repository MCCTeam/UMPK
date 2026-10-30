using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class CandleAttachmentSessionRuntimeTests
{
    // Exact registered native CandleBlock items across755–777, including the later same-color ColorCollection registration.
    private static readonly string[] CandleNames = ["candle", "white_candle", "orange_candle", "magenta_candle", "light_blue_candle", "yellow_candle", "lime_candle", "pink_candle", "gray_candle", "light_gray_candle", "cyan_candle", "purple_candle", "blue_candle", "brown_candle", "green_candle", "red_candle", "black_candle"];

    public static IEnumerable<object[]> NativeHandRows()
    {
        foreach (int protocol in new[] { 755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777 })
            foreach (Hand hand in new[] { Hand.Main, Hand.Off })
                if (protocol != 755 || hand != Hand.Main)
                    yield return [protocol, hand];
    }

    [Fact]
    public Task FramedSourceBackedAttachmentCommitsBeforeTheOwnedUseWrite()
        => TaggedCandleConsumesOnceAndChangesTheBlockBeforeTheOwnedUseWrite(755, Hand.Main);

    [Theory]
    [MemberData(nameof(NativeHandRows))]
    public async Task TaggedCandleConsumesOnceAndChangesTheBlockBeforeTheOwnedUseWrite(int protocol, Hand hand)
    {
        await using Session session = await Ready(protocol, hand);
        ItemStack original = Held(session, hand);
        BlockUsePrediction preview = await Preview(session, hand);
        Assert.Equal(original, Held(session, hand));
        Assert.Equal(Cake(session), Local(session).StateId);
        Assert.False(session.Client.State.Self.HealthObserved);
        await Use(session, hand, BlockUseResult.Success, 2, Attached(session, "candle"), confirm: true);
        Assert.Equal(BlockUseResult.Success, preview.Result);
        Assert.Equal(original.Shrink(1), Held(session, hand));
        Assert.False(session.Client.State.Self.HealthObserved);
        // Candle-cake activation is a separate open family; this attachment slice cannot consume the held stack twice.
        await Use(session, hand, BlockUseResult.Unknown, 2, Attached(session, "candle"));
    }

    public static IEnumerable<object[]> ColorRows()
    {
        foreach (int protocol in new[] { 755, 777 })
            foreach (string candle in CandleNames)
                yield return [protocol, candle];
    }

    [Theory]
    [MemberData(nameof(ColorRows))]
    public async Task EveryRegisteredCandleMapsToItsUnlitCakeAndCountOneBecomesEmpty(int protocol, string candle)
    {
        await using Session session = await Ready(protocol, Hand.Main, candle, count: 1);
        await Use(session, Hand.Main, BlockUseResult.Success, 0, Attached(session, candle), confirm: true);
        Assert.True(Held(session, Hand.Main).IsEmpty);
        Assert.True(Local(session).TryGetProperty("lit", out string lit));
        Assert.Equal("false", lit);
    }

    [Theory]
    [InlineData(760, true, false, false, 3)]
    [InlineData(760, false, true, true, 2)]
    [InlineData(761, true, false, false, 3)]
    [InlineData(766, false, true, true, 2)]
    [InlineData(767, true, false, true, 2)]
    [InlineData(767, false, true, false, 3)]
    [InlineData(777, true, false, true, 2)]
    [InlineData(777, false, true, false, 3)]
    public async Task CountUsesOwnPlayerInfoThenInstantBuildRatherThanControllerMode(int protocol, bool ownCreative, bool instantBuild, bool controllerCreative, int count)
    {
        await using Session session = await Ready(protocol, Hand.Main);
        await Info(session, ownCreative ? GameMode.Creative : GameMode.Survival);
        await session.Mode(controllerCreative ? GameMode.Creative : GameMode.Survival, instantBuild);
        await Use(session, Hand.Main, BlockUseResult.Success, count, Attached(session, "candle"), confirm: true);
    }

    [Theory]
    [InlineData(760, false)]
    [InlineData(761, true)]
    [InlineData(766, true)]
    public async Task OwnInfoRemovalKeepsTheExistingNativeCachedObjectBoundary(int protocol, bool cached)
    {
        await using Session session = await Ready(protocol, Hand.Main);
        await Info(session, GameMode.Creative);
        await Use(session, Hand.Main, BlockUseResult.Success, 3, Attached(session, "candle"), confirm: true);
        await RemoveInfo(session);
        await Info(session, GameMode.Survival);
        await SetCake(session);
        await Use(session, Hand.Main, BlockUseResult.Success, cached ? 3 : 2, Attached(session, "candle"), confirm: true);
        await session.Respawn();
        await session.Terrain();
        await SetCake(session);
        await Hold(session, Hand.Main, "candle", 3);
        await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), confirm: true);
    }

    [Theory]
    [InlineData(760)]
    [InlineData(761)]
    [InlineData(766)]
    public async Task SamePlayerInfoObjectReceivesGameModeUpdatesAndMissingEntryIsRetried(int protocol)
    {
        await using Session session = await Ready(protocol, Hand.Main);
        await RemoveInfo(session);
        await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), confirm: true);
        await Info(session, GameMode.Creative);
        await SetCake(session);
        await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), confirm: true);
        Assert.True(session.Client.State.TabList.TryGet(session.Client.State.Self.Uuid, out TabListEntry? original));
        await Info(session, GameMode.Survival, update: true);
        Assert.True(session.Client.State.TabList.TryGet(session.Client.State.Self.Uuid, out TabListEntry? updated));
        Assert.Same(original, updated);
        await SetCake(session);
        await Use(session, Hand.Main, BlockUseResult.Success, 1, Attached(session, "candle"), confirm: true);
    }

    [Theory]
    [InlineData(760, false)]
    [InlineData(761, true)]
    [InlineData(766, true)]
    public async Task MiningPrimesTheSameOwnInfoCacheThatCandleAttachmentReads(int protocol, bool cached)
    {
        await using Session session = await Ready(protocol, Hand.Main, physics: true);
        await Info(session, GameMode.Creative);
        await session.Mode(GameMode.Creative, instantBuild: true);
        await session.Block(Session.Target, "stone");
        Assert.Empty(await session.Barrier());
        await session.Hold("diamond_sword");
        await MineOnce(session, restricted: true);
        await RemoveInfo(session);
        await Info(session, GameMode.Survival);
        await SetCake(session);
        await Hold(session, Hand.Main, "candle", 3);
        await Use(session, Hand.Main, BlockUseResult.Success, cached ? 3 : 2, Attached(session, "candle"), confirm: true);
    }

    [Theory]
    [InlineData(760, false)]
    [InlineData(761, true)]
    [InlineData(766, true)]
    public async Task CandleAttachmentPrimesTheSameOwnInfoCacheThatMiningReads(int protocol, bool cached)
    {
        await using Session session = await Ready(protocol, Hand.Main, physics: true);
        await Info(session, GameMode.Creative);
        await Use(session, Hand.Main, BlockUseResult.Success, 3, Attached(session, "candle"), confirm: true);
        await RemoveInfo(session);
        await Info(session, GameMode.Survival);
        await session.Mode(GameMode.Creative, instantBuild: true);
        await session.Block(Session.Target, "stone");
        Assert.Empty(await session.Barrier());
        await session.Hold("diamond_sword");
        await MineOnce(session, restricted: cached);
    }

    [Theory]
    [InlineData("preview", false)]
    [InlineData("preview", true)]
    [InlineData("missing-tags", false)]
    [InlineData("missing-tags", true)]
    [InlineData("noncandle", false)]
    [InlineData("noncandle", true)]
    [InlineData("bites", false)]
    [InlineData("bites", true)]
    [InlineData("tag-excluded", false)]
    [InlineData("tag-excluded", true)]
    [InlineData("open", false)]
    [InlineData("open", true)]
    [InlineData("pre-canceled", false)]
    [InlineData("pre-canceled", true)]
    public async Task APreviewOrUncommittedCakePathCannotPrimeTheFirstActualOwnInfoLookup(string attempt, bool thenMine)
    {
        await using Session session = await Session.Create(766, "cake", physics: true, observeWrites: true);
        await Abilities(session, 0);
        await Info(session, GameMode.Creative);
        await Hold(session, Hand.Main, attempt == "noncandle" ? "stick" : "candle", 3);
        if (attempt != "missing-tags")
            await Tags(session, attempt == "tag-excluded" ? [] : CandleNames);
        if (attempt == "bites")
            await SetCake(session, 1);
        if (attempt is "bites" or "tag-excluded")
            await session.Cooldown();
        if (attempt == "open")
        {
            await session.Send(new ClientboundOpenScreenPacket(7, 0, Umpk.Text.Component.Text("Uncommitted candle"), null, 0, null));
            Assert.Empty(await session.Barrier());
        }
        if (attempt == "preview")
        {
            _ = await Preview(session, Hand.Main);
            Assert.Equal(3, Held(session, Hand.Main).Count);
            Assert.Equal(Cake(session), Local(session).StateId);
        }
        else if (attempt == "pre-canceled")
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), ct: canceled.Token));
            Assert.Empty(await session.Barrier());
        }
        else
            await Use(session, Hand.Main, BlockUseResult.Unknown, 3, Cake(session, attempt == "bites" ? 1 : 0));
        await RemoveInfo(session);
        await Info(session, GameMode.Survival);
        if (attempt == "open")
        {
            await session.Send(new ClientboundContainerClosePacket(7));
            Assert.Empty(await session.Barrier());
        }
        await Tags(session, CandleNames);
        if (thenMine)
        {
            await session.Mode(GameMode.Creative, instantBuild: true);
            await session.Block(Session.Target, "stone");
            Assert.Empty(await session.Barrier());
            await session.Hold("diamond_sword");
            await MineOnce(session, restricted: false);
        }
        else
        {
            await SetCake(session);
            await Hold(session, Hand.Main, "candle", 3);
            await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), confirm: true);
        }
    }

    public static IEnumerable<object[]> ContextRows()
    {
        yield return [755, "unknown-tag", "Unknown"];
        yield return [777, "unknown-tag", "Unknown"];
        yield return [755, "tag-excluded", "Unknown"];
        yield return [777, "tag-excluded", "Unknown"];
        yield return [777, "block-tag-only", "Unknown"];
        yield return [777, "bites-one", "Unknown"];
        yield return [777, "bites-six", "Unknown"];
        yield return [777, "missing-abilities", "Unknown"];
        yield return [766, "missing-identity", "Unknown"];
        yield return [777, "missing-inventory", "Unknown"];
        yield return [766, "open-main", "Unknown"];
        yield return [766, "open-off", "Unknown"];
        yield return [777, "open-off", "Unknown"];
        yield return [777, "pending-click", "Unknown"];
        yield return [755, "secondary", "Unknown"];
        yield return [777, "secondary", "Unknown"];
        yield return [755, "cooldown", "Success"];
        yield return [777, "cooldown", "Success"];
        yield return [755, "adventure", "Success"];
        yield return [777, "adventure", "Success"];
        yield return [765, "spectator", "Success"];
        yield return [768, "spectator", "Consume"];
        yield return [777, "spectator", "Consume"];
        yield return [777, "disabled-item", "Fail"];
        yield return [755, "tagged-stick", "Success"];
        yield return [777, "tagged-stick", "Success"];
        yield return [755, "patched-candle", "Success"];
        yield return [766, "patched-candle", "Success"];
        yield return [777, "patched-candle", "Success"];
    }

    [Theory]
    [MemberData(nameof(ContextRows))]
    public async Task KnownPrecedenceAndUnresolvedContextKeepTheirSeparateOutcomes(int protocol, string context, string expected)
    {
        Hand hand = context == "open-off" ? Hand.Off : Hand.Main;
        await using Session session = await Session.Create(protocol, "cake", observeWrites: true);
        if (context != "missing-abilities")
            await Abilities(session, 0);
        if (protocol < 767)
            await Info(session, GameMode.Survival);
        string item = context == "tagged-stick" ? "stick" : "candle";
        await Hold(session, hand, item, 3, patched: context == "patched-candle");
        if (context != "unknown-tag")
            await Tags(session, context == "tag-excluded" ? [] : [item], blockOnly: context == "block-tag-only");
        int bites = context == "bites-one" ? 1 : context == "bites-six" ? 6 : 0;
        await SetCake(session, bites);
        if (context == "missing-identity")
            await session.Client.InvokeAsync(client => { client.State.Self.Uuid = Guid.Empty; return true; }, session.Token);
        if (context == "missing-inventory")
            await session.Client.InvokeAsync(client => { client.State.Features.Inventory = false; return true; }, session.Token);
        if (context.StartsWith("open-", StringComparison.Ordinal))
        {
            await session.Send(new ClientboundOpenScreenPacket(7, 0, Umpk.Text.Component.Text("Candle context"), null, 0, null));
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.Inventory.HasOpenContainer);
        }
        if (context == "pending-click")
        {
            Task<ClickResult> click = await session.Client.InvokeAsync(client => client.Actions.Inventory.ClickAsync(new ClickAction.Pickup(9, MouseButton.Left), session.Token), session.Token);
            await click.WaitAsync(session.Token);
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.Inventory.HasPendingPrediction);
        }
        if (context == "secondary")
        {
            await session.Client.Actions.Movement.SetSneakingAsync(true, session.Token);
            Assert.Empty(await session.Barrier());
        }
        if (context == "cooldown")
            await session.Cooldown();
        if (context == "adventure")
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context == "spectator")
            await session.Mode(GameMode.Spectator, instantBuild: false);
        if (context == "disabled-item")
        {
            await session.Reconfigure([]);
            await session.RejoinWorld("cake");
            await Hold(session, hand, "candle", 3);
        }
        if (context == "tagged-stick")
            await Health(session, 18, 0);
        bool attachment = expected == "Success" && context != "tagged-stick" && context != "spectator";
        int state = attachment ? Attached(session, "candle") : Cake(session, context == "tagged-stick" ? 1 : bites);
        await Use(session, hand, Enum.Parse<BlockUseResult>(expected), attachment ? 2 : 3, state,
            food: 20, saturation: context == "tagged-stick" ? .4F : 0, confirm: attachment || context == "tagged-stick", send: context != "disabled-item");
        if (context == "patched-candle")
            Assert.False(Held(session, hand).Components.HasNoPatch);
    }

    [Theory]
    [InlineData(754, Hand.Main)]
    [InlineData(754, Hand.Off)]
    public async Task PredecessorHasNoCandleBranchEvenWhenTheServerCreatesAnItemTag(int protocol, Hand hand)
    {
        await using Session session = await Session.Create(protocol, "cake", observeWrites: true);
        await Abilities(session, 0);
        await Health(session, 18, 0);
        await Hold(session, hand, "stick", 3);
        await Tags(session, ["stick"]);
        Assert.False(session.Client.State.Registries!.Items.TryGet(Identifier.Minecraft("candle"), out _));
        await Use(session, hand, BlockUseResult.Success, 3, Cake(session, 1), food: 20, saturation: .4F, confirm: true);
    }

    [Theory]
    [InlineData(755)]
    [InlineData(759)]
    [InlineData(777)]
    public async Task CancellationBeforeCommitDoesNotConsumeOrProject(int protocol)
    {
        await using Session session = await Ready(protocol, Hand.Main);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), ct: canceled.Token));
        Assert.Empty(await session.Barrier());
        Assert.Equal(3, Held(session, Hand.Main).Count);
        Assert.Equal(Cake(session), Local(session).StateId);
        await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), confirm: true);
    }

    [Theory]
    [InlineData(759)]
    [InlineData(777)]
    public async Task CallerCancellationAtTheFirstPipeAdvanceFinishesTheCommittedGroupExactlyOnce(int protocol)
    {
        await using Session session = await Ready(protocol, Hand.Main);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Receipt use = await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), deferAck: true, caller: caller.Token, beforeWrite: caller.Cancel);
        Assert.True(caller.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => use.Action.WaitAsync(session.Token));
        await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence!.Value));
        Assert.Empty(await session.Barrier());
        Assert.Equal(2, Held(session, Hand.Main).Count);
        Assert.Equal(20, session.Client.State.Self.Food);
    }

    [Theory]
    [InlineData(755)]
    [InlineData(777)]
    public async Task ActualOwnedPipeFaultKeepsCountAndBlockMutationBeforeTheFailureWithoutRetry(int protocol)
    {
        await using Session session = await Ready(protocol, Hand.Main);
        await session.Server.ClientPipe.Output.CompleteAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), ct: session.Token));
        Assert.Equal(2, Held(session, Hand.Main).Count);
        Assert.Equal(Attached(session, "candle"), Local(session).StateId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), ct: session.Token));
        Assert.Equal(2, Held(session, Hand.Main).Count);
        Assert.Equal(Attached(session, "candle"), Local(session).StateId);
    }

    [Theory]
    [InlineData(759)]
    [InlineData(777)]
    public async Task BlockAckAndAuthoritativeSlotCorrectionHaveIndependentOwners(int protocol)
    {
        await using Session session = await Ready(protocol, Hand.Main);
        Receipt first = await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), deferAck: true);
        await SetCake(session);
        Assert.Equal(Attached(session, "candle"), Local(session).StateId);
        await session.Send(new ClientboundBlockChangedAckPacket(first.Sequence!.Value));
        await first.Action.WaitAsync(session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Equal(Cake(session), Local(session).StateId);
        Assert.Equal(2, Held(session, Hand.Main).Count);
        await Hold(session, Hand.Main, "candle", 3);
        await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), confirm: true);
    }

    [Theory]
    [InlineData(767)]
    [InlineData(777)]
    public async Task ReplacementPlayerDoesNotReusePriorInstantBuildObservation(int protocol)
    {
        await using Session session = await Ready(protocol, Hand.Main);
        await Abilities(session, 8);
        await Use(session, Hand.Main, BlockUseResult.Success, 3, Attached(session, "candle"), confirm: true);
        await session.Respawn();
        await session.Terrain();
        await SetCake(session);
        await Hold(session, Hand.Main, "candle", 3);
        await Use(session, Hand.Main, BlockUseResult.Unknown, 3, Cake(session));
        await Abilities(session, 0);
        await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), confirm: true);
    }

    [Theory]
    [InlineData(766)]
    [InlineData(777)]
    public async Task ReconfigurationEndsTheOldBlockWaitWithoutSharingItWithTheNewWorld(int protocol)
    {
        await using Session session = await Ready(protocol, Hand.Main);
        Receipt old = await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), deferAck: true);
        await session.Reconfigure(null);
        Assert.False(session.Client.State.HasWorld);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.Action.WaitAsync(session.Token));
        await session.RejoinWorld("cake");
        await Hold(session, Hand.Main, "candle", 3);
        await Abilities(session, 0);
        if (protocol < 767)
            await Info(session, GameMode.Survival);
        Receipt current = await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), deferAck: true);
        Assert.False(current.Action.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.Action.WaitAsync(session.Token));
        Assert.False(current.Action.IsCompleted);
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, Attached(session, "candle")));
        await session.Send(new ClientboundBlockChangedAckPacket(current.Sequence!.Value));
        await current.Action.WaitAsync(session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Equal(2, Held(session, Hand.Main).Count);
    }

    [Fact]
    public async Task ReconnectClearsTagsAndCurrentAbilityPermissionBeforeAnotherAttachment()
    {
        await using Session session = await Ready(777, Hand.Main);
        await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), confirm: true);
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () => { while (true) await session.Server.NextFrameAsync(session.Token); });
        await session.Restart("cake");
        await Hold(session, Hand.Main, "candle", 3);
        await Use(session, Hand.Main, BlockUseResult.Unknown, 3, Cake(session));
        await Tags(session, ["candle"]);
        await Use(session, Hand.Main, BlockUseResult.Unknown, 3, Cake(session));
        await Abilities(session, 0);
        await Use(session, Hand.Main, BlockUseResult.Success, 2, Attached(session, "candle"), confirm: true);
    }

    private static async Task<Session> Ready(int protocol, Hand hand, string candle = "candle", int count = 3, bool physics = false)
    {
        Session session = await Session.Create(protocol, "cake", physics: physics, observeWrites: true);
        try
        {
            await Abilities(session, 0);
            if (protocol < 767)
                await Info(session, GameMode.Survival);
            await Hold(session, hand, candle, count);
            await Tags(session, CandleNames);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static ItemStack Held(Session session, Hand hand) => session.Client.State.Inventory.PlayerSlots[hand == Hand.Main ? 36 + session.Client.State.Self.HeldSlot : 45];
    private static BlockState Local(Session session) => session.Client.State.JukeboxPrediction.GetBlock(session.Client.State.World, Session.Target);
    private static int Cake(Session session, int bites = 0)
    {
        BlockDefinition cake = session.Client.State.Registries!.Blocks[Identifier.Minecraft("cake")].Value;
        return Enumerable.Range(cake.MinStateId, cake.StateCount).Single(id => cake.TryGetPropertyValue(id, "bites", out string value) && value == bites.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private static int Attached(Session session, string candle)
    {
        BlockDefinition cake = session.Client.State.Registries!.Blocks[Identifier.Minecraft(candle + "_cake")].Value;
        Assert.True(cake.TryGetPropertyValue(cake.DefaultStateId, "lit", out string lit));
        Assert.Equal("false", lit);
        return cake.DefaultStateId;
    }
    private static Task<BlockUsePrediction> Preview(Session session, Hand hand) => session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), hand), session.Token);

    private static async Task Hold(Session session, Hand hand, string name, int count, bool patched = false)
    {
        ItemStack item = new(session.Client.State.Registries!.Items[Identifier.Minecraft(name)], count);
        if (patched)
            item = session.Protocol >= 766 ? item.With(DataComponents.Unbreakable, new UnbreakableComponent())
                : item.With(DataComponents.LegacyNbt, new LegacyNbtComponent(new NbtCompound { ["BlockStateTag"] = new NbtCompound { ["lit"] = new NbtString("true") } }));
        int slot = hand == Hand.Main ? 36 + session.Client.State.Self.HeldSlot : 45;
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, slot, item));
        Assert.Empty(await session.Barrier());
        Assert.Equal(item, Held(session, hand));
    }

    private static async Task Tags(Session session, string[] names, bool blockOnly = false)
    {
        int[] ids = names.Select(name => blockOnly ? session.Client.State.Registries!.Blocks[Identifier.Minecraft(name)].NetworkId : session.Client.State.Registries!.Items[Identifier.Minecraft(name)].NetworkId).ToArray();
        var packet = new ClientboundUpdateTagsPacket([new TagRegistry(blockOnly ? RegistryIds.Block : RegistryIds.Item, [new TagEntry(Identifier.Minecraft("candles"), ids)])]);
        BoundPacketCodec codec = Support.BoundDescriptorCodec.Clientbound(session.Protocol, "update_tags");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<ClientboundUpdateTagsPacket>(codec.Decode(bytes, context));
        Identifier registry = blockOnly ? RegistryIds.Block : RegistryIds.Item;
        TagRegistry decodedRegistry;
        if (session.Protocol == 754)
        {
            Identifier[] expectedRegistries = [RegistryIds.Block, RegistryIds.Item, Identifier.Minecraft("fluid"), RegistryIds.EntityType];
            Assert.Equal(4, decoded.Registries.Count);
            Assert.Equal(expectedRegistries, decoded.Registries.Select(group => group.Registry));
            decodedRegistry = Assert.Single(decoded.Registries, group => group.Registry == registry);
            foreach (TagRegistry other in decoded.Registries.Where(group => group.Registry != registry))
                Assert.Empty(other.Tags);
        }
        else
        {
            decodedRegistry = Assert.Single(decoded.Registries);
            Assert.Equal(registry, decodedRegistry.Registry);
        }
        TagEntry tag = Assert.Single(decodedRegistry.Tags);
        Assert.Equal(Identifier.Minecraft("candles"), tag.TagId);
        Assert.Equal(ids, tag.Ids);
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
        Assert.Equal(!blockOnly && names.Contains("candle", StringComparer.Ordinal), session.Client.State.RegistryTags.Contains(RegistryIds.Item, Identifier.Minecraft("candles"), session.Client.State.Registries!.Items.TryGet(Identifier.Minecraft("candle"), out var candle) ? candle.NetworkId : -1));
    }

    private static async Task Info(Session session, GameMode mode, bool update = false)
    {
        Guid uuid = session.Client.State.Self.Uuid;
        if (session.Protocol < 761)
            await session.Send(new ClientboundLegacyPlayerListItemPacket(update ? LegacyPlayerListAction.UpdateGameMode : LegacyPlayerListAction.AddPlayer, [new LegacyPlayerListEntry(uuid, update ? null : "CandleSelf", update ? null : [], (int)mode, 0, null)]));
        else
            await session.Send(new ClientboundPlayerInfoUpdatePacket((update ? (PlayerInfoActions)0 : PlayerInfoActions.AddPlayer) | PlayerInfoActions.UpdateGameMode, [new PlayerInfoEntry(uuid, update ? null : "CandleSelf", update ? null : [], false, null, mode, true, 0, null, 0, true)]));
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.TabList.TryGet(uuid, out TabListEntry? info));
        Assert.Equal(mode, info.GameMode);
    }

    private static async Task RemoveInfo(Session session)
    {
        Guid uuid = session.Client.State.Self.Uuid;
        if (session.Protocol < 761)
            await session.Send(new ClientboundLegacyPlayerListItemPacket(LegacyPlayerListAction.RemovePlayer, [new LegacyPlayerListEntry(uuid, null, null, 0, 0, null)]));
        else
            await session.Send(new ClientboundPlayerInfoRemovePacket([uuid]));
        Assert.Empty(await session.Barrier());
        Assert.False(session.Client.State.TabList.TryGet(uuid, out _));
    }

    private static async Task Abilities(Session session, byte flags)
    {
        await session.Send(new ClientboundPlayerAbilitiesPacket(flags, .05F, .1F));
        Assert.Empty(await session.Barrier());
        Assert.Equal((flags & 8) != 0, session.Client.State.Self.InstantBuild);
    }

    private static async Task Health(Session session, int food, float saturation)
    {
        var packet = new ClientboundSetHealthPacket(20, food, saturation);
        BoundPacketCodec codec = Support.BoundDescriptorCodec.Clientbound(session.Protocol, "set_health");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        Assert.Equal(packet, Assert.IsType<ClientboundSetHealthPacket>(codec.Decode(bytes, context)));
        Assert.Equal(bytes, Encode(codec, codec.Decode(bytes, context), context));
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
        Assert.Equal((food, saturation), (session.Client.State.Self.Food, session.Client.State.Self.Saturation));
    }

    private static async Task SetCake(Session session, int bites = 0)
    {
        await session.Send(new ClientboundBlockUpdatePacket(Session.Target, Cake(session, bites)));
        Assert.Empty(await session.Barrier());
    }

    private static async Task MineOnce(Session session, bool restricted)
    {
        Assert.Empty(await session.Barrier());
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.Up, session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        await session.Tick();
        List<IPacket> animations = await session.Barrier();
        Assert.Equal(restricted ? 2 : 1, animations.Count);
        Assert.All(animations, packet => Assert.IsType<ServerboundSwingPacket>(packet));
        ServerboundPlayerActionPacket start = Assert.Single(session.ObservedOutbound.OfType<ServerboundPlayerActionPacket>(), packet => packet.Action == 0);
        Assert.False(session.Client.State.World.GetBlock(Session.Target).IsAir);
        if (session.Protocol >= 759)
            await session.Send(new ClientboundBlockChangedAckPacket(start.Sequence!.Value));
        await action.WaitAsync(session.Token);
        Assert.Empty(await session.Barrier());
    }

    private readonly record struct AtWrite(int Count, int State, int Food, float Saturation);
    private sealed record Receipt(Task Action, int? Sequence);

    private static async Task<Receipt> Use(Session session, Hand hand, BlockUseResult expected, int count, int state, int food = 20, float saturation = 0, bool confirm = false, bool deferAck = false, bool send = true, CancellationToken? caller = null, Action? beforeWrite = null)
    {
        Assert.Empty(await session.Barrier());
        ItemStack original = Held(session, hand);
        int received = session.Client.State.World.GetBlock(Session.Target).StateId;
        BlockUsePrediction preview = await Preview(session, hand);
        Assert.Equal(original, Held(session, hand));
        var observed = new List<AtWrite>();
        bool armed = true;
        session.BeforeClientWrite = () =>
        {
            if (!armed)
                return;
            armed = false;
            observed.Add(new(Held(session, hand).Count, Local(session).StateId, session.Client.State.Self.Food, session.Client.State.Self.Saturation));
            beforeWrite?.Invoke();
        };
        Task action;
        List<IPacket> packets;
        try
        {
            action = session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), hand, ct: caller ?? session.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            // Disarm before the independent keepalive barrier's own transport write.
            session.BeforeClientWrite = null;
            packets = await session.Barrier();
        }
        finally
        {
            session.BeforeClientWrite = null;
        }
        bool packetSwing = expected == BlockUseResult.Success && session.Protocol != 777;
        Assert.Equal(send ? packetSwing ? 2 : 1 : 0, packets.Count);
        Assert.Equal(send, preview.SendPacket);
        Assert.Equal(expected, preview.Result);
        Assert.Equal(expected == BlockUseResult.Success ? session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None, preview.Swing);
        int? sequence = null;
        if (send)
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal((Session.Target, (int)Direction.Up, (int)hand, .5F, 1F, .5F, false, false), (use.Position, use.Face, use.Hand, use.CursorX, use.CursorY, use.CursorZ, use.Inside, use.WorldBorderHit));
            if (session.Protocol >= 759)
            {
                Assert.True(use.Sequence > 0);
                sequence = use.Sequence;
            }
            if (packetSwing)
                Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
            AtWrite before = Assert.Single(observed);
            Assert.Equal(count, before.Count);
            Assert.Equal(state, before.State);
            Assert.Equal((food, saturation), (before.Food, before.Saturation));
        }
        else
            Assert.Empty(observed);
        Assert.Equal(count, Held(session, hand).Count);
        Assert.Equal(state, Local(session).StateId);
        Assert.Equal((food, saturation), (session.Client.State.Self.Food, session.Client.State.Self.Saturation));
        Assert.Equal(received, session.Client.State.World.GetBlock(Session.Target).StateId);
        if (confirm)
            await session.Send(new ClientboundBlockUpdatePacket(Session.Target, state));
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
