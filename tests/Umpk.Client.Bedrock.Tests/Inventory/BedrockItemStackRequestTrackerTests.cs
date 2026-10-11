using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.Inventory;

public sealed class BedrockItemStackRequestTrackerTests
{
    private static BedrockItemStackResponse OkResponse(int requestId, params BedrockItemStackResponseContainer[] containers) =>
        new(0, requestId, containers.Length > 0, [.. containers]);

    private static BedrockItemStackResponseContainer Container(byte containerId, params BedrockItemStackResponseSlot[] slots) =>
        new(new BedrockFullContainerName(containerId, null), [.. slots]);

    [Fact]
    public void AcceptedResponseAppliesAuthoritativeBaseline()
    {
        var tracker = new BedrockItemStackRequestTracker();

        Assert.True(tracker.TryRegister(1, tracker.Generation));
        BedrockItemStackResponseOutcome outcome = tracker.ApplyResponse(
            OkResponse(1, Container(12, new BedrockItemStackResponseSlot(0, 0, 5, 7, string.Empty, string.Empty, 0))),
            tracker.Generation);

        Assert.Equal(BedrockItemStackResponseOutcome.Accepted, outcome);
        Assert.Equal(0, tracker.PendingCount);
        Assert.Equal(1, tracker.AcceptedCount);
        BedrockItemStackBaselineSlot? baseline = tracker.GetBaseline(12, null, 0);
        Assert.NotNull(baseline);
        Assert.Equal(5, baseline.Value.Count);
        Assert.Equal(7, baseline.Value.StackId);
    }

    [Fact]
    public void ErrorResponseReleasesPredictionWithoutTouchingBaseline()
    {
        var tracker = new BedrockItemStackRequestTracker();

        Assert.True(tracker.TryRegister(2, tracker.Generation));
        BedrockItemStackResponseOutcome outcome = tracker.ApplyResponse(
            new BedrockItemStackResponse(1, 2, false, []),
            tracker.Generation);

        Assert.Equal(BedrockItemStackResponseOutcome.Rejected, outcome);
        Assert.Equal(0, tracker.PendingCount);
        Assert.Equal(1, tracker.RejectedCount);
        Assert.Null(tracker.GetBaseline(12, null, 0));
    }

    [Fact]
    public void ErrorResponseWithContainersLeavesBaselineUntouched()
    {
        var tracker = new BedrockItemStackRequestTracker();

        Assert.True(tracker.TryRegister(3, tracker.Generation));
        BedrockItemStackResponseOutcome outcome = tracker.ApplyResponse(
            new BedrockItemStackResponse(
                1,
                3,
                true,
                [Container(12, new BedrockItemStackResponseSlot(1, 1, 9, 11, string.Empty, string.Empty, 0))]),
            tracker.Generation);

        Assert.Equal(BedrockItemStackResponseOutcome.Rejected, outcome);
        Assert.Null(tracker.GetBaseline(12, null, 1));
    }

    [Fact]
    public void ClearPendingKeepsGenerationWhileLateResponsesStayUnknown()
    {
        var tracker = new BedrockItemStackRequestTracker();
        int generation = tracker.Generation;

        Assert.True(tracker.TryRegister(-1, generation));
        tracker.ClearPending();

        Assert.Equal(0, tracker.PendingCount);
        Assert.Equal(generation, tracker.Generation);
        BedrockItemStackResponseOutcome late = tracker.ApplyResponse(
            OkResponse(-1, Container(28, new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0))),
            tracker.Generation);

        Assert.Equal(BedrockItemStackResponseOutcome.UnknownRequest, late);
        Assert.Equal(1, tracker.UnknownCount);
        Assert.Null(tracker.GetBaseline(28, null, 1));

        Assert.True(tracker.TryRegister(-1, tracker.Generation));
        BedrockItemStackResponseOutcome outcome = tracker.ApplyResponse(
            OkResponse(-1, Container(28, new BedrockItemStackResponseSlot(1, 1, 1, 9, string.Empty, string.Empty, 0))),
            tracker.Generation);

        Assert.Equal(BedrockItemStackResponseOutcome.Accepted, outcome);
        Assert.Equal(1, tracker.AcceptedCount);
    }

    [Fact]
    public void UnknownRequestIsCountedWithoutMutation()
    {
        var tracker = new BedrockItemStackRequestTracker();

        BedrockItemStackResponseOutcome outcome = tracker.ApplyResponse(
            OkResponse(99, Container(12, new BedrockItemStackResponseSlot(0, 0, 5, 7, string.Empty, string.Empty, 0))),
            tracker.Generation);

        Assert.Equal(BedrockItemStackResponseOutcome.UnknownRequest, outcome);
        Assert.Equal(1, tracker.UnknownCount);
        Assert.Null(tracker.GetBaseline(12, null, 0));
    }

    [Fact]
    public void DuplicateResponseIsCountedWithoutMutation()
    {
        var tracker = new BedrockItemStackRequestTracker();

        Assert.True(tracker.TryRegister(4, tracker.Generation));
        Assert.Equal(
            BedrockItemStackResponseOutcome.Accepted,
            tracker.ApplyResponse(
                OkResponse(4, Container(12, new BedrockItemStackResponseSlot(0, 0, 5, 7, string.Empty, string.Empty, 0))),
                tracker.Generation));
        BedrockItemStackResponseOutcome outcome = tracker.ApplyResponse(
            OkResponse(4, Container(12, new BedrockItemStackResponseSlot(0, 0, 1, 2, string.Empty, string.Empty, 0))),
            tracker.Generation);

        Assert.Equal(BedrockItemStackResponseOutcome.DuplicateResponse, outcome);
        Assert.Equal(1, tracker.DuplicateCount);
        Assert.Equal(5, tracker.GetBaseline(12, null, 0)!.Value.Count);
    }

    [Fact]
    public void DuplicatePendingRegistrationIsRejected()
    {
        var tracker = new BedrockItemStackRequestTracker();

        Assert.True(tracker.TryRegister(5, tracker.Generation));
        Assert.False(tracker.TryRegister(5, tracker.Generation));
        Assert.Equal(1, tracker.PendingCount);
    }

    [Fact]
    public void OverlappingDistinctRequestsCorrelateIndependently()
    {
        var tracker = new BedrockItemStackRequestTracker();

        Assert.True(tracker.TryRegister(6, tracker.Generation));
        Assert.True(tracker.TryRegister(7, tracker.Generation));
        Assert.Equal(
            BedrockItemStackResponseOutcome.Accepted,
            tracker.ApplyResponse(
                OkResponse(7, Container(29, new BedrockItemStackResponseSlot(3, 3, 2, 8, string.Empty, string.Empty, 1))),
                tracker.Generation));

        Assert.Equal(1, tracker.PendingCount);
        Assert.Null(tracker.GetBaseline(29, null, 0));
        Assert.Equal(2, tracker.GetBaseline(29, null, 3)!.Value.Count);
    }

    [Fact]
    public void StaleGenerationResponseIsCountedWithoutMutation()
    {
        var tracker = new BedrockItemStackRequestTracker();
        int generation = tracker.Generation;

        Assert.True(tracker.TryRegister(8, generation));
        tracker.Invalidate();
        BedrockItemStackResponseOutcome outcome = tracker.ApplyResponse(
            OkResponse(8, Container(12, new BedrockItemStackResponseSlot(0, 0, 5, 7, string.Empty, string.Empty, 0))),
            generation);

        Assert.Equal(BedrockItemStackResponseOutcome.StaleGeneration, outcome);
        Assert.Equal(1, tracker.StaleCount);
        Assert.Null(tracker.GetBaseline(12, null, 0));
    }

    [Fact]
    public void InvalidateClearsPendingWork()
    {
        var tracker = new BedrockItemStackRequestTracker();

        Assert.True(tracker.TryRegister(9, tracker.Generation));
        tracker.Invalidate();

        Assert.Equal(0, tracker.PendingCount);
        Assert.NotEqual(1, tracker.Generation);
    }

    [Fact]
    public void RegistrationAfterCompletionIsAllowed()
    {
        var tracker = new BedrockItemStackRequestTracker();

        Assert.True(tracker.TryRegister(10, tracker.Generation));
        Assert.Equal(
            BedrockItemStackResponseOutcome.Rejected,
            tracker.ApplyResponse(new BedrockItemStackResponse(1, 10, false, []), tracker.Generation));
        Assert.True(tracker.TryRegister(10, tracker.Generation));
    }

    [Fact]
    public void CompletionGenerationUpdatesOnRequestIdReuseAfterInvalidate()
    {
        var tracker = new BedrockItemStackRequestTracker();

        Assert.True(tracker.TryRegister(11, tracker.Generation));
        Assert.Equal(
            BedrockItemStackResponseOutcome.Accepted,
            tracker.ApplyResponse(
                OkResponse(11, Container(12, new BedrockItemStackResponseSlot(0, 0, 5, 7, string.Empty, string.Empty, 0))),
                tracker.Generation));
        tracker.Invalidate();
        Assert.True(tracker.TryRegister(11, tracker.Generation));
        Assert.Equal(
            BedrockItemStackResponseOutcome.Accepted,
            tracker.ApplyResponse(
                OkResponse(11, Container(12, new BedrockItemStackResponseSlot(0, 0, 6, 8, string.Empty, string.Empty, 0))),
                tracker.Generation));
        BedrockItemStackResponseOutcome outcome = tracker.ApplyResponse(
            OkResponse(11, Container(12, new BedrockItemStackResponseSlot(0, 0, 1, 2, string.Empty, string.Empty, 0))),
            tracker.Generation);

        Assert.Equal(BedrockItemStackResponseOutcome.DuplicateResponse, outcome);
        Assert.Equal(6, tracker.GetBaseline(12, null, 0)!.Value.Count);
    }

    [Fact]
    public void DuplicateSlotsInOneResponseCountAsOneFreshKey()
    {
        var tracker = new BedrockItemStackRequestTracker();

        for (int index = 0; index < BedrockItemStackRequestTracker.MaxBaselineEntries - 1; index++)
        {
            byte slot = (byte)(index % 256);
            byte container = (byte)(index / 256);
            Assert.True(tracker.TryRegister(100000 + index, tracker.Generation));
            Assert.Equal(
                BedrockItemStackResponseOutcome.Accepted,
                tracker.ApplyResponse(
                    OkResponse(100000 + index, Container(container, new BedrockItemStackResponseSlot(slot, slot, 1, index, string.Empty, string.Empty, 0))),
                    tracker.Generation));
        }

        Assert.True(tracker.TryRegister(200000, tracker.Generation));
        BedrockItemStackResponseOutcome outcome = tracker.ApplyResponse(
            new BedrockItemStackResponse(
                0,
                200000,
                true,
                [new BedrockItemStackResponseContainer(
                    new BedrockFullContainerName(99, null),
                    [new BedrockItemStackResponseSlot(7, 7, 1, 1, string.Empty, string.Empty, 0),
                     new BedrockItemStackResponseSlot(7, 7, 2, 2, string.Empty, string.Empty, 0)])]),
            tracker.Generation);

        Assert.Equal(BedrockItemStackResponseOutcome.Accepted, outcome);
        Assert.Equal(2, tracker.GetBaseline(99, null, 7)!.Value.Count);
    }

    [Fact]
    public void BatchDuplicateRequestIdsResolveFirstWins()
    {
        var tracker = new BedrockItemStackRequestTracker();

        Assert.True(tracker.TryRegister(50, tracker.Generation));
        IReadOnlyList<BedrockItemStackResponseOutcome> outcomes = tracker.ApplyResponses(
            [OkResponse(50, Container(12, new BedrockItemStackResponseSlot(0, 0, 5, 7, string.Empty, string.Empty, 0))),
             OkResponse(50, Container(12, new BedrockItemStackResponseSlot(0, 0, 1, 2, string.Empty, string.Empty, 0)))],
            tracker.Generation);

        Assert.Equal(BedrockItemStackResponseOutcome.Accepted, outcomes[0]);
        Assert.Equal(BedrockItemStackResponseOutcome.DuplicateResponse, outcomes[1]);
        Assert.Equal(0, tracker.PendingCount);
        Assert.Equal(1, tracker.AcceptedCount);
        Assert.Equal(1, tracker.DuplicateCount);
        Assert.Equal(5, tracker.GetBaseline(12, null, 0)!.Value.Count);
    }

    [Fact]
    public void BatchOverflowCommitsNothing()
    {
        var tracker = new BedrockItemStackRequestTracker();

        for (int index = 0; index < BedrockItemStackRequestTracker.MaxBaselineEntries - 1; index++)
        {
            byte slot = (byte)(index % 256);
            byte container = (byte)(index / 256);
            Assert.True(tracker.TryRegister(400000 + index, tracker.Generation));
            Assert.Equal(
                BedrockItemStackResponseOutcome.Accepted,
                tracker.ApplyResponse(
                    OkResponse(400000 + index, Container(container, new BedrockItemStackResponseSlot(slot, slot, 1, index, string.Empty, string.Empty, 0))),
                    tracker.Generation));
        }

        Assert.True(tracker.TryRegister(500001, tracker.Generation));
        Assert.True(tracker.TryRegister(500002, tracker.Generation));
        Assert.Throws<InvalidOperationException>(() => tracker.ApplyResponses(
            [OkResponse(500001, Container(200, new BedrockItemStackResponseSlot(1, 1, 1, 1, string.Empty, string.Empty, 0))),
             OkResponse(500002, Container(201, new BedrockItemStackResponseSlot(2, 2, 1, 2, string.Empty, string.Empty, 0)))],
            tracker.Generation));

        Assert.Equal(2, tracker.PendingCount);
        Assert.Equal(BedrockItemStackRequestTracker.MaxBaselineEntries - 1, tracker.AcceptedCount);
        Assert.Null(tracker.GetBaseline(200, null, 1));
        Assert.Null(tracker.GetBaseline(201, null, 2));
    }
}
