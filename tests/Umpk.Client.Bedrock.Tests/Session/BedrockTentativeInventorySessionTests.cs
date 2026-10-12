using Umpk.Client.Bedrock.Actions;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;
using Fixture = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.OwnershipSession;
using SendGate = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.SendGate;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockTentativeInventorySessionTests
{
    private static readonly BedrockContainerAddress s_player = new(0, 0, null);
    private const int RawRequestId = -101;
    private const string UnavailableMessage = "The raw inventory request requires all retained slots to be authoritative.";
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockItemInstance Item(byte count, int? stackId) => new(306, count, 0, stackId, 0, []);
    private static BedrockInventorySnapshot Snapshot(Fixture fixture) => fixture.Session.InventoryStore.GetSnapshot(s_player)!.Value;

    [Fact]
    public async Task Raw147RefusesTentativeSourceBeforeCapacityAndReleasesAdmittedOwnership()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using Fixture fixture = await CreateAsync(timeout.Token);
        PublishTentative(fixture);
        await AssertRawRefusalAndQualifiedControlAsync(fixture, BedrockItemStackEgress.BuildPlace(RawRequestId, 0, 1, 1, 1, 0), timeout.Token);
    }

    [Theory]
    [InlineData("unrelated")]
    [InlineData("legacy")]
    [InlineData("drop")]
    [InlineData("craft")]
    public async Task Raw147CannotAvoidUncertaintyByUsingAnUnrelatedSlotOrUnsupportedGraph(string shape)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using Fixture fixture = await CreateAsync(timeout.Token);
        PublishTentative(fixture);
        BedrockStackRequestSlot source = new(new(28, null), 0, -4);
        BedrockItemStackRequestPacket packet = shape switch
        {
            "unrelated" => BedrockItemStackEgress.BuildPlace(RawRequestId, 2, 3, 1, 7, 0),
            "legacy" => BedrockItemStackEgress.BuildPlace(RawRequestId, 0, 1, 1, -4, 0),
            "drop" => new([new(RawRequestId, [new(BedrockItemStackActionType.Drop, 3, new BedrockDropPayload(1, source, false))], [], 0)]),
            "craft" => new([new(RawRequestId, [new(BedrockItemStackActionType.CraftCreative, 14, new BedrockCraftCreativePayload(17, 1)), new(BedrockItemStackActionType.Drop, 3, new BedrockDropPayload(1, source, false))], [], 0)]),
            _ => throw new InvalidOperationException("Unknown test request shape."),
        };
        Assert.Equal(packet.Requests[0], BedrockItemStackCodecs.DecodeRequest(BedrockItemStackCodecs.Encode(packet)).Requests[0]);
        await AssertRawRefusalAndQualifiedControlAsync(fixture, packet, timeout.Token);
    }

    [Fact]
    public async Task RetiredOverlayLeavesRaw147BlockedUntilNewActualAuthority()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        BedrockInventorySnapshot before = PublishTentative(fixture);
        Assert.True(fixture.Session.InventoryStore.TryRetireTentativeSlot(s_player, 0, before.AuthorityReceipts[0], fixture.Session.InventoryStore.Generation));
        Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
        Assert.False(Snapshot(fixture).IsSlotKnown(0));
        await AssertRawRefusalAndQualifiedControlAsync(fixture, BedrockItemStackEgress.BuildPlace(RawRequestId, 0, 1, 1, 1, 0), ct);

        object receipt = Snapshot(fixture).AuthorityReceipts[0];
        await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(0, 0, s_player, null, Item(64, 1))), ct);
        await WaitAsync(() => Snapshot(fixture).IsSlotKnown(0) && !ReferenceEquals(receipt, Snapshot(fixture).AuthorityReceipts[0]), ct);
        Assert.Equal(Item(64, 1), Snapshot(fixture).Items[0]);
        Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
        SendGate gate = fixture.Transport.HoldNextReservation();
        Task<BedrockItemStackSubmitResult> send = StartRaw(fixture, BedrockItemStackEgress.BuildPlace(-103, 0, 1, 1, 1, 0), -103, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            gate.Allow();
            Assert.Equal(-103, (await send.WaitAsync(ct)).RequestId);
            Assert.Equal(-103, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            await RejectAsync(fixture, -103, ct);
            AssertReleased(fixture);
        }
        finally
        {
            gate.Allow();
        }
    }

    [Fact]
    public async Task IncompleteAcceptedResponseUnavailabilityAlsoBlocksRawEntry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        BedrockItemStackSubmitResult admitted = await fixture.Session.SubmitInventoryPlaceAsync(s_player, 0, 1, 1, ct);
        Assert.Equal(admitted.RequestId, (await fixture.ReceiveRequestAsync(ct)).RequestId);
        await RespondAsync(fixture, new(0, admitted.RequestId, true, [new(new(28, null), [new(1, 1, 1, 9, string.Empty, string.Empty, 0)])]), ct);
        Assert.False(Snapshot(fixture).IsSlotKnown(0));
        Assert.Equal(Item(64, 1), Snapshot(fixture).Items[0]);
        Assert.True(Snapshot(fixture).IsSlotKnown(1));
        Assert.Null(fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0));
        await AssertRawRefusalAndQualifiedControlAsync(fixture, BedrockItemStackEgress.BuildPlace(RawRequestId, 0, 1, 1, 1, 9), ct);
    }

    [Fact]
    public async Task DuplicateRawEntryCannotReleaseAnAlreadyActiveIncumbent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await CreateAsync(ct);
        BedrockItemStackRequestPacket packet = BedrockItemStackEgress.BuildPlace(RawRequestId, 2, 3, 1, 7, 0);
        int commits = fixture.Transport.CommittedCount;
        SendGate gate = fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> incumbent = StartRaw(fixture, packet, RawRequestId, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            PublishTentative(fixture);
            BedrockInventorySnapshot current = Snapshot(fixture);
            BedrockTentativeInventorySnapshot? tentative = fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0);
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SendItemStackRequestAsync(packet, RawRequestId, fixture.Session.ItemStackTracker.Generation, ct).AsTask());
            Assert.Equal("The inventory request already has an active send.", error.Message);
            Assert.False(incumbent.IsCompleted);
            Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
            Assert.Equal(1, fixture.Session.ActionCoordinator.ClaimedCount);
            Assert.True(fixture.Session.ActionCoordinator.IsClaimed(RawRequestId));
            Assert.Equal(commits, fixture.Transport.CommittedCount);
            AssertUnchanged(fixture, current, tentative);
            gate.Allow();
            Assert.Equal(RawRequestId, (await incumbent.WaitAsync(ct)).RequestId);
            Assert.Equal(packet.Requests[0], await fixture.ReceiveRequestAsync(ct));
            Assert.Equal(commits + 1, fixture.Transport.CommittedCount);
            Assert.Single(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            await RejectAsync(fixture, RawRequestId, ct);
            AssertReleased(fixture);
            AssertUnchanged(fixture, current, tentative);
        }
        finally
        {
            gate.Allow();
        }
    }

    private static async Task<Fixture> CreateAsync(CancellationToken ct)
    {
        Fixture fixture = await Fixture.CreateAsync(ct);
        try
        {
            object receipt = Snapshot(fixture).AuthorityReceipts[0];
            BedrockItemInstance[] items = Enumerable.Repeat(Air(), 36).ToArray();
            items[0] = Item(64, 1);
            items[2] = Item(5, 7);
            await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, items, s_player, Air())), ct);
            await WaitAsync(() => !ReferenceEquals(receipt, Snapshot(fixture).AuthorityReceipts[0]), ct);
            Assert.Equal(items, Snapshot(fixture).Items.ToArray());
            Assert.True(Snapshot(fixture).IsSlotKnown(0));
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private static BedrockInventorySnapshot PublishTentative(Fixture fixture)
    {
        BedrockInventorySnapshot before = Snapshot(fixture);
        // Test-only Store state models an unavailable remainder; no outbound operation or authority is inferred.
        Assert.True(fixture.Session.InventoryStore.TryPublishTentativeSlot(before, 0, Item(63, null), -4, fixture.Session.InventoryStore.Generation));
        BedrockTentativeInventorySnapshot tentative = fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0)!.Value;
        Assert.Equal(Item(64, 1), Snapshot(fixture).Items[0]);
        Assert.Equal(Item(63, null), tentative.TentativeItem);
        Assert.Equal(-4, tentative.LegacyRequestId);
        Assert.Same(before.AuthorityReceipts[0], Snapshot(fixture).AuthorityReceipts[0]);
        Assert.False(Snapshot(fixture).IsSlotKnown(0));
        return before;
    }

    private static Task<BedrockItemStackSubmitResult> StartRaw(Fixture fixture, BedrockItemStackRequestPacket packet, int requestId, CancellationToken ct)
    {
        int generation = fixture.Session.ItemStackTracker.Generation;
        Assert.True(fixture.Session.ActionCoordinator.TryClaim(requestId, BedrockActionEmission.Standalone));
        Assert.True(fixture.Session.ItemStackTracker.TryRegister(requestId, generation));
        return fixture.Session.SendItemStackRequestAsync(packet, requestId, generation, ct).AsTask();
    }

    private static async Task AssertRawRefusalAndQualifiedControlAsync(Fixture fixture, BedrockItemStackRequestPacket packet, CancellationToken ct)
    {
        BedrockInventorySnapshot before = Snapshot(fixture);
        BedrockTentativeInventorySnapshot? tentative = fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0);
        int commits = fixture.Transport.CommittedCount;
        int outbound = fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
        SendGate gate = fixture.Transport.HoldNextReservation();
        Task<BedrockItemStackSubmitResult> raw = StartRaw(fixture, packet, RawRequestId, ct);
        try
        {
            Task first = await Task.WhenAny(raw, gate.Entered).WaitAsync(ct);
            if (ReferenceEquals(first, gate.Entered) || gate.Entered.IsCompleted)
            {
                // Join the actual current-implementation send before reporting the expected behavioral red failure.
                gate.Allow();
                _ = await raw.WaitAsync(ct);
                Assert.Equal(RawRequestId, (await fixture.ReceiveRequestAsync(ct)).RequestId);
                await RejectAsync(fixture, RawRequestId, ct);
                Assert.Fail("Raw147 reached transport capacity while retained inventory data was unavailable.");
            }
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => raw);
            Assert.Equal(UnavailableMessage, error.Message);
            Assert.False(gate.Entered.IsCompleted);
            Assert.Equal(commits, fixture.Transport.CommittedCount);
            Assert.Equal(outbound, fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147));
            AssertReleased(fixture);
            AssertUnchanged(fixture, before, tentative);

            Task<BedrockItemStackSubmitResult> qualified = fixture.Session.SubmitInventoryPlaceAsync(s_player, 2, 3, 1, ct).AsTask();
            await gate.Entered.WaitAsync(ct);
            Assert.False(qualified.IsCompleted);
            gate.Allow();
            BedrockItemStackSubmitResult transferred = await qualified.WaitAsync(ct);
            BedrockItemStackRequest received = await fixture.ReceiveRequestAsync(ct);
            Assert.Equal(transferred.RequestId, received.RequestId);
            BedrockPlacePayload place = Assert.IsType<BedrockPlacePayload>(Assert.Single(received.Actions).Payload);
            Assert.Equal((byte)2, place.Source.Slot);
            Assert.Equal((byte)3, place.Destination.Slot);
            Assert.Equal(7, place.Source.StackId);
            await RespondAsync(fixture, new(0, transferred.RequestId, true, [new(new(28, null), [new(2, 2, 4, 7, string.Empty, string.Empty, 0), new(3, 3, 1, 9, string.Empty, string.Empty, 0)])]), ct);
            AssertReleased(fixture);
            BedrockInventorySnapshot after = Snapshot(fixture);
            Assert.Equal(Item(4, 7), after.Items[2]);
            Assert.Equal(Item(1, 9), after.Items[3]);
            Assert.True(after.IsSlotKnown(2));
            Assert.True(after.IsSlotKnown(3));
            Assert.Equal(before.Items[0], after.Items[0]);
            Assert.Equal(before.IsSlotKnown(0), after.IsSlotKnown(0));
            Assert.Same(before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
            AssertTentative(fixture, tentative);
        }
        finally
        {
            gate.Allow();
        }
    }

    private static async Task RejectAsync(Fixture fixture, int requestId, CancellationToken ct) => await RespondAsync(fixture, new(1, requestId, false, []), ct);

    private static async Task RespondAsync(Fixture fixture, BedrockItemStackResponse response, CancellationToken ct)
    {
        int outcomes = fixture.Session.ItemStackTracker.AcceptedCount + fixture.Session.ItemStackTracker.RejectedCount;
        await fixture.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([response])), ct);
        await WaitAsync(() => fixture.Session.ItemStackTracker.AcceptedCount + fixture.Session.ItemStackTracker.RejectedCount > outcomes
            && fixture.Session.ItemStackTracker.PendingCount == 0 && fixture.Session.ActionCoordinator.ClaimedCount == 0, ct);
    }

    private static void AssertReleased(Fixture fixture)
    {
        Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
        Assert.Equal(0, fixture.Session.PendingItemStackReconciliationCount);
    }

    private static void AssertUnchanged(Fixture fixture, BedrockInventorySnapshot before, BedrockTentativeInventorySnapshot? tentative)
    {
        BedrockInventorySnapshot after = Snapshot(fixture);
        Assert.Equal(before.Items.ToArray(), after.Items.ToArray());
        Assert.Equal(before.StorageItem, after.StorageItem);
        Assert.Equal(before.KnownSlots.ToArray(), after.KnownSlots.ToArray());
        Assert.Equal(before.ResponseCorrections.ToArray(), after.ResponseCorrections.ToArray());
        for (int slot = 0; slot < before.Items.Length; slot++)
            Assert.Same(before.AuthorityReceipts[slot], after.AuthorityReceipts[slot]);
        AssertTentative(fixture, tentative);
    }

    private static void AssertTentative(Fixture fixture, BedrockTentativeInventorySnapshot? expected)
    {
        BedrockTentativeInventorySnapshot? actual = fixture.Session.InventoryStore.GetTentativeSnapshot(s_player, 0);
        if (expected is not { } retained)
        {
            Assert.Null(actual);
            return;
        }
        BedrockTentativeInventorySnapshot current = Assert.IsType<BedrockTentativeInventorySnapshot>(actual);
        Assert.Equal(retained.Address, current.Address);
        Assert.Equal(retained.Slot, current.Slot);
        Assert.Equal(retained.Generation, current.Generation);
        Assert.Equal(retained.LegacyRequestId, current.LegacyRequestId);
        Assert.Equal(retained.AuthorityItem, current.AuthorityItem);
        Assert.Equal(retained.TentativeItem, current.TentativeItem);
        Assert.Equal(retained.AuthorityCorrection, current.AuthorityCorrection);
        Assert.Same(retained.AuthorityReceipt, current.AuthorityReceipt);
    }

    private static async Task WaitAsync(Func<bool> predicate, CancellationToken ct)
    {
        while (!predicate())
            await Task.Delay(10, ct);
    }
}
