using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Xunit;
using Fixture = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.OwnershipSession;
using SendGate = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.SendGate;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockCreativeCursorSessionTests
{
    private static readonly BedrockContainerAddress s_pool = new(124, 0, null);
    private static readonly BedrockContainerAddress s_inventory = new(0, 0, null);
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockItemRegistryPacket Registry(string name = "minecraft:oak_planks") => new([
        new BedrockItemState(name, 5, false, 2, new NbtCompound()),
        new BedrockItemState("minecraft:diamond", 306, false, 2, new NbtCompound()),
    ]);
    private static BedrockCreativeContentPacket Catalog() => new([], [new BedrockCreativeEntry(17, new(5, 1, 0, 1921718966, new byte[10]), 0)]);
    private static BedrockItemStackRequestPacket Request(BedrockCreativeEntry entry, int id = -3, string name = "minecraft:oak_planks") => new([new BedrockItemStackRequest(id,
    [
        new(BedrockItemStackActionType.CraftCreative, 14, new BedrockCraftCreativePayload(entry.EntryId, 1)),
        new(BedrockItemStackActionType.ResultsDeprecated, 19, new BedrockResultsDeprecatedPayload([new(1, 1, name, checked((int)entry.Item.MetadataValue), 1, checked((uint)entry.Item.BlockRuntimeId), entry.Item.ExtraData)], 1)),
        new(BedrockItemStackActionType.Take, 0, new BedrockTakePayload(64, new(new(60, null), 50, id), new(new(59, null), 0, 0))),
    ], [], -1)]);

    private static async Task<Fixture> CreateAsync(CancellationToken ct, string registryName = "minecraft:oak_planks", bool pool = true, bool screen = true, bool twoRegistries = false, BedrockItemRegistryPacket? registry = null, BedrockCreativeContentPacket? catalog = null)
    {
        Fixture fixture = await Fixture.CreateAsync(ct, twoRegistries ? [Registry("minecraft:stone"), registry ?? Registry(registryName)] : [registry ?? Registry(registryName)]);
        try
        {
            await fixture.SendAsync(145, BedrockCreativeContentCodecs.Encode(catalog ?? Catalog()), ct);
            await WaitAsync(() => fixture.Session.LastCreativeContent is not null, ct);
            if (pool)
                await SendPoolAsync(fixture, ct);
            if (screen)
                await OpenAsync(fixture, ct);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private static async Task SendPoolAsync(Fixture fixture, CancellationToken ct, int count = 54, BedrockItemInstance? cursor = null)
    {
        BedrockItemInstance[] items = Enumerable.Range(0, count).Select(_ => Air()).ToArray();
        if (count > 0)
            items[0] = cursor ?? Air();
        if (count > 7)
            items[7] = new(306, 2, 0, 11, 0, [1, 2]);
        await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(124, items, s_pool, Air())), ct);
        await WaitAsync(() => fixture.Session.InventoryStore.GetSnapshot(s_pool) is { } saved && saved.Items.Length == count && (count == 0 || saved.IsSlotKnown(0) && saved.Items[0].Equals(items[0])), ct);
    }

    private static async Task OpenAsync(Fixture fixture, CancellationToken ct, int window = 2, sbyte type = -1)
    {
        await fixture.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(checked((sbyte)window), type, 0, 0, 0, -1)), ct);
        await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(window) is not null, ct);
    }

    private static Task<BedrockItemStackSubmitResult> Submit(Fixture fixture, CancellationToken ct, int id = -3)
    {
        BedrockCreativeEntry entry = Assert.Single(fixture.Session.LastCreativeContent!.Entries);
        return fixture.Session.SubmitObservedCreativeCursorRequestAsync(entry, Request(entry, id), ct).AsTask();
    }

    private static Task RespondAsync(Fixture fixture, CancellationToken ct, int id = -3, byte status = 0, params BedrockItemStackResponseContainer[] containers)
        => RespondAsync(fixture, ct, id, status, 0, containers);

    private static async Task RespondAsync(Fixture fixture, CancellationToken ct, int id, byte status, int expectedPending, params BedrockItemStackResponseContainer[] containers)
    {
        long prior = fixture.Session.ItemStackTracker.AcceptedCount + fixture.Session.ItemStackTracker.RejectedCount + fixture.Session.ItemStackTracker.UnknownCount + fixture.Session.ItemStackTracker.DuplicateCount;
        await fixture.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new(status, id, status == 0, containers)])), ct);
        await WaitAsync(() => fixture.Session.ItemStackTracker.AcceptedCount + fixture.Session.ItemStackTracker.RejectedCount + fixture.Session.ItemStackTracker.UnknownCount + fixture.Session.ItemStackTracker.DuplicateCount > prior && HasPendingOwnership(fixture, expectedPending), ct);
    }
    private static BedrockItemStackResponseContainer Cursor(byte count = 64, int? id = 1) => new(new(59, null), [new(0, 0, count, id, "named", "filtered", 9)]);
    // The Session-gated getter fences the complete transition before independently locked tracker and coordinator reads.
    private static bool HasPendingOwnership(Fixture fixture, int expected) => fixture.Session.PendingItemStackReconciliationCount == expected
        && fixture.Session.ItemStackTracker.PendingCount == expected && fixture.Session.ActionCoordinator.ClaimedCount == expected;
    private static bool IsReleased(Fixture fixture) => HasPendingOwnership(fixture, 0);
    private static void Released(Fixture fixture)
    {
        Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(0, fixture.Session.PendingItemStackReconciliationCount);
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(63)]
    [InlineData(0)]
    public async Task AcceptedCursorRetainsFullCatalogItemCorrectionsWithoutContentRefresh(byte count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        BedrockInventorySnapshot before = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value;
        BedrockCreativeEntry entry = Assert.Single(fixture.Session.LastCreativeContent!.Entries);
        int contentCount = fixture.Session.WireTranscript.Count(row => row.PacketId == 49 && row.Direction == BedrockWireDirection.Inbound);
        byte[] expected = BedrockItemStackCodecs.Encode(Request(entry));
        BedrockItemStackSubmitResult submitted = await Submit(fixture, ct);
        Assert.Equal(-3, submitted.RequestId);
        Assert.Equal(expected, submitted.OutboundPayload);
        Assert.Equal(Request(entry).Requests[0], await fixture.ReceiveRequestAsync(ct));
        Assert.Equal(1, fixture.Session.PendingItemStackReconciliationCount);
        entry.Item.ExtraData[0] = 23;
        await RespondAsync(fixture, ct, -3, 0, Cursor(count));
        BedrockInventorySnapshot after = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value;
        Assert.True(after.IsSlotKnown(0));
        Assert.Equal(count == 0 ? Air() : new BedrockItemInstance(5, count, 0, 1, 1921718966, new byte[10]), after.Items[0]);
        Assert.Equal(Cursor(count).Slots[0], after.ResponseCorrections[0]);
        Assert.Equal(before.Items[7], after.Items[7]);
        Assert.True(after.IsSlotKnown(7));
        Assert.Equal(contentCount, fixture.Session.WireTranscript.Count(row => row.PacketId == 49 && row.Direction == BedrockWireDirection.Inbound));
        Released(fixture);
    }

    [Fact]
    public async Task LastOfTwoActualBootstrapRegistriesOwnsCreativeIdentity()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct, twoRegistries: true);
        Assert.Equal(-3, (await Submit(fixture, ct)).RequestId);
        await RespondAsync(fixture, ct, -3, 0, Cursor());
        Assert.Equal(5, fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.Items[0].NetworkId);
    }

    [Theory]
    [InlineData("pool")]
    [InlineData("screen")]
    [InlineData("size")]
    [InlineData("nonempty")]
    [InlineData("registry")]
    [InlineData("descriptor")]
    [InlineData("entry")]
    [InlineData("screen-type")]
    public async Task UnqualifiedObservedRequestRejectsBeforeEmission(string missing)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct, missing == "registry" ? "minecraft:stone" : "minecraft:oak_planks", pool: missing != "pool", screen: missing is not ("screen" or "screen-type"));
        if (missing == "size") await SendPoolAsync(fixture, ct, 53);
        if (missing == "nonempty") await SendPoolAsync(fixture, ct, cursor: new(5, 1, 0, 1, 1921718966, new byte[10]));
        if (missing == "screen-type") await OpenAsync(fixture, ct, type: 0);
        BedrockCreativeEntry entry = Assert.Single(fixture.Session.LastCreativeContent!.Entries);
        if (missing == "entry") entry = new(entry.EntryId, entry.Item, entry.GroupIndex);
        BedrockItemStackRequestPacket packet = Request(entry, name: missing == "descriptor" ? "minecraft:stone" : "minecraft:oak_planks");
        int commits = fixture.Transport.CommittedCount;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitObservedCreativeCursorRequestAsync(entry, packet, ct).AsTask());
        Assert.Equal(commits, fixture.Transport.CommittedCount);
        Released(fixture);
    }

    [Fact]
    public async Task RequestIdMismatchBurnsAllocatorValueWithoutRewritingSuppliedRequest()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, ct, -5));
        Released(fixture);
        Assert.Equal(-5, (await Submit(fixture, ct, -5)).RequestId);
        Assert.Equal(-5, (await fixture.ReceiveRequestAsync(ct)).RequestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreativeAndHotbarShareSingleOutstandingAdmission(bool creativeFirst)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        if (creativeFirst)
        {
            await Submit(fixture, ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitInventoryPlaceAsync(s_inventory, 0, 1, 1, ct).AsTask());
        }
        else
        {
            await fixture.Session.SubmitInventoryPlaceAsync(s_inventory, 0, 1, 1, ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, ct));
        }
        Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(1, fixture.Session.ActionCoordinator.ClaimedCount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-container")]
    [InlineData("no-id")]
    [InlineData("conflict")]
    public async Task CurrentAcceptedIncompleteCursorOnlyRetiresAffectedSlot(string invalid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        BedrockItemInstance unrelated = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.Items[7];
        await Submit(fixture, ct);
        BedrockItemStackResponseContainer[] containers = invalid switch
        {
            "missing" => [],
            "wrong-container" => [new(new(60, null), Cursor().Slots)],
            "no-id" => [Cursor(id: null)],
            _ => [new(new(59, null), [Cursor().Slots[0], Cursor(63).Slots[0]])],
        };
        await RespondAsync(fixture, ct, -3, 0, containers);
        BedrockInventorySnapshot after = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value;
        Assert.False(after.IsSlotKnown(0));
        Assert.True(after.IsSlotKnown(7));
        Assert.Equal(unrelated, after.Items[7]);
        Released(fixture);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, ct, -5));
    }

    [Theory]
    [InlineData("close")]
    [InlineData("catalog")]
    [InlineData("registry")]
    [InlineData("life")]
    [InlineData("world")]
    [InlineData("terminal")]
    public async Task CreativeCapacityWaitRetiresAtActualOwnerBoundary(string boundary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextReservation();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        int commits = fixture.Transport.CommittedCount;
        try
        {
            await gate.Entered.WaitAsync(ct);
            switch (boundary)
            {
                case "close": await CloseAsync(fixture, ct); break;
                case "catalog": await ReplaceCatalogAsync(fixture, ct); break;
                case "registry": await fixture.SendAsync(162, BedrockItemRegistryCodec.Encode(Registry()), ct); break;
                case "life": fixture.Session.HandleRespawn(); break;
                case "world": fixture.Session.HandleWorldReplacement(); break;
                case "terminal": await fixture.Session.DisconnectAsync(ct); break;
            }
            if (boundary == "terminal")
                await Assert.ThrowsAsync<BedrockSessionException>(() => submit.WaitAsync(TimeSpan.FromSeconds(2), ct));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => submit.WaitAsync(TimeSpan.FromSeconds(2), ct));
            Assert.Equal(commits, fixture.Transport.CommittedCount);
            Released(fixture);
        }
        finally
        {
            gate.Allow();
            try { await submit.WaitAsync(TimeSpan.FromSeconds(5), ct); }
            catch (Exception exception) when (exception is BedrockSessionException or InvalidOperationException) { }
        }
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("same-screen")]
    [InlineData("foreign-screen")]
    [InlineData("nonnormal-screen")]
    public async Task RetiredOwnerLateResponseCannotChangeFreshPoolOrReuseStaleBaseline(string boundary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        await Submit(fixture, ct);
        if (boundary == "catalog") await ReplaceCatalogAsync(fixture, ct);
        else await OpenAsync(fixture, ct, window: boundary == "foreign-screen" ? 3 : 2, type: boundary == "nonnormal-screen" ? (sbyte)0 : (sbyte)-1);
        await WaitAsync(() => fixture.Session.ItemStackTracker.PendingCount == 0, ct);
        Assert.False(fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.IsSlotKnown(0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, ct, -5));
        await SendPoolAsync(fixture, ct);
        await RespondAsync(fixture, ct, -3, 0, Cursor());
        BedrockInventorySnapshot after = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value;
        Assert.True(after.IsSlotKnown(0));
        Assert.Equal(Air(), after.Items[0]);
        Released(fixture);
        if (boundary == "nonnormal-screen")
            await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, ct, -5));
        else
            Assert.Equal(-5, (await Submit(fixture, ct, -5)).RequestId);
    }

    [Fact]
    public async Task PostReadyRegistryRetiresCreativeUntilReconnectWithoutRetiringHotbar()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        await fixture.SendAsync(162, BedrockItemRegistryCodec.Encode(Registry()), ct);
        await WaitAsync(() => !fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.IsSlotKnown(0), ct);
        await SendPoolAsync(fixture, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, ct));
        Assert.Equal(-3, (await fixture.Session.SubmitInventoryPlaceAsync(s_inventory, 0, 1, 1, ct)).RequestId);
        await fixture.AcceptAsync(-3, ct);
        Assert.Equal(4, fixture.Session.InventoryStore.GetSnapshot(s_inventory)!.Value.Items[0].Count);
    }

    [Fact]
    public async Task RejectedUnknownAndDuplicateResponsesDoNotInventCursorOrRetainClaims()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        await Submit(fixture, ct);
        await RespondAsync(fixture, ct, -5, 0, 1, Cursor());
        Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
        await RespondAsync(fixture, ct, -3, 7);
        Released(fixture);
        Assert.True(fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.IsSlotKnown(0));
        await RespondAsync(fixture, ct, -3, 0, Cursor());
        Assert.Equal(Air(), fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.Items[0]);
        Released(fixture);
    }

    [Theory]
    [InlineData("caller", false)]
    [InlineData("caller", true)]
    [InlineData("catalog", false)]
    [InlineData("catalog", true)]
    [InlineData("close", false)]
    [InlineData("close", true)]
    [InlineData("life", false)]
    [InlineData("life", true)]
    public async Task GrantedCreativeCommitAccountsAfterCancellationWithoutRevivingRetiredOwners(string boundary, bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        using var caller = new CancellationTokenSource();
        SendGate gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, caller.Token);
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (afterTransfer) Assert.Equal(-3, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            switch (boundary)
            {
                case "caller": await caller.CancelAsync(); break;
                case "catalog": await ReplaceCatalogAsync(fixture, ct); break;
                case "close": await CloseAsync(fixture, ct); break;
                case "life": fixture.Session.HandleRespawn(); break;
            }
            Assert.False(submit.IsCompleted);
            gate.Allow();
            Assert.Equal(-3, (await submit.WaitAsync(TimeSpan.FromSeconds(5), ct)).RequestId);
            if (!afterTransfer) Assert.Equal(-3, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            Assert.Single(fixture.Session.WireTranscript, row => row.PacketId == 147 && row.Direction == BedrockWireDirection.Outbound);
            if (boundary == "caller")
            {
                Assert.Equal(1, fixture.Session.PendingItemStackReconciliationCount);
                await RespondAsync(fixture, ct, -3, 0, Cursor());
                Assert.Equal(64, fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.Items[0].Count);
            }
            else
            {
                Released(fixture);
                await SendPoolAsync(fixture, ct);
                await RespondAsync(fixture, ct, -3, 0, Cursor());
                Assert.True(fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.IsSlotKnown(0));
                Assert.Equal(Air(), fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.Items[0]);
            }
            Released(fixture);
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("registry")]
    [InlineData("screen")]
    public async Task CreativeOwnerChangesDoNotCancelOutstandingGenericHotbarRequest(string owner)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        await fixture.Session.SubmitInventoryPlaceAsync(s_inventory, 0, 1, 1, ct);
        if (owner == "catalog") await ReplaceCatalogAsync(fixture, ct);
        else if (owner == "registry")
        {
            await fixture.SendAsync(162, BedrockItemRegistryCodec.Encode(Registry()), ct);
            await WaitAsync(() => !fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.IsSlotKnown(0), ct);
        }
        else
        {
            await OpenAsync(fixture, ct, window: 3);
            await WaitAsync(() => !fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.IsSlotKnown(0), ct);
        }
        Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(1, fixture.Session.PendingItemStackReconciliationCount);
        Assert.Equal(1, fixture.Session.ActionCoordinator.ClaimedCount);
        await fixture.AcceptAsync(-3, ct);
        Assert.Equal(4, fixture.Session.InventoryStore.GetSnapshot(s_inventory)!.Value.Items[0].Count);
        Released(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualCreativeCursorReplacementPreservesKnownDataAndUnrelatedUpdate(bool affectedChanged)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        await Submit(fixture, ct);
        BedrockItemInstance[] items = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.Items.ToArray();
        if (affectedChanged) items[0] = new(5, 1, 0, 42, 1921718966, new byte[10]);
        items[7] = new(306, 3, 0, 11, 0, [1, 2]);
        await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(124, items, s_pool, Air())), ct);
        await WaitAsync(() => fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.Items[7].Count == 3, ct);
        BedrockInventorySnapshot newer = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value;
        await RespondAsync(fixture, ct, -3, 0, Cursor());
        BedrockInventorySnapshot after = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value;
        Assert.True(after.IsSlotKnown(0));
        Assert.Equal(items[0].Count, after.Items[0].Count);
        Assert.Equal(newer.Items[0], after.Items[0]);
        Assert.Same(newer.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Equal(newer.ResponseCorrections[0], after.ResponseCorrections[0]);
        Assert.True(after.IsSlotKnown(7));
        Assert.Equal(items[7], after.Items[7]);
        Assert.Same(newer.AuthorityReceipts[7], after.AuthorityReceipts[7]);
        Assert.Equal(newer.ResponseCorrections[7], after.ResponseCorrections[7]);
        Released(fixture);
    }

    [Theory]
    [InlineData("components")]
    [InlineData("component-based")]
    [InlineData("version")]
    [InlineData("metadata")]
    [InlineData("opaque")]
    public async Task UnobservedOakDefinitionOrRepresentationRejectsBeforeCapacity(string mismatch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        BedrockItemRegistryPacket registry = Registry();
        BedrockItemState oak = registry.Items[0];
        var components = new NbtCompound();
        if (mismatch == "components") components.PutInt("max_stack_size", 1);
        oak = oak with { Components = components, ComponentBased = mismatch == "component-based", Version = mismatch == "version" ? 3 : 2 };
        registry = new([oak, registry.Items[1]]);
        byte[] extra = new byte[10];
        if (mismatch == "opaque") extra[0] = 1;
        var catalog = new BedrockCreativeContentPacket([], [new BedrockCreativeEntry(17, new(5, 1, mismatch == "metadata" ? 1U : 0U, 1921718966, extra), 0)]);
        await using Fixture fixture = await CreateAsync(ct, registry: registry, catalog: catalog);
        int commits = fixture.Transport.CommittedCount;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, ct));
        Assert.Equal(commits, fixture.Transport.CommittedCount);
        Released(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedCursorCancelsCreativeCapacityWaitAndPreservesAuthoritativeReplacement(bool slotPacket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextReservation();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        int commits = fixture.Transport.CommittedCount;
        var replacement = new BedrockItemInstance(5, 1, 0, 42, 1921718966, new byte[10]);
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (slotPacket)
                await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(124, 0, s_pool, Air(), replacement)), ct);
            else
                await SendPoolAsync(fixture, ct, cursor: replacement);
            await WaitAsync(() => fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.Items[0].Equals(replacement), ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => submit.WaitAsync(TimeSpan.FromSeconds(1), ct));
            Assert.Equal(commits, fixture.Transport.CommittedCount);
            BedrockInventorySnapshot saved = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value;
            Assert.True(saved.IsSlotKnown(0));
            Assert.Equal(replacement, saved.Items[0]);
            Released(fixture);
        }
        finally
        {
            gate.Allow();
            try { await submit.WaitAsync(TimeSpan.FromSeconds(5), ct); }
            catch (InvalidOperationException) { }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GrantedCreativeResultSurvivesCursorReplacementWhileLateResponsePreservesKnownData(bool slotPacket, bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        SendGate gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        var replacement = new BedrockItemInstance(5, 1, 0, 42, 1921718966, new byte[10]);
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (afterTransfer) Assert.Equal(-3, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            if (slotPacket)
                await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(124, 0, s_pool, Air(), replacement)), ct);
            else
                await SendPoolAsync(fixture, ct, cursor: replacement);
            await WaitAsync(() => fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.Items[0].Equals(replacement) && IsReleased(fixture), ct);
            Released(fixture);
            Assert.False(submit.IsCompleted);
            gate.Allow();
            Assert.Equal(-3, (await submit.WaitAsync(TimeSpan.FromSeconds(5), ct)).RequestId);
            if (!afterTransfer) Assert.Equal(-3, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            Assert.Single(fixture.Session.WireTranscript, row => row.PacketId == 147 && row.Direction == BedrockWireDirection.Outbound);
            await RespondAsync(fixture, ct, -3, 0, Cursor());
            BedrockInventorySnapshot saved = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value;
            Assert.True(saved.IsSlotKnown(0));
            Assert.Equal(replacement, saved.Items[0]);
            Assert.Null(saved.ResponseCorrections[0]);
            Released(fixture);
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    [Fact]
    public async Task UnrelatedActualPoolSlotDoesNotCancelCreativeCapacityWait()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextReservation();
        Task<BedrockItemStackSubmitResult> submit = Submit(fixture, ct);
        var unrelated = new BedrockItemInstance(306, 3, 0, 11, 0, [1, 2]);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(124, 7, s_pool, Air(), unrelated)), ct);
            await WaitAsync(() => fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value.Items[7].Equals(unrelated), ct);
            Assert.False(submit.IsCompleted);
            Assert.Equal(1, fixture.Session.PendingItemStackReconciliationCount);
            Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
            gate.Allow();
            Assert.Equal(-3, (await submit.WaitAsync(TimeSpan.FromSeconds(5), ct)).RequestId);
            await RespondAsync(fixture, ct, -3, 0, Cursor());
            BedrockInventorySnapshot after = fixture.Session.InventoryStore.GetSnapshot(s_pool)!.Value;
            Assert.Equal(64, after.Items[0].Count);
            Assert.True(after.IsSlotKnown(7));
            Assert.Equal(unrelated, after.Items[7]);
            Released(fixture);
        }
        finally
        {
            gate.Allow();
            await submit.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    private static async Task CloseAsync(Fixture fixture, CancellationToken ct)
    {
        await fixture.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(2, -1, true)), ct);
        await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(2) is null && IsReleased(fixture), ct);
    }

    private static async Task ReplaceCatalogAsync(Fixture fixture, CancellationToken ct)
    {
        BedrockCreativeContentPacket prior = fixture.Session.LastCreativeContent!;
        await fixture.SendAsync(145, BedrockCreativeContentCodecs.Encode(Catalog()), ct);
        await WaitAsync(() => !ReferenceEquals(prior, fixture.Session.LastCreativeContent), ct);
    }
    private static async Task WaitAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition()) await Task.Delay(10, ct);
    }
}
