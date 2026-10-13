using System.Collections.Immutable;
using Umpk.Client.Bedrock.Actions;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;
using OwnershipSession = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.OwnershipSession;
using SendGate = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.SendGate;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockFullPlayerEgressSessionTests
{
    private static readonly BedrockContainerAddress s_address = new(0, 0, null);
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockItemInstance Item(ushort count, int? id = 7, short material = 306, uint metadata = 0, byte[]? opaque = null) => new(material, count, metadata, id, 0, opaque ?? []);

    [Theory]
    [InlineData("take", 8, 9, 28, 29, 5, 0, 2)]
    [InlineData("place", 9, 8, 29, 28, 5, 0, 5)]
    [InlineData("take", 9, 35, 29, 29, 5, 3, 2)]
    [InlineData("place", 35, 9, 29, 29, 5, 3, 1)]
    [InlineData("take", 35, 8, 29, 28, 255, 0, 255)]
    [InlineData("place", 8, 35, 28, 29, 255, 254, 1)]
    public async Task TransferPublishesPhysicalPairFromCorrelatedResponse(string operation, byte source, byte destination, byte sourceName, byte destinationName, ushort sourceCount, ushort destinationCount, byte count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemInstance[] items = EmptyItems(36);
        items[source] = Item(sourceCount, 7, opaque: [0x11, 0x22]);
        if (destinationCount != 0)
            items[destination] = Item(destinationCount, 9, opaque: [0x11, 0x22]);
        await PublishAsync(fixture, items, ct);
        await OpenMainAsync(fixture, ct);
        BedrockInventorySnapshot before = Read(fixture);
        int contentCount = fixture.Session.WireTranscript.Count(row => row.PacketId == 49);

        BedrockItemStackSubmitResult submit = await Submit(fixture, operation, source, destination, count, ct);
        Assert.Equal(-3, submit.RequestId);
        (BedrockItemStackRequest request, byte[] payload) = await ReceiveRequestAsync(fixture, ct);
        Assert.Equal(submit.OutboundPayload, payload);
        AssertRequest(request, operation, source, destination, sourceName, destinationName, 7, destinationCount == 0 ? 0 : 9, count);
        Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
        Assert.True(fixture.Session.ActionCoordinator.IsClaimed(submit.RequestId));
        Assert.Equal(before.Items[source], Read(fixture).Items[source]);
        Assert.Same(before.AuthorityReceipts[destination], Read(fixture).AuthorityReceipts[destination]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, operation, source, destination, count, ct));
        Assert.Equal(1, fixture.Session.ActionCoordinator.ClaimedCount);

        byte remaining = checked((byte)(sourceCount - count));
        byte resulting = checked((byte)(destinationCount + count));
        await RespondAsync(fixture, submit.RequestId, source, destination, sourceName, destinationName, remaining, resulting, ct);
        BedrockInventorySnapshot after = Read(fixture);
        Assert.True(after.IsSlotKnown(source));
        Assert.True(after.IsSlotKnown(destination));
        Assert.Equal(remaining, after.Items[source].Count);
        Assert.Equal(remaining == 0 ? 0 : 306, after.Items[source].NetworkId);
        Assert.Equal(resulting, after.Items[destination].Count);
        Assert.Equal(306, after.Items[destination].NetworkId);
        Assert.Equal(420, after.Items[destination].StackNetworkId);
        Assert.Equal(new byte[] { 0x11, 0x22 }, after.Items[destination].ExtraData);
        Assert.Equal("accepted", after.ResponseCorrections[source]!.CustomName);
        Assert.Equal("filtered", after.ResponseCorrections[source]!.FilteredCustomName);
        Assert.Equal(17, after.ResponseCorrections[source]!.DurabilityCorrection);
        Assert.Equal(source, after.ResponseCorrections[source]!.Slot);
        Assert.Equal(source, after.ResponseCorrections[source]!.HotbarSlot);
        Assert.NotSame(before.AuthorityReceipts[source], after.AuthorityReceipts[source]);
        Assert.Same(after.AuthorityReceipts[source], after.AuthorityReceipts[destination]);
        Assert.Same(before.AuthorityReceipts[2], after.AuthorityReceipts[2]);
        Assert.Equal(contentCount, fixture.Session.WireTranscript.Count(row => row.PacketId == 49));
        AssertResolved(fixture, 1);
    }

    [Theory]
    [InlineData(8, 9, 28, 29, 3, 5)]
    [InlineData(9, 35, 29, 29, 255, 255)]
    public async Task SwapKeepsDistinctMaterialsAndSupportsResponseByteBoundary(byte source, byte destination, byte sourceName, byte destinationName, ushort sourceCount, ushort destinationCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemInstance[] items = EmptyItems(36);
        items[source] = Item(sourceCount, 7, opaque: [1]);
        items[destination] = Item(destinationCount, 9, 1, opaque: [2]);
        await PublishAsync(fixture, items, ct);
        await OpenMainAsync(fixture, ct);
        BedrockInventorySnapshot before = Read(fixture);

        BedrockItemStackSubmitResult submit = await Submit(fixture, "swap", source, destination, 0, ct);
        (BedrockItemStackRequest request, byte[] payload) = await ReceiveRequestAsync(fixture, ct);
        Assert.Equal(submit.OutboundPayload, payload);
        AssertRequest(request, "swap", source, destination, sourceName, destinationName, 7, 9, 0);
        await RespondAsync(fixture, submit.RequestId, source, destination, sourceName, destinationName, checked((byte)destinationCount), checked((byte)sourceCount), ct);

        BedrockInventorySnapshot after = Read(fixture);
        Assert.Equal(1, after.Items[source].NetworkId);
        Assert.Equal(306, after.Items[destination].NetworkId);
        Assert.Equal(destinationCount, after.Items[source].Count);
        Assert.Equal(sourceCount, after.Items[destination].Count);
        Assert.Equal(new byte[] { 2 }, after.Items[source].ExtraData);
        Assert.Equal(new byte[] { 1 }, after.Items[destination].ExtraData);
        Assert.Equal(410, after.Items[source].StackNetworkId);
        Assert.Equal(420, after.Items[destination].StackNetworkId);
        Assert.NotSame(before.AuthorityReceipts[source], after.AuthorityReceipts[source]);
        AssertResolved(fixture, 1);
    }

    [Theory]
    [InlineData("take", "source-count")]
    [InlineData("place", "source-count")]
    [InlineData("swap", "source-count")]
    [InlineData("take", "destination-count")]
    [InlineData("place", "destination-count")]
    [InlineData("swap", "destination-count")]
    [InlineData("take", "merge-sum")]
    [InlineData("place", "merge-sum")]
    [InlineData("take", "source-id")]
    [InlineData("place", "source-id")]
    [InlineData("swap", "source-id")]
    [InlineData("take", "destination-id")]
    [InlineData("place", "destination-id")]
    [InlineData("swap", "destination-id")]
    [InlineData("take", "metadata")]
    [InlineData("place", "opaque")]
    [InlineData("place", "over-source")]
    [InlineData("take", "zero")]
    [InlineData("place", "malformed-destination")]
    public async Task UnsupportedPreflightDoesNotEmitClaimOrConsumeAllocator(string operation, string refusal)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemInstance[] items = EmptyItems(36);
        items[0] = Item(refusal == "source-count" ? (ushort)256 : (ushort)5, refusal == "source-id" ? 0 : 7);
        items[1] = Item(refusal == "destination-count" ? (ushort)256 : refusal == "merge-sum" ? (ushort)255 : refusal == "malformed-destination" ? (ushort)0 : (ushort)3,
            refusal == "destination-id" ? -9 : 9, metadata: refusal == "metadata" ? 1U : 0U, opaque: refusal == "opaque" ? [1] : []);
        await PublishAsync(fixture, items, ct);
        BedrockInventorySnapshot before = Read(fixture);
        int commits = fixture.Transport.CommittedCount;
        byte count = refusal == "over-source" ? (byte)6 : refusal == "zero" ? (byte)0 : (byte)1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, operation, 0, 1, count, ct));

        Assert.Equal(commits, fixture.Transport.CommittedCount);
        Assert.Equal(0, fixture.Session.WireTranscript.Count(row => row.PacketId == 147));
        AssertResolved(fixture, 0);
        Assert.Same(before.AuthorityReceipts[0], Read(fixture).AuthorityReceipts[0]);
        Assert.Equal(before.Items[1], Read(fixture).Items[1]);
        items[0] = Item(5, 7);
        items[1] = operation == "swap" ? Item(3, 9, 1) : Air();
        await PublishAsync(fixture, items, ct);
        BedrockItemStackSubmitResult next = await Submit(fixture, operation, 0, 1, 1, ct);
        Assert.Equal(-3, next.RequestId);
        Assert.Equal(-3, (await ReceiveRequestAsync(fixture, ct)).Request.RequestId);
        await fixture.RejectAsync(next.RequestId, ct);
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
    }

    [Theory]
    [InlineData("take", 36, 40)]
    [InlineData("place", 9, 9)]
    [InlineData("swap", 35, 35)]
    public async Task PhysicalLimitAndActualCapacityRefuseWithoutIndexing(string operation, byte destination, int capacity)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemInstance[] items = EmptyItems(capacity);
        items[8] = Item(5);
        if (destination < capacity)
            items[destination] = Item(3, 9);
        await PublishAsync(fixture, items, ct);
        int commits = fixture.Transport.CommittedCount;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, operation, 8, destination, 1, ct));

        Assert.Equal(commits, fixture.Transport.CommittedCount);
        AssertResolved(fixture, 0);
        Assert.Equal(capacity, Read(fixture).Items.Length);
    }

    [Fact]
    public async Task MissingResponseRowMakesMainSlotUnavailableForAllPreflights()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemInstance[] items = EmptyItems(36);
        items[8] = Item(5);
        items[9] = Item(3, 9);
        await PublishAsync(fixture, items, ct);
        await OpenMainAsync(fixture, ct);
        BedrockItemStackSubmitResult first = await Submit(fixture, "take", 8, 9, 1, ct);
        _ = await ReceiveRequestAsync(fixture, ct);
        var response = new BedrockItemStackResponsePacket([new(0, first.RequestId, true, [new(new(28, null), [new(8, 8, 4, 410, string.Empty, string.Empty, 0)])])]);
        await fixture.SendAsync(148, BedrockItemStackCodecs.Encode(response), ct);
        await WaitUntilAsync(() => fixture.Session.PendingItemStackReconciliationCount == 0, ct);
        Assert.False(Read(fixture).IsSlotKnown(9));
        int commits = fixture.Transport.CommittedCount;
        foreach (string operation in new[] { "take", "place", "swap" })
        {
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, operation, 8, 9, 1, ct));
            Assert.Contains("authoritative data", failure.Message, StringComparison.Ordinal);
        }
        Assert.Equal(commits, fixture.Transport.CommittedCount);
        AssertResolved(fixture, 1);
    }

    [Theory]
    [InlineData("items")]
    [InlineData("known")]
    [InlineData("corrections")]
    [InlineData("receipts")]
    public async Task MalformedRetainedStateFailsBeforeTransportAndReleasesRawOwner(string malformed)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemInstance[] items = EmptyItems(36);
        items[8] = Item(5);
        await PublishAsync(fixture, items, ct);
        BedrockInventorySnapshot before = Read(fixture);
        BedrockInventorySnapshot invalid = malformed switch
        {
            "items" => before with { Items = default },
            "known" => before with { KnownSlots = [true] },
            "corrections" => before with { ResponseCorrections = [null] },
            _ => before with { AuthorityReceipts = default },
        };
        int generation = fixture.Session.ItemStackTracker.Generation;
        Assert.True(fixture.Session.ActionCoordinator.TryClaim(-1, BedrockActionEmission.Standalone));
        Assert.True(fixture.Session.ItemStackTracker.TryRegister(-1, generation));
        int commits = fixture.Transport.CommittedCount;
        BedrockItemStackRequestPacket request = BedrockItemStackEgress.BuildTake(-1, 8, 9, 1, 7, 0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SendItemStackRequestAsync(request, -1, generation, ct, invalid).AsTask());

        Assert.Equal(commits, fixture.Transport.CommittedCount);
        AssertResolved(fixture, 0);
        Assert.Same(before.AuthorityReceipts[8], Read(fixture).AuthorityReceipts[8]);
    }

    [Theory]
    [InlineData("take")]
    [InlineData("place")]
    [InlineData("swap")]
    public async Task SameValueMainReceiptAtCapacityGrantWinsOverOlderResponse(string operation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemInstance[] items = EmptyItems(36);
        items[9] = Item(5);
        items[35] = operation == "swap" ? Item(3, 9, 1) : Air();
        await PublishAsync(fixture, items, ct);
        await OpenMainAsync(fixture, ct);
        BedrockInventorySnapshot before = Read(fixture);
        SendGate gate = fixture.Transport.HoldNextReservation();
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, operation, 9, 35, 1, ct);
        try
        {
            await WaitForGateAsync(gate, pending, ct);
            await fixture.SendAsync(50, BedrockInventoryCodecs.Encode(new BedrockInventorySlotPacket(0, 9, s_address, null, before.Items[9])), ct);
            await WaitUntilAsync(() => !ReferenceEquals(before.AuthorityReceipts[9], Read(fixture).AuthorityReceipts[9]), ct);
            BedrockInventorySnapshot newer = Read(fixture);
            gate.Allow();
            BedrockItemStackSubmitResult result = await pending.WaitAsync(ct);
            Assert.Equal(result.RequestId, (await ReceiveRequestAsync(fixture, ct)).Request.RequestId);
            await RespondAsync(fixture, result.RequestId, 9, 35, 29, 29, operation == "swap" ? (byte)3 : (byte)4, operation == "swap" ? (byte)5 : (byte)1, ct);
            BedrockInventorySnapshot after = Read(fixture);
            Assert.Equal(before.Items[9], after.Items[9]);
            Assert.True(after.IsSlotKnown(9));
            Assert.Same(newer.AuthorityReceipts[9], after.AuthorityReceipts[9]);
            Assert.False(after.IsSlotKnown(35));
            Assert.Equal(newer.Items[35], after.Items[35]);
            Assert.Same(before.AuthorityReceipts[8], after.AuthorityReceipts[8]);
            AssertResolved(fixture, 1);
        }
        finally
        {
            gate.Allow();
            await ObservePendingAsync(pending, ct);
        }
    }

    [Fact]
    public async Task CallerCancellationBeforeMainReservationDoesNotCommitOrReuseId()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemInstance[] items = EmptyItems(36);
        items[35] = Item(5);
        await PublishAsync(fixture, items, ct);
        await OpenMainAsync(fixture, ct);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SendGate gate = fixture.Transport.HoldNextReservation();
        int commits = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", 35, 8, 1, caller.Token);
        try
        {
            await WaitForGateAsync(gate, pending, ct);
            await caller.CancelAsync();
            OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(caller.Token, failure.CancellationToken);
            Assert.Equal(commits, fixture.Transport.CommittedCount);
            AssertResolved(fixture, 0);
            gate.Allow();
            BedrockItemStackSubmitResult next = await Submit(fixture, "take", 35, 8, 1, ct);
            Assert.Equal(-5, next.RequestId);
            _ = await ReceiveRequestAsync(fixture, ct);
            await fixture.RejectAsync(next.RequestId, ct);
        }
        finally
        {
            gate.Allow();
            await ObservePendingAsync(pending, ct);
        }
    }

    [Fact]
    public async Task CallerCancellationAfterMainTransferKeepsActualResult()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemInstance[] items = EmptyItems(36);
        items[9] = Item(5);
        await PublishAsync(fixture, items, ct);
        await OpenMainAsync(fixture, ct);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SendGate gate = fixture.Transport.HoldNextTransferResult();
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "place", 9, 35, 1, caller.Token);
        try
        {
            await WaitForGateAsync(gate, pending, ct);
            (BedrockItemStackRequest request, _) = await ReceiveRequestAsync(fixture, ct);
            await caller.CancelAsync();
            await RespondAsync(fixture, request.RequestId, 9, 35, 29, 29, 4, 1, ct);
            Assert.False(pending.IsCompleted);
            gate.Allow();
            Assert.Equal(request.RequestId, (await pending.WaitAsync(ct)).RequestId);
            AssertResolved(fixture, 1);
            Assert.False(fixture.Session.Completion.IsCompleted);
        }
        finally
        {
            gate.Allow();
            await ObservePendingAsync(pending, ct);
        }
    }

    [Fact]
    public async Task DisposeJoinsMainGrantedCommitBeforeOwnedTransportTeardown()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using OwnershipSession fixture = await OwnershipSession.CreateAsync(ct);
        BedrockItemInstance[] items = EmptyItems(36);
        items[9] = Item(5);
        await PublishAsync(fixture, items, ct);
        await OpenMainAsync(fixture, ct);
        SendGate gate = fixture.Transport.HoldNextGrantedCommit();
        int commits = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "place", 9, 35, 1, ct);
        Task? dispose = null;
        try
        {
            await WaitForGateAsync(gate, pending, ct);
            Assert.Equal(commits, fixture.Transport.CommittedCount);
            dispose = fixture.Session.DisposeAsync().AsTask();
            await Task.WhenAny(fixture.Transport.DisposeEntered.Task, Task.Delay(250, ct));
            Assert.False(dispose.IsCompleted);
            Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
            Assert.False(fixture.Transport.DisposeEntered.Task.IsCompleted);
            gate.Allow();
            Assert.Equal(-3, (await pending.WaitAsync(TimeSpan.FromSeconds(5), ct)).RequestId);
            await dispose.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(commits + 1, fixture.Transport.CommittedCount);
            Assert.Single(fixture.Session.WireTranscript, row => row.PacketId == 147 && row.Direction == BedrockWireDirection.Outbound);
            AssertResolved(fixture, 0);
        }
        finally
        {
            gate.Allow();
            await ObservePendingAsync(pending, ct);
            if (dispose is not null)
                await dispose.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    private static Task<BedrockItemStackSubmitResult> Submit(OwnershipSession fixture, string operation, byte source, byte destination, byte count, CancellationToken ct) => operation switch
    {
        "take" => fixture.Session.SubmitInventoryTakeAsync(s_address, source, destination, count, ct).AsTask(),
        "swap" => fixture.Session.SubmitInventorySwapAsync(s_address, source, destination, ct).AsTask(),
        _ => fixture.Session.SubmitInventoryPlaceAsync(s_address, source, destination, count, ct).AsTask(),
    };

    private static void AssertRequest(BedrockItemStackRequest request, string operation, byte source, byte destination, byte sourceName, byte destinationName, int sourceId, int destinationId, byte count)
    {
        Assert.Equal(-3, request.RequestId);
        Assert.Equal(0, request.Cause);
        BedrockItemStackAction action = Assert.Single(request.Actions);
        (BedrockStackRequestSlot from, BedrockStackRequestSlot to) = action.Payload switch
        {
            BedrockTakePayload take => (take.Source, take.Destination),
            BedrockPlacePayload place => (place.Source, place.Destination),
            BedrockSwapPayload swap => (swap.Source, swap.Destination),
            _ => throw new InvalidOperationException("Unexpected inventory action."),
        };
        Assert.Equal(operation == "take" ? BedrockItemStackActionType.Take : operation == "swap" ? BedrockItemStackActionType.Swap : BedrockItemStackActionType.Place, action.Type);
        Assert.Equal(operation == "take" ? 0 : operation == "swap" ? 2 : 1, action.LegacyTypeId);
        if (action.Payload is BedrockTakePayload takePayload)
            Assert.Equal(count, takePayload.Count);
        if (action.Payload is BedrockPlacePayload placePayload)
            Assert.Equal(count, placePayload.Count);
        Assert.Equal(new BedrockFullContainerName(sourceName, null), from.Container);
        Assert.Equal(new BedrockFullContainerName(destinationName, null), to.Container);
        Assert.Equal(source, from.Slot);
        Assert.Equal(destination, to.Slot);
        Assert.Equal(sourceId, from.StackId);
        Assert.Equal(destinationId, to.StackId);
    }

    private static BedrockItemInstance[] EmptyItems(int capacity) => Enumerable.Repeat(Air(), capacity).ToArray();
    private static BedrockInventorySnapshot Read(OwnershipSession fixture) => fixture.Session.InventoryStore.GetSnapshot(s_address)!.Value;

    private static async Task OpenMainAsync(OwnershipSession fixture, CancellationToken ct)
    {
        await fixture.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(3, -1, 0, 0, 0, -1)), ct);
        await WaitUntilAsync(() => fixture.Session.ContainerWindows.GetWindow(3) is { WindowType: -1, EntityUniqueId: -1 }, ct);
    }

    private static async Task PublishAsync(OwnershipSession fixture, BedrockItemInstance[] items, CancellationToken ct)
    {
        object receipt = Read(fixture).AuthorityReceipts[0];
        await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, items, s_address, Air())), ct);
        await WaitUntilAsync(() => !ReferenceEquals(receipt, Read(fixture).AuthorityReceipts[0]), ct);
        Assert.Equal(items.Length, Read(fixture).Items.Length);
    }

    private static async Task<(BedrockItemStackRequest Request, byte[] Payload)> ReceiveRequestAsync(OwnershipSession fixture, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            (int id, byte[] payload) = await fixture.ReceiveAsync(ct);
            if (id != 147)
                continue;
            BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(payload);
            Assert.Equal(payload, BedrockItemStackCodecs.Encode(decoded));
            return (Assert.Single(decoded.Requests), payload);
        }
        throw new InvalidOperationException("The fake peer did not receive a correlated inventory request.");
    }

    private static async Task RespondAsync(OwnershipSession fixture, int requestId, byte source, byte destination, byte sourceName, byte destinationName, byte sourceCount, byte destinationCount, CancellationToken ct)
    {
        var response = new BedrockItemStackResponsePacket([new(0, requestId, true,
        [
            new(new(sourceName, null), [new(source, source, sourceCount, 410, "accepted", "filtered", 17)]),
            new(new(destinationName, null), [new(destination, destination, destinationCount, 420, string.Empty, string.Empty, 0)]),
        ])]);
        byte[] payload = BedrockItemStackCodecs.Encode(response);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(BedrockItemStackCodecs.DecodeResponse(payload)));
        await fixture.SendAsync(148, payload, ct);
        await WaitUntilAsync(() => fixture.Session.PendingItemStackReconciliationCount == 0, ct);
    }

    private static void AssertResolved(OwnershipSession fixture, int accepted)
    {
        Assert.Equal(accepted, fixture.Session.ItemStackTracker.AcceptedCount);
        Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(0, fixture.Session.PendingItemStackReconciliationCount);
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
    }

    private static async Task WaitForGateAsync(SendGate gate, Task<BedrockItemStackSubmitResult> pending, CancellationToken ct)
    {
        await Task.WhenAny(gate.Entered, pending).WaitAsync(ct);
        if (pending.IsCompleted)
            await pending.WaitAsync(ct);
        await gate.Entered.WaitAsync(ct);
    }

    private static async Task ObservePendingAsync(Task<BedrockItemStackSubmitResult> pending, CancellationToken ct)
    {
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken ct)
    {
        while (!predicate())
            await Task.Delay(10, ct);
    }
}
