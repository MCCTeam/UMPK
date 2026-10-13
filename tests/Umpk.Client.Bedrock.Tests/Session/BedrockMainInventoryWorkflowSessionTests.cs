using System.Collections.Immutable;
using Umpk.Client.Bedrock.Actions;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;
using Fixture = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.OwnershipSession;

namespace Umpk.Client.Bedrock.Session;

[Collection("Bedrock pre-ready inventory capture")]
public sealed class BedrockMainInventoryWorkflowSessionTests
{
    private static int s_openCoordinate;
    private static readonly BedrockContainerAddress s_player = new(0, 0, null);
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockItemInstance Item(ushort count = 5, int id = 7, short material = 306) => new(material, count, 0, id, 0, []);
    private static Task<BedrockItemStackSubmitResult> Submit(Fixture fixture, string operation, CancellationToken ct) => operation switch
    {
        "take" => fixture.Session.SubmitInventoryTakeAsync(s_player, 8, 9, 1, ct).AsTask(),
        "place" => fixture.Session.SubmitInventoryPlaceAsync(s_player, 8, 9, 1, ct).AsTask(),
        _ => fixture.Session.SubmitInventorySwapAsync(s_player, 8, 9, ct).AsTask(),
    };

    [Theory]
    [InlineData("take")]
    [InlineData("place")]
    [InlineData("swap")]
    public async Task MainTakePlaceSwapWithoutWindowOpensAndWaitsForActual46(string operation)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), operation == "swap" ? Item(3, 9, 1) : Air(), ct);
        BedrockInventorySnapshot before = Read(fixture);
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, operation, ct);
        await ExpectOpenAsync(fixture, ct);
        Assert.False(pending.IsCompleted);
        Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
        Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
        Assert.Same(before.AuthorityReceipts[8], Read(fixture).AuthorityReceipts[8]);
        await OpenAsync(fixture, ct);
        BedrockItemStackSubmitResult sent = await pending.WaitAsync(ct);
        Assert.Equal(-3, sent.RequestId);
        BedrockItemStackRequest request = await fixture.ReceiveRequestAsync(ct);
        Assert.Equal(operation == "take" ? BedrockItemStackActionType.Take : operation == "place" ? BedrockItemStackActionType.Place : BedrockItemStackActionType.Swap, Assert.Single(request.Actions).Type);
        await AcceptAsync(fixture, request.RequestId, operation == "swap" ? (byte)3 : (byte)4, operation == "swap" ? (byte)5 : (byte)1, ct);
        Assert.Equal(operation == "swap" ? 1 : 306, Read(fixture).Items[8].NetworkId);
        Assert.Equal(1, OpenCount(fixture));
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
    }

    [Fact]
    public async Task ExistingCurrentNormalOwnerSkips33AndUsesCurrentReceipts()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        await OpenAsync(fixture, ct);
        await PublishAsync(fixture, Item(6, 21), Air(), ct);
        BedrockItemStackSubmitResult sent = await Submit(fixture, "take", ct);
        BedrockTakePayload payload = Assert.IsType<BedrockTakePayload>(Assert.Single((await fixture.ReceiveRequestAsync(ct)).Actions).Payload);
        Assert.Equal(21, payload.Source.StackId);
        Assert.Equal(0, OpenCount(fixture));
        await fixture.RejectAsync(sent.RequestId, ct);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("count")]
    [InlineData("material")]
    public async Task FreshAuthorityDuringOpeningIsRevalidated(string replacement)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", ct);
        await ExpectOpenAsync(fixture, ct);
        await PublishAsync(fixture, replacement == "count" ? Air() : Item(6, 21), replacement == "material" ? Item(3, 9, 1) : Air(), ct);
        await OpenAsync(fixture, ct);
        if (replacement == "valid")
        {
            BedrockItemStackSubmitResult sent = await pending.WaitAsync(ct);
            Assert.Equal(21, Assert.IsType<BedrockTakePayload>(Assert.Single((await fixture.ReceiveRequestAsync(ct)).Actions).Payload).Source.StackId);
            await fixture.RejectAsync(sent.RequestId, ct);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
            Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            await PublishAsync(fixture, Item(), Air(), ct);
            Assert.Equal(-3, (await Submit(fixture, "take", ct)).RequestId);
            await fixture.RejectAsync(-3, ct);
        }
    }

    [Theory]
    [InlineData("take", 0)]
    [InlineData("place", 6)]
    [InlineData("swap", 0)]
    public async Task InvalidMainPreflightEmitsNeitherPacketNorBurnsId(string operation, byte count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        int commits = fixture.Transport.CommittedCount;
        Task<BedrockItemStackSubmitResult> invalid = operation == "swap" ? Submit(fixture, operation, ct) : operation == "take"
            ? fixture.Session.SubmitInventoryTakeAsync(s_player, 8, 9, count, ct).AsTask()
            : fixture.Session.SubmitInventoryPlaceAsync(s_player, 8, 9, count, ct).AsTask();
        await Assert.ThrowsAsync<InvalidOperationException>(() => invalid);
        Assert.Equal(commits, fixture.Transport.CommittedCount);
        Assert.Equal(0, OpenCount(fixture));
        await OpenAsync(fixture, ct);
        Assert.Equal(-3, (await Submit(fixture, "take", ct)).RequestId);
        await fixture.RejectAsync(-3, ct);
    }

    [Fact]
    public async Task LateActual46AfterCanceledWaitQualifiesNextCaller()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        using var caller = new CancellationTokenSource();
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", caller.Token);
        await ExpectOpenAsync(fixture, ct);
        await caller.CancelAsync();
        OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(caller.Token, failure.CancellationToken);
        Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
        await OpenAsync(fixture, ct);
        Assert.Equal(-3, (await Submit(fixture, "take", ct)).RequestId);
        Assert.Equal(1, OpenCount(fixture));
        await fixture.RejectAsync(-3, ct);
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("grant")]
    [InlineData("result")]
    public async Task Normal46BeforeHeld33ResultWaitsForAccounting(string phase)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        var gate = phase == "capacity" ? fixture.Transport.HoldNextReservation() : phase == "grant" ? fixture.Transport.HoldNextGrantedCommit() : fixture.Transport.HoldNextTransferResult();
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await OpenAsync(fixture, ct);
            if (phase != "capacity")
            {
                Assert.False(pending.IsCompleted);
                Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            }
            gate.Allow();
            Assert.Equal(-3, (await pending.WaitAsync(ct)).RequestId);
            Assert.Equal(phase == "capacity" ? 0 : 1, OpenCount(fixture));
            await fixture.RejectAsync(-3, ct);
        }
        finally { gate.Allow(); }
    }

    [Theory]
    [InlineData("close")]
    [InlineData("foreign")]
    [InlineData("foreign-entity")]
    [InlineData("registry")]
    [InlineData("life")]
    [InlineData("world")]
    public async Task BoundaryWhileWaitingRetiresBefore147(string boundary)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", ct);
        await ExpectOpenAsync(fixture, ct);
        await BoundaryAsync(fixture, boundary, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(ct));
        Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
        if (boundary is "close" or "foreign" or "foreign-entity")
        {
            await OpenAsync(fixture, ct);
            Assert.Equal(-3, (await Submit(fixture, "take", ct)).RequestId);
            Assert.Equal(1, OpenCount(fixture));
            await fixture.RejectAsync(-3, ct);
        }
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("wait46")]
    public async Task CallerCancelBefore147PreservesAuthorityAndAllocator(string phase)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        BedrockInventorySnapshot before = Read(fixture);
        using var caller = new CancellationTokenSource();
        var gate = phase == "capacity" ? fixture.Transport.HoldNextInlineCancellation() : null;
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", caller.Token);
        try
        {
            if (gate is not null) await gate.Entered.WaitAsync(ct); else await ExpectOpenAsync(fixture, ct);
            await caller.CancelAsync();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Same(before.AuthorityReceipts[8], Read(fixture).AuthorityReceipts[8]);
            Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            gate?.Allow();
            await OpenAsync(fixture, ct);
            Assert.Equal(-3, (await Submit(fixture, "take", ct)).RequestId);
            await fixture.RejectAsync(-3, ct);
        }
        finally { gate?.Allow(); }
    }

    [Theory]
    [InlineData("caller", false)]
    [InlineData("close", false)]
    [InlineData("dispose", false)]
    [InlineData("caller", true)]
    [InlineData("close", true)]
    [InlineData("dispose", true)]
    public async Task Granted147RetirementReturnsActualResultAndJoinsDrain(string boundary, bool afterTransfer)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        await OpenAsync(fixture, ct);
        using var caller = new CancellationTokenSource();
        var gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", caller.Token);
        Task? dispose = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (boundary == "caller") await caller.CancelAsync();
            else if (boundary == "dispose") dispose = fixture.Session.DisposeAsync().AsTask();
            else await BoundaryAsync(fixture, "close", ct);
            Assert.False(pending.IsCompleted);
            if (dispose is not null) Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
            gate.Allow();
            Assert.Equal(-3, (await pending.WaitAsync(ct)).RequestId);
            Assert.Single(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            if (dispose is not null) await dispose.WaitAsync(ct);
        }
        finally { gate.Allow(); }
    }

    [Theory]
    [InlineData("close")]
    [InlineData("foreign")]
    [InlineData("normal-replacement")]
    public async Task ConfirmedOwnerRetiresBefore147Capacity(string boundary)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        await OpenAsync(fixture, ct);
        var gate = fixture.Transport.HoldNextInlineCancellation();
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await BoundaryAsync(fixture, boundary, ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(ct));
            Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
            if (boundary == "close")
            {
                // A pending147 at the actual47 boundary retires egress even if inline cleanup removes its tracker first.
                await OpenAsync(fixture, ct);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, "take", ct));
                Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            }
        }
        finally { gate.Allow(); }
    }

    [Theory]
    [InlineData("main")]
    [InlineData("hotbar")]
    [InlineData("opener")]
    [InlineData("raw")]
    public async Task CompetingAdmissionsCannotStealWorkflowOwner(string competitor)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", ct);
        await ExpectOpenAsync(fixture, ct);
        int commits = fixture.Transport.CommittedCount;
        if (competitor == "opener") await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitNormalInventoryOpenAsync(ct).AsTask());
        else if (competitor == "main") await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, "take", ct));
        else if (competitor == "hotbar") await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SubmitInventoryTakeAsync(s_player, 0, 1, 1, ct).AsTask());
        else
        {
            int generation = fixture.Session.ItemStackTracker.Generation;
            Assert.True(fixture.Session.ItemStackTracker.TryRegister(-101, generation));
            Assert.True(fixture.Session.ActionCoordinator.TryClaim(-101, BedrockActionEmission.Standalone));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SendItemStackRequestAsync(BedrockItemStackEgress.BuildTake(-101, 0, 1, 1, 7, 0), -101, generation, ct).AsTask());
            Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
            Assert.True(fixture.Session.ActionCoordinator.IsClaimed(-101));
            Assert.True(fixture.Session.ItemStackTracker.TryCancel(-101, generation));
            fixture.Session.ActionCoordinator.Release(-101);
        }
        Assert.Equal(commits, fixture.Transport.CommittedCount);
        await OpenAsync(fixture, ct);
        Assert.Equal(-3, (await pending.WaitAsync(ct)).RequestId);
        await fixture.RejectAsync(-3, ct);
    }

    [Fact]
    public async Task DuplicateRawIncumbentCannotReleaseWorkflowRequest()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        await OpenAsync(fixture, ct);
        var gate = fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            int generation = fixture.Session.ItemStackTracker.Generation;
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SendItemStackRequestAsync(BedrockItemStackEgress.BuildTake(-3, 8, 9, 1, 7, 0), -3, generation, ct).AsTask());
            Assert.True(fixture.Session.ActionCoordinator.IsClaimed(-3));
            Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
            gate.Allow();
            Assert.Equal(-3, (await pending.WaitAsync(ct)).RequestId);
            await fixture.RejectAsync(-3, ct);
        }
        finally { gate.Allow(); }
    }

    [Theory]
    [InlineData("take", false)]
    [InlineData("take", true)]
    [InlineData("place", false)]
    [InlineData("place", true)]
    [InlineData("swap", false)]
    [InlineData("swap", true)]
    public async Task RawMainCannotBypassOwnedWorkflow(string action, bool withBefore)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Item(3, 9), ct);
        int generation = fixture.Session.ItemStackTracker.Generation;
        Assert.True(fixture.Session.ItemStackTracker.TryRegister(-101, generation));
        Assert.True(fixture.Session.ActionCoordinator.TryClaim(-101, BedrockActionEmission.Standalone));
        BedrockItemStackRequestPacket packet = action == "take" ? BedrockItemStackEgress.BuildTake(-101, 8, 9, 1, 7, 9) : action == "place" ? BedrockItemStackEgress.BuildPlace(-101, 8, 9, 1, 7, 9) : BedrockItemStackEgress.BuildSwap(-101, 8, 9, 7, 9);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SendItemStackRequestAsync(packet, -101, generation, ct, withBefore ? Read(fixture) : null).AsTask());
        Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
        Assert.Equal(0, OpenCount(fixture));
        Assert.True(fixture.Session.ActionCoordinator.IsClaimed(-101));
        Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
        Assert.True(fixture.Session.ItemStackTracker.TryCancel(-101, generation));
        fixture.Session.ActionCoordinator.Release(-101);
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("wait46")]
    public async Task OpeningDeadlineClassifiesTimeoutAndAllowsLateCurrentOwnerRecovery(string phase)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        var clock = new OpeningTimeProvider();
        await using Fixture fixture = await Fixture.CreateAsync(ct, timeProvider: clock);
        await PublishAsync(fixture, Item(), Air(), ct);
        object[] priorTimerIdentities = clock.LiveTimerIdentities;
        int priorTimers = priorTimerIdentities.Length;
        var gate = phase == "capacity" ? fixture.Transport.HoldNextInlineCancellation() : null;
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", ct);
        try
        {
            if (gate is not null) await gate.Entered.WaitAsync(ct); else await ExpectOpenAsync(fixture, ct);
            object ownedDeadline = Assert.Single(clock.LiveTimerIdentities.Except(priorTimerIdentities));
            clock.Advance(TimeSpan.FromSeconds(15));
            await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(ct));
            Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            Assert.Equal(priorTimers, clock.ActiveTimers);
            Assert.Equal(priorTimerIdentities, clock.LiveTimerIdentities);
            Assert.True(clock.WasFired(ownedDeadline));
            Assert.True(clock.WasDisposed(ownedDeadline));
            gate?.Allow();
            await OpenAsync(fixture, ct);
            Assert.Equal(-3, (await Submit(fixture, "take", ct)).RequestId);
            await fixture.RejectAsync(-3, ct);
        }
        finally { gate?.Allow(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningDeadlineAfter33GrantWaitsForHonestAccounting(bool afterTransfer)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        var clock = new OpeningTimeProvider();
        await using Fixture fixture = await Fixture.CreateAsync(ct, timeProvider: clock);
        await PublishAsync(fixture, Item(), Air(), ct);
        object[] priorTimerIdentities = clock.LiveTimerIdentities;
        int priorTimers = priorTimerIdentities.Length;
        var gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
            object ownedDeadline = Assert.Single(clock.LiveTimerIdentities.Except(priorTimerIdentities));
            clock.Advance(TimeSpan.FromSeconds(15));
            Assert.False(pending.IsCompleted);
            gate.Allow();
            await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(ct));
            Assert.Equal(1, OpenCount(fixture));
            Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            Assert.Equal(priorTimers, clock.ActiveTimers);
            Assert.Equal(priorTimerIdentities, clock.LiveTimerIdentities);
            Assert.True(clock.WasFired(ownedDeadline));
            Assert.True(clock.WasDisposed(ownedDeadline));
            await OpenAsync(fixture, ct);
            Assert.Equal(-3, (await Submit(fixture, "take", ct)).RequestId);
            await fixture.RejectAsync(-3, ct);
        }
        finally { gate.Allow(); }
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("wait46")]
    [InlineData("grant")]
    [InlineData("result")]
    public async Task DisposeOwnsOpeningWorkflowDrainThroughGranted33(string phase)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        var gate = phase == "capacity" ? fixture.Transport.HoldNextInlineCancellation() : phase == "grant" ? fixture.Transport.HoldNextGrantedCommit() : phase == "result" ? fixture.Transport.HoldNextTransferResult() : null;
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", ct);
        try
        {
            if (gate is null) await ExpectOpenAsync(fixture, ct); else await gate.Entered.WaitAsync(ct);
            Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
            Task dispose = fixture.Session.DisposeAsync().AsTask();
            if (phase is "grant" or "result")
            {
                Assert.False(dispose.IsCompleted);
                Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
            }
            gate?.Allow();
            await Assert.ThrowsAsync<BedrockSessionException>(() => pending.WaitAsync(ct));
            await dispose.WaitAsync(ct);
            Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            Assert.Equal(phase == "capacity" ? 0 : 1, OpenCount(fixture));
            Assert.True(fixture.Transport.AbortEntered.Task.IsCompleted);
            Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
        }
        finally { gate?.Allow(); }
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("exception")]
    public async Task FailedImplicit33WithoutReal46CannotAdmit147OrPoisonRetry(string failure)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        if (failure == "refusal") fixture.Transport.RefuseNextReservation(); else fixture.Transport.ThrowNextReservation();
        if (failure == "refusal") await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(fixture, "take", ct));
        else await Assert.ThrowsAsync<IOException>(() => Submit(fixture, "take", ct));
        Assert.Equal(0, OpenCount(fixture));
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
        Task<BedrockItemStackSubmitResult> retry = Submit(fixture, "take", ct);
        await ExpectOpenAsync(fixture, ct);
        await OpenAsync(fixture, ct);
        Assert.Equal(-3, (await retry.WaitAsync(ct)).RequestId);
        await fixture.RejectAsync(-3, ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful33CaptureIsOptInDefensiveAndBounded(bool enabled)
    {
        string? previous = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", enabled ? "1" : null);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            CancellationToken ct = deadline.Token;
            await using Fixture fixture = await Fixture.CreateAsync(ct);
            await PublishAsync(fixture, Item(), Air(), ct);
            Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", ct);
            await ExpectOpenAsync(fixture, ct);
            await WaitAsync(() => OpenCount(fixture) == 1, ct);
            Assert.Equal(enabled ? 1 : 0, fixture.Session.CapturedNormalInventoryOpenPayloads.Count);
            if (enabled)
            {
                byte[] captured = Assert.Single(fixture.Session.CapturedNormalInventoryOpenPayloads);
                Assert.Equal(6, captured[0]);
                captured[0] = 255;
                Assert.Equal(6, Assert.Single(fixture.Session.CapturedNormalInventoryOpenPayloads)[0]);
            }
            await OpenAsync(fixture, ct);
            Assert.Equal(-3, (await pending.WaitAsync(ct)).RequestId);
            await fixture.RejectAsync(-3, ct);
            for (int i = 0; i < 34; i++)
                await fixture.Session.SubmitNormalInventoryOpenAsync(ct);
            Assert.Equal(enabled ? 32 : 0, fixture.Session.CapturedNormalInventoryOpenPayloads.Count);
        }
        finally { Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", previous); }
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("refusal")]
    [InlineData("exception")]
    [InlineData("granted")]
    public async Task Capture33RequiresSuccessfulReturnedTransportOwnership(string phase)
    {
        string? previous = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", "1");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            CancellationToken ct = deadline.Token;
            await using Fixture fixture = await Fixture.CreateAsync(ct);
            using var caller = new CancellationTokenSource();
            if (phase == "refusal") fixture.Transport.RefuseNextReservation();
            if (phase == "exception") fixture.Transport.ThrowNextReservation();
            var gate = phase == "capacity" ? fixture.Transport.HoldNextInlineCancellation() : phase == "granted" ? fixture.Transport.HoldNextTransferResult() : null;
            Task<(ulong TargetRuntimeId, byte[] OutboundPayload)> pending = fixture.Session.SubmitNormalInventoryOpenAsync(caller.Token).AsTask();
            try
            {
                if (gate is not null)
                {
                    await gate.Entered.WaitAsync(ct);
                    Assert.Empty(fixture.Session.CapturedNormalInventoryOpenPayloads);
                    await caller.CancelAsync();
                }
                if (phase == "granted")
                {
                    Assert.False(pending.IsCompleted);
                    gate!.Allow();
                    byte[] payload = (await pending.WaitAsync(ct)).OutboundPayload;
                    Assert.Equal(payload, Assert.Single(fixture.Session.CapturedNormalInventoryOpenPayloads));
                    payload[0] = 0;
                    Assert.Equal(6, Assert.Single(fixture.Session.CapturedNormalInventoryOpenPayloads)[0]);
                }
                else
                {
                    if (phase == "capacity") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                    else if (phase == "exception") await Assert.ThrowsAsync<IOException>(() => pending);
                    else await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
                    Assert.Empty(fixture.Session.CapturedNormalInventoryOpenPayloads);
                }
            }
            finally { gate?.Allow(); }
        }
        finally { Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", previous); }
    }

    [Theory]
    [InlineData("none")]
    [InlineData("valid")]
    [InlineData("items")]
    [InlineData("known")]
    [InlineData("corrections")]
    [InlineData("receipts")]
    public async Task DuplicateRawAfter147ReturnCannotRetirePendingResponseOwner(string retainedShape)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        await OpenAsync(fixture, ct);
        BedrockInventorySnapshot before = Read(fixture);
        BedrockInventorySnapshot? rawBefore = retainedShape switch
        {
            "none" => null,
            "items" => before with { Items = default },
            "known" => before with { KnownSlots = [true] },
            "corrections" => before with { ResponseCorrections = [null] },
            "receipts" => before with { AuthorityReceipts = default },
            _ => before,
        };
        BedrockItemStackSubmitResult actual = await Submit(fixture, "take", ct);
        Assert.Equal(actual.RequestId, (await fixture.ReceiveRequestAsync(ct)).RequestId);
        Assert.Equal(1, fixture.Session.PendingItemStackReconciliationCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.SendItemStackRequestAsync(
            BedrockItemStackEgress.BuildTake(actual.RequestId, 8, 9, 1, 7, 0), actual.RequestId,
            fixture.Session.ItemStackTracker.Generation, ct, rawBefore).AsTask());
        Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
        Assert.True(fixture.Session.ActionCoordinator.IsClaimed(actual.RequestId));
        Assert.Equal(1, fixture.Session.PendingItemStackReconciliationCount);
        Assert.Single(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
        await AcceptAsync(fixture, actual.RequestId, 4, 1, ct);
        Assert.Equal(1, fixture.Session.ItemStackTracker.AcceptedCount);
        Assert.Equal(1, Read(fixture).Items[9].Count);
        Assert.NotSame(before.AuthorityReceipts[9], Read(fixture).AuthorityReceipts[9]);
    }

    [Fact]
    public async Task CallerCancellationAtRetainedPublicationKeepsOriginalTokenAndOwnedCleanup()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await PublishAsync(fixture, Item(), Air(), ct);
        await OpenAsync(fixture, ct);
        BedrockInventorySnapshot before = Read(fixture);
        using var caller = new CancellationTokenSource();
        int barrierCalls = 0;
        fixture.Session.BeforeOrdinaryMainInventoryRetention = () => { barrierCalls++; caller.Cancel(); };
        try
        {
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Submit(fixture, "take", caller.Token));
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Equal(1, barrierCalls);
            Assert.DoesNotContain(fixture.Session.WireTranscript, row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 147);
            Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
            Assert.Equal(0, fixture.Session.PendingItemStackReconciliationCount);
            Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
            Assert.Same(before.AuthorityReceipts[8], Read(fixture).AuthorityReceipts[8]);
            Assert.Same(before.AuthorityReceipts[9], Read(fixture).AuthorityReceipts[9]);
            fixture.Session.BeforeOrdinaryMainInventoryRetention = null;
            Assert.Equal(-5, (await Submit(fixture, "take", ct)).RequestId);
            Assert.Equal(-5, (await fixture.ReceiveRequestAsync(ct)).RequestId);
            await fixture.RejectAsync(-5, ct);
            await fixture.Session.DisposeAsync().AsTask().WaitAsync(ct);
            Assert.True(fixture.Transport.AbortEntered.Task.IsCompleted);
        }
        finally { fixture.Session.BeforeOrdinaryMainInventoryRetention = null; }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("caller")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task OwnedOpeningTimerIsDisposedAcrossSuccessCancellationAndTeardown(string ending)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = deadline.Token;
        var clock = new OpeningTimeProvider();
        await using Fixture fixture = await Fixture.CreateAsync(ct, timeProvider: clock);
        await PublishAsync(fixture, Item(), Air(), ct);
        object[] earlierTimers = clock.LiveTimerIdentities;
        using var caller = new CancellationTokenSource();
        Task<BedrockItemStackSubmitResult> pending = Submit(fixture, "take", caller.Token);
        await ExpectOpenAsync(fixture, ct);
        object owned = Assert.Single(clock.LiveTimerIdentities.Except(earlierTimers));
        if (ending == "success")
        {
            await OpenAsync(fixture, ct);
            Assert.Equal(-3, (await pending.WaitAsync(ct)).RequestId);
            await fixture.RejectAsync(-3, ct);
        }
        else if (ending == "caller")
        {
            await caller.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        else if (ending == "close")
        {
            await BoundaryAsync(fixture, "close", ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        }
        else
        {
            await fixture.Session.DisposeAsync().AsTask().WaitAsync(ct);
            await Assert.ThrowsAsync<BedrockSessionException>(() => pending);
        }
        Assert.False(clock.WasFired(owned));
        Assert.True(clock.WasDisposed(owned));
        Assert.DoesNotContain(owned, clock.LiveTimerIdentities);
    }

    private static BedrockInventorySnapshot Read(Fixture fixture) => fixture.Session.InventoryStore.GetSnapshot(s_player)!.Value;
    private static int OpenCount(Fixture fixture) => fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Outbound && row.PacketId == 33);
    private static async Task PublishAsync(Fixture fixture, BedrockItemInstance source, BedrockItemInstance destination, CancellationToken ct)
    {
        BedrockItemInstance[] items = Enumerable.Repeat(Air(), 36).ToArray(); items[0] = Item(); items[8] = source; items[9] = destination;
        object? prior = fixture.Session.InventoryStore.GetSnapshot(s_player)?.AuthorityReceipts[8];
        await fixture.SendAsync(49, BedrockInventoryCodecs.Encode(new BedrockInventoryContentPacket(0, items, s_player, Air())), ct);
        await WaitAsync(() => !ReferenceEquals(prior, Read(fixture).AuthorityReceipts[8]), ct);
    }
    private static async Task ExpectOpenAsync(Fixture fixture, CancellationToken ct)
    {
        for (int i = 0; i < 16; i++)
        {
            (int id, byte[] payload) = await fixture.ReceiveAsync(ct);
            if (id is not (33 or 147)) continue;
            Assert.Equal(33, id);
            var reader = new BedrockReader(payload, BedrockCodecLimits.Default);
            Assert.Equal(6, reader.ReadByte()); Assert.Equal(fixture.Session.Snapshot.LocalRuntimeEntityId, reader.ReadUInt64VarInt()); Assert.False(reader.ReadBoolean()); Assert.True(reader.End);
            return;
        }
        Assert.Fail("Expected owned opening packet33.");
    }
    private static async Task OpenAsync(Fixture fixture, CancellationToken ct)
    {
        int coordinate = Interlocked.Increment(ref s_openCoordinate);
        await fixture.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(3, -1, coordinate, 0, 0, -1)), ct);
        await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(3) is { } window && window.X == coordinate, ct);
    }
    private static async Task BoundaryAsync(Fixture fixture, string boundary, CancellationToken ct)
    {
        if (boundary == "life") fixture.Session.HandleRespawn();
        else if (boundary == "world") fixture.Session.HandleWorldReplacement();
        else if (boundary == "registry")
        {
            int count = fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 162);
            await fixture.SendAsync(162, BedrockItemRegistryCodec.Encode(new BedrockItemRegistryPacket([])), ct);
            await WaitAsync(() => fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 162) > count, ct);
        }
        else if (boundary == "normal-replacement") await OpenAsync(fixture, ct);
        else if (boundary is "foreign" or "foreign-entity")
        {
            await fixture.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(4, boundary == "foreign" ? (sbyte)0 : (sbyte)-1, 0, 0, 0, boundary == "foreign" ? -1 : 0)), ct);
            await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(4) is not null, ct);
        }
        else
        {
            int count = fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 47);
            await fixture.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(3, -1, true)), ct);
            await WaitAsync(() => fixture.Session.WireTranscript.Count(row => row.Direction == BedrockWireDirection.Inbound && row.PacketId == 47) > count && fixture.Session.ContainerWindows.GetWindow(3) is null, ct);
        }
    }
    private static async Task AcceptAsync(Fixture fixture, int id, byte source, byte destination, CancellationToken ct)
    {
        var response = new BedrockItemStackResponsePacket([new(0, id, true, [new(new(28, null), [new(8, 8, source, 41, "", "", 0)]), new(new(29, null), [new(9, 9, destination, 42, "", "", 0)])])]);
        await fixture.SendAsync(148, BedrockItemStackCodecs.Encode(response), ct);
        await WaitAsync(() => fixture.Session.PendingItemStackReconciliationCount == 0, ct);
    }
    private static async Task WaitAsync(Func<bool> predicate, CancellationToken ct) { while (!predicate()) await Task.Delay(5, ct); }

    private sealed class OpeningTimeProvider : TimeProvider
    {
        private readonly List<OpeningTimer> _timers = [];
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        internal object[] LiveTimerIdentities { get { lock (_timers) return _timers.Where(timer => !timer.Disposed).Cast<object>().ToArray(); } }
        internal bool WasDisposed(object identity) => ((OpeningTimer)identity).Disposed;
        internal bool WasFired(object identity) => ((OpeningTimer)identity).Fired;
        internal int ActiveTimers { get { lock (_timers) return _timers.Count(timer => !timer.Disposed); } }
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new OpeningTimer(this, callback, state, _now + dueTime);
            lock (_timers) _timers.Add(timer);
            return timer;
        }
        internal void Advance(TimeSpan value)
        {
            _now += value;
            OpeningTimer[] due;
            lock (_timers) due = _timers.Where(timer => !timer.Disposed && timer.Due <= _now).ToArray();
            foreach (OpeningTimer timer in due) timer.Fire();
        }
        private sealed class OpeningTimer(OpeningTimeProvider owner, TimerCallback callback, object? state, DateTimeOffset due) : ITimer
        {
            internal DateTimeOffset Due { get; private set; } = due;
            internal bool Disposed { get; private set; }
            internal bool Fired { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) { lock (owner._timers) { Due = owner._now + dueTime; return !Disposed; } }
            internal void Fire() { Fired = true; callback(state); }
            public void Dispose() { lock (owner._timers) Disposed = true; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
