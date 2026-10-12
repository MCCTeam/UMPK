using System.Net;
using Umpk.Client.Bedrock.Actions;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockItemStackEgressTests
{
    private static BedrockItemInstance Diamond(byte count, int stackId) =>
        new(306, count, 0, stackId, 0, []);

    private static BedrockItemInstance Stone(byte count, int stackId) =>
        new(1, count, 0, stackId, 0, []);

    private static BedrockItemInstance Air() =>
        new(0, 0, 0, null, 0, []);

    private static BedrockContainerAddress PlayerContentAddress() =>
        new(0, 0, null);

    private static BedrockInventoryContentPacket PlayerContent(params BedrockItemInstance[] items) =>
        new(0, items, PlayerContentAddress(), Air());

    [Fact]
    public void TrackerCancelRemovesPendingWithoutCounts()
    {
        var tracker = new BedrockItemStackRequestTracker();
        Assert.True(tracker.TryRegister(61, tracker.Generation));
        Assert.True(tracker.TryCancel(61, tracker.Generation));
        Assert.Equal(0, tracker.PendingCount);
        Assert.Equal(0, tracker.AcceptedCount);
        Assert.Equal(0, tracker.RejectedCount);
        Assert.False(tracker.TryCancel(61, tracker.Generation));
    }

    [Fact]
    public void TrackerCancelRejectsStaleGeneration()
    {
        var tracker = new BedrockItemStackRequestTracker();
        int generation = tracker.Generation;
        Assert.True(tracker.TryRegister(62, generation));
        tracker.Invalidate();
        Assert.False(tracker.TryCancel(62, generation));
    }

    [Fact]
    public async Task SuccessfulAdmissionAndAcceptedResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        Assert.Equal(-3, submit.RequestId);
        Assert.True(session.ActionCoordinator.IsClaimed(submit.RequestId));
        Assert.Equal(1, session.ItemStackTracker.PendingCount);

        (int packetId, byte[] payload) = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(147, packetId);
        Assert.Equal(payload, submit.OutboundPayload);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(payload);
        Assert.Equal(submit.RequestId, decoded.Requests[0].RequestId);
        Assert.Single(decoded.Requests[0].Actions);
        Assert.Equal(BedrockItemStackActionType.Place, decoded.Requests[0].Actions[0].Type);
        var place = (BedrockPlacePayload)decoded.Requests[0].Actions[0].Payload;
        Assert.Equal(1, place.Count);
        Assert.Equal(0, place.Source.Slot);
        Assert.Equal(1, place.Destination.Slot);
        Assert.Equal(7, place.Source.StackId);
        Assert.Equal(0, place.Destination.StackId);
        Assert.Equal(28, place.Source.Container.ContainerId);
        Assert.Equal(28, place.Destination.Container.ContainerId);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(decoded));

        var responseSlot = new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0);
        var responseContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [responseSlot]);
        var response = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, submit.RequestId, true, [responseContainer])]);
        byte[] responsePayload = BedrockItemStackCodecs.Encode(response);
        await peer.SendAsync(148, responsePayload, ct);

        await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(submit.RequestId), ct);

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(1, session.ItemStackTracker.GetBaseline(28, null, 1)!.Value.Count);
        BedrockInventorySnapshot snapshot = session.InventoryStore.GetSnapshot(PlayerContentAddress())!.Value;
        Assert.False(snapshot.IsSlotKnown(0));
        Assert.True(snapshot.IsSlotKnown(1));
        Assert.Equal(306, snapshot.Items[1].NetworkId);
        Assert.Equal(1, snapshot.Items[1].Count);
        Assert.Equal(9, snapshot.Items[1].StackNetworkId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 2, 1, ct).AsTask());
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task AcceptedPlaceReconcilesFullInventoryWithoutContentRefresh()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        BedrockItemInstance[] items = Enumerable.Repeat(Air(), 36).ToArray();
        items[0] = Diamond(5, 2);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult first = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        Assert.Equal(-3, first.RequestId);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);
        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null),
        [
            new BedrockItemStackResponseSlot(0, 0, 4, 2, string.Empty, string.Empty, 0),
            new BedrockItemStackResponseSlot(1, 1, 1, 3, string.Empty, string.Empty, 0),
        ]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket(
            [new BedrockItemStackResponse(0, first.RequestId, true, [container])])), ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(first.RequestId), ct);

        BedrockInventorySnapshot snapshot = session.InventoryStore.GetSnapshot(PlayerContentAddress())!.Value;
        Assert.Equal(4, snapshot.Items[0].Count);
        Assert.Equal(306, snapshot.Items[1].NetworkId);
        Assert.Equal(1, snapshot.Items[1].Count);
        Assert.Equal(3, snapshot.Items[1].StackNetworkId);

        BedrockItemStackSubmitResult second = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 1, 2, 1, ct);
        Assert.Equal(-5, second.RequestId);
        (int packetId, byte[] payload) = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(147, packetId);
        Assert.Equal(payload, second.OutboundPayload);
        BedrockPlacePayload place = Assert.IsType<BedrockPlacePayload>(BedrockItemStackCodecs.DecodeRequest(payload).Requests[0].Actions[0].Payload);
        Assert.Equal(3, place.Source.StackId);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task DelayedFirstResponseCannotCompleteOrMutateSecondRequest()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        await using BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        BedrockItemInstance[] items = Enumerable.Repeat(Air(), 36).ToArray();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult first = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        Assert.Equal(-3, first.RequestId);
        (_, byte[] firstPayload) = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(first.RequestId, BedrockItemStackCodecs.DecodeRequest(firstPayload).Requests[0].RequestId);
        var firstContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null),
        [
            new BedrockItemStackResponseSlot(0, 0, 4, 7, string.Empty, string.Empty, 0),
            new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0),
        ]);
        byte[] firstResponse = BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, first.RequestId, true, [firstContainer])]));
        await peer.SendAsync(148, firstResponse, ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(first.RequestId), ct);

        BedrockItemStackSubmitResult second = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 1, 2, 1, ct);
        Assert.Equal(-5, second.RequestId);
        (_, byte[] secondPayload) = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(second.RequestId, BedrockItemStackCodecs.DecodeRequest(secondPayload).Requests[0].RequestId);

        await peer.SendAsync(148, firstResponse, ct);
        await WaitUntilAsync(() => session.ItemStackTracker.DuplicateCount == 1, ct);

        Assert.Equal(1, session.ItemStackTracker.AcceptedCount);
        Assert.Equal(1, session.ItemStackTracker.PendingCount);
        Assert.Equal(1, session.ActionCoordinator.ClaimedCount);
        Assert.True(session.ActionCoordinator.IsClaimed(second.RequestId));
        BedrockInventorySnapshot afterDuplicate = session.InventoryStore.GetSnapshot(PlayerContentAddress())!.Value;
        Assert.True(afterDuplicate.IsSlotKnown(0));
        Assert.True(afterDuplicate.IsSlotKnown(1));
        Assert.True(afterDuplicate.IsSlotKnown(2));
        Assert.Equal(4, afterDuplicate.Items[0].Count);
        Assert.Equal(7, afterDuplicate.Items[0].StackNetworkId);
        Assert.Equal(1, afterDuplicate.Items[1].Count);
        Assert.Equal(9, afterDuplicate.Items[1].StackNetworkId);
        Assert.Equal(0, afterDuplicate.Items[2].NetworkId);

        var secondContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null),
        [
            new BedrockItemStackResponseSlot(1, 1, 0, 0, string.Empty, string.Empty, 0),
            new BedrockItemStackResponseSlot(2, 2, 1, 10, string.Empty, string.Empty, 0),
        ]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, second.RequestId, true, [secondContainer])])), ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(second.RequestId), ct);

        Assert.Equal(2, session.ItemStackTracker.AcceptedCount);
        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        BedrockInventorySnapshot afterSecond = session.InventoryStore.GetSnapshot(PlayerContentAddress())!.Value;
        Assert.True(afterSecond.IsSlotKnown(1));
        Assert.Equal(Air(), afterSecond.Items[1]);
        Assert.True(afterSecond.IsSlotKnown(2));
        Assert.Equal(306, afterSecond.Items[2].NetworkId);
        Assert.Equal(1, afterSecond.Items[2].Count);
        Assert.Equal(10, afterSecond.Items[2].StackNetworkId);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task ExplicitErrorResponseReleasesWithoutBaseline()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(1, submit.RequestId, false, [])])), ct);

        await WaitUntilAsync(() => session.ItemStackTracker.RejectedCount == 1, ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(submit.RequestId), ct);

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Null(session.ItemStackTracker.GetBaseline(28, null, 1));
    }

    [Fact]
    public async Task CancelledSubmitLeavesNoClaimOrPending()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, cancelled.Token).AsTask());

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
    }

    [Fact]
    public async Task PreTransferCancellationReleasesClaimAndPending()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        int generation = session.ItemStackTracker.Generation;
        Assert.True(session.ActionCoordinator.TryClaim(-1, BedrockActionEmission.Standalone));
        Assert.True(session.ItemStackTracker.TryRegister(-1, generation));
        BedrockItemStackRequestPacket packet = BedrockItemStackEgress.BuildTake(-1, 0, 1, 1, 7, 0);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        ValueTask<BedrockItemStackSubmitResult> pending = session.SendItemStackRequestAsync(packet, -1, generation, cancelled.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.AsTask());

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.False(session.ActionCoordinator.IsClaimed(-1));
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);

        BedrockItemStackSubmitResult retry = await session.SubmitInventoryTakeAsync(PlayerContentAddress(), 0, 1, 1, ct);
        Assert.Equal(-3, retry.RequestId);
        Assert.Equal(1, session.ItemStackTracker.PendingCount);
        Assert.True(session.ActionCoordinator.IsClaimed(retry.RequestId));
    }

    [Fact]
    public async Task AuthoritativeMismatchBeforeSendLeavesNoClaim()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct).AsTask());

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
    }

    [Fact]
    public async Task ConflictingDestinationIsRejectedBeforeSend()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        items[1] = Stone(3, 11);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct).AsTask());

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
    }

    [Fact]
    public async Task SuccessfulPlaceMergeAdmissionAndAcceptedResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        items[1] = Diamond(2, 11);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 2, ct);
        Assert.True(session.ActionCoordinator.IsClaimed(submit.RequestId));
        Assert.Equal(1, session.ItemStackTracker.PendingCount);

        (int packetId, byte[] payload) = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(147, packetId);
        Assert.Equal(payload, submit.OutboundPayload);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(payload);
        Assert.Equal(submit.RequestId, decoded.Requests[0].RequestId);
        Assert.Single(decoded.Requests[0].Actions);
        Assert.Equal(BedrockItemStackActionType.Place, decoded.Requests[0].Actions[0].Type);
        Assert.Equal(1, decoded.Requests[0].Actions[0].LegacyTypeId);
        var place = (BedrockPlacePayload)decoded.Requests[0].Actions[0].Payload;
        Assert.Equal(2, place.Count);
        Assert.Equal(0, place.Source.Slot);
        Assert.Equal(1, place.Destination.Slot);
        Assert.Equal(7, place.Source.StackId);
        Assert.Equal(11, place.Destination.StackId);
        Assert.Equal(28, place.Source.Container.ContainerId);
        Assert.Equal(28, place.Destination.Container.ContainerId);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(decoded));

        var sourceContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(0, 0, 3, 7, string.Empty, string.Empty, 0)]);
        var destinationContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 4, 11, string.Empty, string.Empty, 0)]);
        var response = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, submit.RequestId, true, [sourceContainer, destinationContainer])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(response), ct);

        await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(submit.RequestId), ct);

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(3, session.ItemStackTracker.GetBaseline(28, null, 0)!.Value.Count);
        Assert.Equal(4, session.ItemStackTracker.GetBaseline(28, null, 1)!.Value.Count);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task PlaceMergeRejectsMissingDestinationStackIdentity()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        items[1] = new BedrockItemInstance(306, 2, 0, null, 0, []);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct).AsTask());

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
    }

    [Fact]
    public async Task UnknownResponseKeepsPendingClaim()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, submit.RequestId + 999, false, [])])), ct);

        await WaitUntilAsync(() => session.ItemStackTracker.UnknownCount == 1, ct);

        Assert.True(session.ActionCoordinator.IsClaimed(submit.RequestId));
        Assert.Equal(1, session.ItemStackTracker.PendingCount);
    }

    [Fact]
    public async Task DuplicateResponseDoesNotMutateBaseline()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);
        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0)]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, submit.RequestId, true, [container])])), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, ct);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, submit.RequestId, true, [container])])), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.DuplicateCount == 1, ct);

        Assert.False(session.ActionCoordinator.IsClaimed(submit.RequestId));
        Assert.Equal(1, session.ItemStackTracker.GetBaseline(28, null, 1)!.Value.Count);
    }

    [Fact]
    public async Task ContainerCloseInvalidatesPendingWork()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(1, session.ItemStackTracker.PendingCount);

        await peer.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(0, 0, true)), ct);
        await WaitUntilAsync(() => session.PendingItemStackReconciliationCount == 0 && session.ItemStackTracker.PendingCount == 0 && session.ActionCoordinator.ClaimedCount == 0, ct);

        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.True(session.ItemStackTracker.Generation != 1 || session.ItemStackTracker.PendingCount == 0);
        Assert.False(session.Completion.IsCompleted);
        Assert.True(submit.RequestId < 0);
    }

    [Fact]
    public void FirstSubmissionUsesNegativeClientOwnedId()
    {
        var packet = BedrockItemStackEgress.BuildPlace(-1, 0, 1, 1, 7, 0);

        byte[] encoded = BedrockItemStackCodecs.Encode(packet);

        Assert.Equal(-1, BedrockItemStackCodecs.DecodeRequest(encoded).Requests[0].RequestId);
        Assert.Equal(0x01, encoded[1]);
    }

    [Fact]
    public void EgressRejectsNonNegativeRequestId()
    {
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildPlace(0, 0, 1, 1, 7, 0));
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildPlace(1, 0, 1, 1, 7, 0));
    }

    [Fact]
    public void PlaceBuildCarriesDestinationStackIdForMerge()
    {
        var packet = BedrockItemStackEgress.BuildPlace(-1, 0, 1, 2, 7, 11);

        byte[] encoded = BedrockItemStackCodecs.Encode(packet);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(encoded);

        Assert.Equal(-1, decoded.Requests[0].RequestId);
        Assert.Single(decoded.Requests[0].Actions);
        Assert.Equal(BedrockItemStackActionType.Place, decoded.Requests[0].Actions[0].Type);
        Assert.Equal(1, decoded.Requests[0].Actions[0].LegacyTypeId);
        var place = (BedrockPlacePayload)decoded.Requests[0].Actions[0].Payload;
        Assert.Equal(2, place.Count);
        Assert.Equal(0, place.Source.Slot);
        Assert.Equal(1, place.Destination.Slot);
        Assert.Equal(7, place.Source.StackId);
        Assert.Equal(11, place.Destination.StackId);
        Assert.Equal(encoded, BedrockItemStackCodecs.Encode(decoded));
    }

    [Fact]
    public void SwapBuildUsesExactWireForm()
    {
        var packet = BedrockItemStackEgress.BuildSwap(-1, 0, 1, 7, 9);

        byte[] encoded = BedrockItemStackCodecs.Encode(packet);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(encoded);

        Assert.Equal(-1, decoded.Requests[0].RequestId);
        Assert.Single(decoded.Requests[0].Actions);
        Assert.Equal(BedrockItemStackActionType.Swap, decoded.Requests[0].Actions[0].Type);
        Assert.Equal(2, decoded.Requests[0].Actions[0].LegacyTypeId);
        var swap = (BedrockSwapPayload)decoded.Requests[0].Actions[0].Payload;
        Assert.Equal(0, swap.Source.Slot);
        Assert.Equal(1, swap.Destination.Slot);
        Assert.Equal(7, swap.Source.StackId);
        Assert.Equal(9, swap.Destination.StackId);
        Assert.Equal(28, swap.Source.Container.ContainerId);
        Assert.Equal(28, swap.Destination.Container.ContainerId);
        Assert.Equal(encoded, BedrockItemStackCodecs.Encode(decoded));
    }

    [Fact]
    public void SwapBuildRejectsNonNegativeRequestIdAndSameSlot()
    {
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildSwap(0, 0, 1, 7, 9));
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildSwap(-1, 0, 0, 7, 9));
    }

    [Fact]
    public void TakeBuildUsesExactWireForm()
    {
        var packet = BedrockItemStackEgress.BuildTake(-1, 0, 1, 2, 7, 0);

        byte[] encoded = BedrockItemStackCodecs.Encode(packet);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(encoded);

        Assert.Equal(-1, decoded.Requests[0].RequestId);
        Assert.Single(decoded.Requests[0].Actions);
        Assert.Equal(BedrockItemStackActionType.Take, decoded.Requests[0].Actions[0].Type);
        Assert.Equal(0, decoded.Requests[0].Actions[0].LegacyTypeId);
        var take = (BedrockTakePayload)decoded.Requests[0].Actions[0].Payload;
        Assert.Equal(2, take.Count);
        Assert.Equal(0, take.Source.Slot);
        Assert.Equal(1, take.Destination.Slot);
        Assert.Equal(7, take.Source.StackId);
        Assert.Equal(0, take.Destination.StackId);
        Assert.Equal(28, take.Source.Container.ContainerId);
        Assert.Equal(28, take.Destination.Container.ContainerId);
        Assert.Equal(encoded, BedrockItemStackCodecs.Encode(decoded));
    }

    [Fact]
    public void TakeBuildRejectsNonNegativeRequestIdSameSlotAndZeroCount()
    {
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildTake(0, 0, 1, 1, 7, 0));
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildTake(-1, 0, 0, 1, 7, 0));
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildTake(-1, 0, 1, 0, 7, 0));
        Assert.Throws<InvalidOperationException>(() => BedrockItemStackEgress.BuildTake(-1, 0, 9, 1, 7, 0));
    }

    [Fact]
    public async Task SuccessfulTakeAdmissionAndAcceptedResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryTakeAsync(PlayerContentAddress(), 0, 1, 2, ct);
        Assert.True(session.ActionCoordinator.IsClaimed(submit.RequestId));
        Assert.Equal(1, session.ItemStackTracker.PendingCount);

        (int packetId, byte[] payload) = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(147, packetId);
        Assert.Equal(payload, submit.OutboundPayload);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(payload);
        Assert.Equal(submit.RequestId, decoded.Requests[0].RequestId);
        Assert.Single(decoded.Requests[0].Actions);
        Assert.Equal(BedrockItemStackActionType.Take, decoded.Requests[0].Actions[0].Type);
        Assert.Equal(0, decoded.Requests[0].Actions[0].LegacyTypeId);
        var take = (BedrockTakePayload)decoded.Requests[0].Actions[0].Payload;
        Assert.Equal(2, take.Count);
        Assert.Equal(0, take.Source.Slot);
        Assert.Equal(1, take.Destination.Slot);
        Assert.Equal(7, take.Source.StackId);
        Assert.Equal(0, take.Destination.StackId);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(decoded));

        var sourceContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(0, 0, 3, 7, string.Empty, string.Empty, 0)]);
        var destinationContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 2, 9, string.Empty, string.Empty, 0)]);
        var response = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, submit.RequestId, true, [sourceContainer, destinationContainer])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(response), ct);

        await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(submit.RequestId), ct);

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(3, session.ItemStackTracker.GetBaseline(28, null, 0)!.Value.Count);
        Assert.Equal(2, session.ItemStackTracker.GetBaseline(28, null, 1)!.Value.Count);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task TakeSubmitRejectsOccupiedDestinationOverCountAndMissingIdentity()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        items[1] = Stone(2, 11);
        items[2] = new BedrockItemInstance(306, 2, 0, null, 0, []);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryTakeAsync(PlayerContentAddress(), 0, 1, 1, ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryTakeAsync(PlayerContentAddress(), 0, 3, 6, ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryTakeAsync(PlayerContentAddress(), 2, 3, 1, ct).AsTask());

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
    }

    [Fact]
    public async Task SuccessfulTakeMergeAdmissionAndAcceptedResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        items[1] = Diamond(2, 11);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventoryTakeAsync(PlayerContentAddress(), 0, 1, 2, ct);
        Assert.True(session.ActionCoordinator.IsClaimed(submit.RequestId));
        Assert.Equal(1, session.ItemStackTracker.PendingCount);

        (int packetId, byte[] payload) = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(147, packetId);
        Assert.Equal(payload, submit.OutboundPayload);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(payload);
        Assert.Equal(submit.RequestId, decoded.Requests[0].RequestId);
        Assert.Single(decoded.Requests[0].Actions);
        Assert.Equal(BedrockItemStackActionType.Take, decoded.Requests[0].Actions[0].Type);
        Assert.Equal(0, decoded.Requests[0].Actions[0].LegacyTypeId);
        var take = (BedrockTakePayload)decoded.Requests[0].Actions[0].Payload;
        Assert.Equal(2, take.Count);
        Assert.Equal(0, take.Source.Slot);
        Assert.Equal(1, take.Destination.Slot);
        Assert.Equal(7, take.Source.StackId);
        Assert.Equal(11, take.Destination.StackId);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(decoded));

        var sourceContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(0, 0, 3, 7, string.Empty, string.Empty, 0)]);
        var destinationContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 4, 11, string.Empty, string.Empty, 0)]);
        var response = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, submit.RequestId, true, [sourceContainer, destinationContainer])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(response), ct);

        await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(submit.RequestId), ct);

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(3, session.ItemStackTracker.GetBaseline(28, null, 0)!.Value.Count);
        Assert.Equal(4, session.ItemStackTracker.GetBaseline(28, null, 1)!.Value.Count);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task SuccessfulSwapAdmissionAndAcceptedResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        items[1] = Stone(3, 11);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult submit = await session.SubmitInventorySwapAsync(PlayerContentAddress(), 0, 1, ct);
        Assert.True(session.ActionCoordinator.IsClaimed(submit.RequestId));
        Assert.Equal(1, session.ItemStackTracker.PendingCount);

        (int packetId, byte[] payload) = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(147, packetId);
        Assert.Equal(payload, submit.OutboundPayload);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(payload);
        Assert.Equal(submit.RequestId, decoded.Requests[0].RequestId);
        Assert.Single(decoded.Requests[0].Actions);
        Assert.Equal(BedrockItemStackActionType.Swap, decoded.Requests[0].Actions[0].Type);
        var swap = (BedrockSwapPayload)decoded.Requests[0].Actions[0].Payload;
        Assert.Equal(0, swap.Source.Slot);
        Assert.Equal(1, swap.Destination.Slot);
        Assert.Equal(7, swap.Source.StackId);
        Assert.Equal(11, swap.Destination.StackId);
        Assert.Equal(28, swap.Source.Container.ContainerId);
        Assert.Equal(28, swap.Destination.Container.ContainerId);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(decoded));

        var sourceContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(0, 0, 3, 11, string.Empty, string.Empty, 0)]);
        var destinationContainer = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 5, 7, string.Empty, string.Empty, 0)]);
        var response = new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, submit.RequestId, true, [sourceContainer, destinationContainer])]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(response), ct);

        await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(submit.RequestId), ct);

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.NotNull(session.ItemStackTracker.GetBaseline(28, null, 0));
        Assert.NotNull(session.ItemStackTracker.GetBaseline(28, null, 1));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task SwapCloseWithPendingRequestRetiresEgressUntilReconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        items[1] = Stone(3, 11);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult first = await session.SubmitInventorySwapAsync(PlayerContentAddress(), 0, 1, ct);
        Assert.Equal(-3, first.RequestId);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);

        await peer.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(0, 0, true)), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.PendingCount == 0, ct);

        Assert.Equal(0, session.PendingItemStackReconciliationCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.Null(session.InventoryStore.GetSnapshot(PlayerContentAddress()));

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventorySwapAsync(PlayerContentAddress(), 0, 1, ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 2, 1, ct).AsTask());

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task SwapSubmitRejectsEmptySlot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventorySwapAsync(PlayerContentAddress(), 0, 1, ct).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventorySwapAsync(PlayerContentAddress(), 1, 0, ct).AsTask());

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
    }

    [Fact]
    public async Task SubmitRejectsNonHotbarSlots()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 9, 1, ct).AsTask());

        Assert.Equal(0, session.ItemStackTracker.PendingCount);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
    }

    [Fact]
    public async Task CloseWithPendingRequestRetiresEgressUntilReconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult first = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        Assert.Equal(-3, first.RequestId);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);

        await peer.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(0, 0, true)), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.PendingCount == 0, ct);

        Assert.Equal(session.InventoryStore.Generation, session.ItemStackTracker.Generation);
        Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
        Assert.Null(session.InventoryStore.GetSnapshot(PlayerContentAddress()));

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 2, 1, ct).AsTask());

        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0)]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, first.RequestId, true, [container])])), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.UnknownCount == 1, ct);

        Assert.Equal(0, session.ItemStackTracker.AcceptedCount);
        Assert.Null(session.ItemStackTracker.GetBaseline(28, null, 1));
        Assert.False(session.Completion.IsCompleted);
    }

    [Fact]
    public async Task CloseWithoutPendingRequestKeepsEgressUsable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 7);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult first = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        Assert.Equal(-3, first.RequestId);
        _ = await ReceiveUntilPacketAsync(peer, 147, ct);

        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0)]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, first.RequestId, true, [container])])), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, ct);

        await peer.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(0, 0, true)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is null, ct);

        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult second = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 2, 1, ct);
        Assert.Equal(-5, second.RequestId);
        (int packetId, byte[] payload) = await ReceiveUntilPacketAsync(peer, 147, ct);
        Assert.Equal(147, packetId);
        Assert.Equal(payload, second.OutboundPayload);
        Assert.NotEqual(first.OutboundPayload, second.OutboundPayload);
    }

    [Fact]
    public async Task SecondSubmitWhileOneOutstandingIsRejected()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using TestMessageConnectionPair pair = BedrockFakePeer.CreatePair();
        await using BedrockFakePeer peer = BedrockFakePeer.Create(pair.Second);
        BedrockClient client = CreateClient(pair);
        await using BedrockClientSession session = await client.ConnectAsync(ct);
        await CompleteJoinAsync(peer, session, ct);

        var items = new BedrockItemInstance[36];
        for (int index = 0; index < items.Length; index++)
            items[index] = Air();
        items[0] = Diamond(5, 11);
        items[2] = Diamond(5, 12);
        await peer.SendAsync(49, BedrockInventoryCodecs.Encode(PlayerContent(items)), ct);
        await WaitUntilAsync(() => session.InventoryStore.GetSnapshot(PlayerContentAddress()) is not null, ct);

        BedrockItemStackSubmitResult first = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 0, 1, 1, ct);
        Assert.Equal(-3, first.RequestId);
        Assert.Equal(1, session.ItemStackTracker.PendingCount);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 2, 3, 1, ct).AsTask());

        Assert.Equal(1, session.ItemStackTracker.PendingCount);
        Assert.Equal(1, session.ActionCoordinator.ClaimedCount);
        Assert.True(session.ActionCoordinator.IsClaimed(first.RequestId));

        var container = new BedrockItemStackResponseContainer(new BedrockFullContainerName(28, null), [new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0)]);
        await peer.SendAsync(148, BedrockItemStackCodecs.Encode(new BedrockItemStackResponsePacket([new BedrockItemStackResponse(0, first.RequestId, true, [container])])), ct);
        await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, ct);
        await WaitUntilAsync(() => !session.ActionCoordinator.IsClaimed(first.RequestId), ct);

        BedrockItemStackSubmitResult second = await session.SubmitInventoryPlaceAsync(PlayerContentAddress(), 2, 3, 1, ct);
        Assert.Equal(-5, second.RequestId);
        Assert.NotEqual(first.OutboundPayload, second.OutboundPayload);
        Assert.Equal(1, session.ItemStackTracker.PendingCount);
    }

    private static async ValueTask WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }

    private static async ValueTask<(int PacketId, byte[] Payload)> ReceiveUntilPacketAsync(BedrockFakePeer peer, int expectedId, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
            if (packetId == expectedId)
                return (packetId, payload);
        }

        throw new InvalidOperationException($"The fake peer did not emit packet {expectedId}.");
    }

    private static BedrockClient CreateClient(TestMessageConnectionPair pair)
    {
        return new BedrockClient(
            new BedrockClientSettings
            {
                Host = "127.0.0.1",
                Port = 19132,
                CredentialProvider = new OfflineBedrockCredentialProvider("TestPlayer", Guid.NewGuid()),
                ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
            },
            (endpoint, _) =>
            {
                Assert.Equal(IPAddress.Loopback, endpoint.Address);
                return ValueTask.FromResult<IMessageConnection>(pair.First);
            });
    }

    private static async ValueTask CompleteJoinAsync(BedrockFakePeer peer, BedrockClientSession session, CancellationToken ct)
    {
        await ExpectPacketAsync(peer, 193, ct);
        await peer.SendAsync(143, Bootstrap(helpers => helpers.NetworkSettings()), ct);
        await peer.EnableCompressionAsync(new BedrockCompressionSettings(1, BedrockCompressionAlgorithm.Deflate), ct);
        await peer.ReceiveAsync(ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.LoginSuccess)), ct);
        await peer.SendAsync(6, Join(helpers => helpers.EmptyResourceInfo()), ct);
        await ExpectResourceResponseAsync(peer, ct);
        await peer.ReceiveAsync(ct);
        await peer.ReceiveAsync(ct);
        await peer.SendAsync(7, Join(helpers => helpers.EmptyResourceStack()), ct);
        await ExpectResourceResponseAsync(peer, ct);
        await peer.SendBatchAsync(
        [
            (162, ItemRegistry()),
            (119, EntityIdentifiers()),
            (122, Join(helpers => helpers.BiomeList())),
            (11, Join(helpers => helpers.StartGame())),
        ], ct);
        await peer.SendAsync(2, Bootstrap(helpers => helpers.PlayStatus(PlayStatusCode.PlayerSpawn)), ct);
        await peer.ReceiveAsync(ct);
        await peer.SendAsync(58, LevelChunk(), ct);
        await session.WaitUntilReadyAsync(ct);
    }

    private static async ValueTask<(int PacketId, byte[] Payload)> ExpectPacketAsync(BedrockFakePeer peer, int expectedId, CancellationToken ct)
    {
        (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
        Assert.Equal(expectedId, packetId);
        return (packetId, payload);
    }

    private static async ValueTask ExpectResourceResponseAsync(BedrockFakePeer peer, CancellationToken ct)
    {
        (int packetId, byte[] payload) = await peer.ReceiveAsync(ct);
        Assert.Equal(8, packetId);
        ResourcePackClientResponsePacket response = BedrockJoinCodecs.DecodeResourcePackClientResponse(payload);
        Assert.Empty(response.PackIds);
        Assert.Equal(ResourcePackResponseStatus.Completed, response.Status);
    }

    private static byte[] Bootstrap(Func<BootstrapPackets, byte[]> encode) => encode(new BootstrapPackets());

    private static byte[] Join(Func<JoinPackets, byte[]> encode) => encode(new JoinPackets());

    private static byte[] ItemRegistry() =>
        BedrockItemRegistryCodec.Encode(new BedrockItemRegistryPacket(
        [
            new BedrockItemState("minecraft:stone", 1, false, 2, new NbtCompound()),
        ]));

    private static byte[] EntityIdentifiers()
    {
        var pig = new NbtCompound();
        pig.PutString("id", "minecraft:pig");
        var list = new NbtList(NbtTagType.Compound) { pig };
        var root = new NbtCompound();
        root.Put("idlist", list);
        return BedrockJoinCodecs.Encode(new AvailableEntityIdentifiersPacket(root));
    }

    private static byte[] LevelChunk() =>
        BedrockWorldCodecs.Encode(new LevelChunkPacket(0, 0, 0, 0, null, false, [], BiomeOnlyPayload()));

    private static byte[] BiomeOnlyPayload()
    {
        var payload = new byte[26];
        payload[0] = 0x01;
        payload[1] = 0x00;
        Array.Fill(payload, (byte)0xFF, 2, 23);
        payload[25] = 0x00;
        return payload;
    }

    private sealed class BootstrapPackets
    {
        internal byte[] NetworkSettings() =>
            BedrockBootstrapCodecs.Encode(new NetworkSettingsPacket(1, BedrockCompressionAlgorithm.Deflate, false, 0, 0f));

        internal byte[] PlayStatus(PlayStatusCode status) =>
            BedrockBootstrapCodecs.Encode(new PlayStatusPacket(status));
    }

    private sealed class JoinPackets
    {
        internal byte[] EmptyResourceInfo() =>
            BedrockJoinCodecs.Encode(new ResourcePacksInfoPacket(false, false, false, false, Guid.Empty, string.Empty, []));

        internal byte[] EmptyResourceStack() =>
            BedrockJoinCodecs.Encode(new ResourcePackStackPacket(false, [], string.Empty, [], false, false));

        internal byte[] BiomeList() =>
            BedrockJoinCodecs.Encode(new BiomeDefinitionListPacket(2U, [1, 2, 3]));

        internal byte[] StartGame() =>
            BedrockJoinCodecs.Encode(new StartGamePacket(456L, 789UL, BedrockStartGameTrailerCodecs.EncodeTrailer(FakeTrailer())));

        internal static BedrockStartGameTrailer FakeTrailer() =>
            new(
                1,
                new BedrockVector3(0.5f, 70f, 0.5f),
                0f,
                0f,
                99UL,
                0,
                "minecraft:plains",
                0,
                1,
                0,
                false,
                1,
                new BedrockStartGamePosition(0, 70, 0),
                false,
                0,
                false,
                false,
                0,
                0U,
                false,
                string.Empty,
                0f,
                0f,
                false,
                true,
                false,
                0U,
                0U,
                false,
                false,
                [],
                [],
                false,
                false,
                false,
                1,
                4,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                "*",
                16,
                16,
                false,
                string.Empty,
                string.Empty,
                false,
                0,
                false,
                0,
                false,
                "test-level",
                "test-world",
                "00000000-0000-0000-0000-000000000000",
                false,
                0,
                false,
                0L,
                0,
                [],
                "correlation",
                false,
                "engine",
                new NbtCompound(),
                0UL,
                Guid.Empty,
                false,
                false,
                false,
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
    }
}
