using Umpk.Client.Bedrock.Actions;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;
using Fixture = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.OwnershipSession;
using SendGate = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.SendGate;

namespace Umpk.Client.Bedrock.Session;

public sealed class BedrockNormalInventoryOpenSessionTests
{
    private static readonly BedrockContainerAddress s_inventory = new(0, 0, null);
    private static Task<(ulong TargetRuntimeId, byte[] OutboundPayload)> Open(Fixture fixture, CancellationToken ct) => fixture.Session.SubmitNormalInventoryOpenAsync(ct).AsTask();
    private static int OpenSends(Fixture fixture) => fixture.Session.WireTranscript.Count(row => row.PacketId == 33 && row.Direction == BedrockWireDirection.Outbound);
    private static void NoInventoryClaim(Fixture fixture)
    {
        Assert.Equal(0, fixture.Session.PendingItemStackReconciliationCount);
        Assert.Equal(0, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(0, fixture.Session.ActionCoordinator.ClaimedCount);
    }

    [Fact]
    public async Task NormalOpenTransfersExactCurrentSelfAndAbsentPosition()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        Assert.True(fixture.Session.Snapshot.IsReady);
        Assert.Equal(2, fixture.Session.WireTranscript.Count(row => row.PacketId == 312 && row.Direction == BedrockWireDirection.Outbound));
        var result = await Open(fixture, ct);
        Assert.Equal(789UL, result.TargetRuntimeId);
        Assert.Equal(fixture.Session.Snapshot.LocalRuntimeEntityId, result.TargetRuntimeId);
        Assert.Equal(new byte[] { 6, 0x95, 6, 0 }, result.OutboundPayload);
        byte[] payload = await ReceiveOpenPayloadAsync(fixture, ct);
        Assert.Equal(result.OutboundPayload, payload);
        var reader = new BedrockReader(payload, BedrockCodecLimits.Default);
        Assert.Equal(6, reader.ReadByte());
        Assert.Equal(789UL, reader.ReadUInt64VarInt());
        Assert.False(reader.ReadBoolean());
        Assert.True(reader.End);
        Assert.Equal(1, OpenSends(fixture));
        NoInventoryClaim(fixture);
        result.OutboundPayload[0] = 99;
        Assert.Equal(6, payload[0]);
    }

    [Fact]
    public async Task PublishedRegistrySnapshotRetainsActualBootstrapNamesAndEntities()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        var actual = new BedrockItemRegistryPacket([new("minecraft:oak_planks", 27, false, 2, new Umpk.Nbt.NbtCompound())]);
        await using Fixture fixture = await Fixture.CreateAsync(ct, [actual]);
        BedrockRegistrySnapshot published = fixture.Session.PublishedRegistries;
        Assert.Equal("minecraft:oak_planks", Assert.Single(published.Items).Value);
        Assert.Equal((short)27, Assert.Single(published.Items).Key);
        Assert.True(published.Entities.ContainsKey("minecraft:pig"));
        Assert.True(published.Generation > 0);
        Assert.Same(published, fixture.Session.PublishedRegistries);
        BedrockRegistrySnapshot replacement = published with { Items = published.Items.Clear() };
        Assert.Empty(replacement.Items);
        Assert.Equal("minecraft:oak_planks", fixture.Session.PublishedRegistries.Items[27]);
    }

    [Fact]
    public async Task OpenTransferDoesNotInventAnActualContainerEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await Open(fixture, ct);
        Assert.Null(fixture.Session.ContainerWindows.GetWindow(2));
        await ApplyBoundaryAsync(fixture, "open", ct);
        Assert.Equal(-1, fixture.Session.ContainerWindows.GetWindow(3)!.Value.WindowType);
        Assert.Equal(1, OpenSends(fixture));
    }

    [Fact]
    public async Task OpenBeforeActualReadyRejectsWithoutTransfer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct, beforeJoin: async (session, token) =>
        {
            Assert.False(session.Snapshot.IsReady);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitNormalInventoryOpenAsync(token).AsTask());
            Assert.DoesNotContain(session.WireTranscript, row => row.PacketId == 33);
        });
        Assert.True(fixture.Session.Snapshot.IsReady);
        await Open(fixture, ct);
        Assert.Equal(1, OpenSends(fixture));
    }

    [Theory]
    [InlineData("caller")]
    [InlineData("refusal")]
    [InlineData("exception")]
    public async Task FailedOpeningReleasesOwnershipForRetry(string failure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        int prior = fixture.Transport.CommittedCount;
        if (failure == "caller")
        {
            using var caller = new CancellationTokenSource();
            SendGate gate = fixture.Transport.HoldNextReservation();
            Task<(ulong TargetRuntimeId, byte[] OutboundPayload)> operation = Open(fixture, caller.Token);
            try
            {
                await gate.Entered.WaitAsync(ct);
                await caller.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(1), ct));
            }
            finally
            {
                gate.Allow();
            }
        }
        else if (failure == "refusal")
        {
            fixture.Transport.RefuseNextReservation();
            await Assert.ThrowsAsync<InvalidOperationException>(() => Open(fixture, ct));
        }
        else
        {
            fixture.Transport.ThrowNextReservation();
            await Assert.ThrowsAsync<IOException>(() => Open(fixture, ct));
        }
        Assert.Equal(prior, fixture.Transport.CommittedCount);
        Assert.Equal(0, OpenSends(fixture));
        NoInventoryClaim(fixture);
        Assert.Equal(789UL, (await Open(fixture, ct)).TargetRuntimeId);
        Assert.Equal(1, OpenSends(fixture));
        await fixture.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), ct);
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("grant")]
    [InlineData("transfer")]
    public async Task OnlyOneOpeningMethodOwnsItsWholeSendLifetime(string phase)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = phase == "capacity" ? fixture.Transport.HoldNextReservation() : phase == "grant" ? fixture.Transport.HoldNextGrantedCommit() : fixture.Transport.HoldNextTransferResult();
        var operation = Open(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Open(fixture, ct));
            Assert.False(operation.IsCompleted);
            gate.Allow();
            await operation.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(1, OpenSends(fixture));
            await Open(fixture, ct);
            Assert.Equal(2, OpenSends(fixture));
        }
        finally
        {
            gate.Allow();
            await operation.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    [Theory]
    [InlineData("open")]
    [InlineData("foreign")]
    [InlineData("close")]
    [InlineData("unknown-close")]
    [InlineData("life")]
    [InlineData("world")]
    [InlineData("terminal")]
    public async Task RealBoundaryCancelsOpeningBeforeCapacityGrant(string boundary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        if (boundary == "close") await ApplyBoundaryAsync(fixture, "open", ct);
        int prior = fixture.Transport.CommittedCount;
        SendGate gate = fixture.Transport.HoldNextReservation();
        var operation = Open(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await ApplyBoundaryAsync(fixture, boundary, ct);
            if (boundary == "terminal")
                await Assert.ThrowsAsync<BedrockSessionException>(() => operation.WaitAsync(TimeSpan.FromSeconds(1), ct));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => operation.WaitAsync(TimeSpan.FromSeconds(1), ct));
            Assert.Equal(prior, fixture.Transport.CommittedCount);
            Assert.Equal(0, OpenSends(fixture));
            NoInventoryClaim(fixture);
            if (boundary is "life" or "world" or "terminal")
                await Assert.ThrowsAsync<InvalidOperationException>(() => Open(fixture, ct));
            else
                Assert.Equal(789UL, (await Open(fixture, ct)).TargetRuntimeId);
        }
        finally
        {
            gate.Allow();
            Assert.True(operation.IsCompleted);
        }
    }

    [Theory]
    [InlineData("caller", false)]
    [InlineData("caller", true)]
    [InlineData("open", false)]
    [InlineData("open", true)]
    [InlineData("close", false)]
    [InlineData("close", true)]
    [InlineData("life", false)]
    [InlineData("life", true)]
    [InlineData("world", false)]
    [InlineData("world", true)]
    [InlineData("terminal", false)]
    [InlineData("terminal", true)]
    public async Task GrantedOpeningAccountsAndReturnsWithoutRevivingOlderScreen(string boundary, bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        if (boundary == "close") await ApplyBoundaryAsync(fixture, "open", ct);
        using var caller = new CancellationTokenSource();
        SendGate gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        var operation = Open(fixture, caller.Token);
        Task? dispose = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            if (afterTransfer) Assert.Equal(new byte[] { 6, 0x95, 6, 0 }, await ReceiveOpenPayloadAsync(fixture, ct));
            if (boundary == "caller") await caller.CancelAsync();
            else if (boundary == "terminal") dispose = fixture.Session.DisposeAsync().AsTask();
            else await ApplyBoundaryAsync(fixture, boundary, ct);
            Assert.False(operation.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Open(fixture, ct));
            gate.Allow();
            Assert.Equal(789UL, (await operation.WaitAsync(TimeSpan.FromSeconds(5), ct)).TargetRuntimeId);
            if (!afterTransfer && boundary != "terminal") Assert.Equal(new byte[] { 6, 0x95, 6, 0 }, await ReceiveOpenPayloadAsync(fixture, ct));
            if (dispose is not null) await dispose.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(1, OpenSends(fixture));
            if (boundary == "open") Assert.NotNull(fixture.Session.ContainerWindows.GetWindow(3));
            if (boundary is "close" or "life" or "world" or "terminal") Assert.Null(fixture.Session.ContainerWindows.GetWindow(3));
            NoInventoryClaim(fixture);
        }
        finally
        {
            gate.Allow();
            await operation.WaitAsync(TimeSpan.FromSeconds(5), ct);
            if (dispose is not null) await dispose.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeJoinsGrantedOpeningThroughReturnedAccounting(bool afterTransfer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = afterTransfer ? fixture.Transport.HoldNextTransferResult() : fixture.Transport.HoldNextGrantedCommit();
        var operation = Open(fixture, ct);
        Task? dispose = null;
        try
        {
            await gate.Entered.WaitAsync(ct);
            dispose = fixture.Session.DisposeAsync().AsTask();
            Assert.False(dispose.IsCompleted);
            Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
            Assert.False(fixture.Transport.DisposeEntered.Task.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Open(fixture, ct));
            gate.Allow();
            Assert.Equal(789UL, (await operation.WaitAsync(TimeSpan.FromSeconds(5), ct)).TargetRuntimeId);
            await dispose.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(1, OpenSends(fixture));
            Assert.True(fixture.Transport.AbortEntered.Task.IsCompleted);
            Assert.True(fixture.Transport.DisposeEntered.Task.IsCompleted);
        }
        finally
        {
            gate.Allow();
            await operation.WaitAsync(TimeSpan.FromSeconds(5), ct);
            if (dispose is not null) await dispose.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    [Fact]
    public async Task DisposeCancelsUnreservedOpeningWithoutACommit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextReservation();
        var operation = Open(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await fixture.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), ct);
            await Assert.ThrowsAsync<BedrockSessionException>(() => operation);
            Assert.Equal(0, OpenSends(fixture));
        }
        finally
        {
            gate.Allow();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OpeningAndInventorySendsShareTeardownDrain(bool openingFirst, bool raw)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextGrantedCommit();
        Task<(ulong TargetRuntimeId, byte[] OutboundPayload)>? opening = null;
        Task<BedrockItemStackSubmitResult>? inventory = null;
        Task? dispose = null;
        try
        {
            if (openingFirst) opening = Open(fixture, ct);
            else inventory = StartInventory(fixture, raw, ct);
            await gate.Entered.WaitAsync(ct);
            if (openingFirst) inventory = StartInventory(fixture, raw, ct);
            else opening = Open(fixture, ct);
            dispose = fixture.Session.DisposeAsync().AsTask();
            Assert.False(dispose.IsCompleted);
            Assert.False(fixture.Transport.AbortEntered.Task.IsCompleted);
            if (openingFirst) await Assert.ThrowsAsync<BedrockSessionException>(() => inventory!);
            else await Assert.ThrowsAsync<BedrockSessionException>(() => opening!);
            gate.Allow();
            if (openingFirst) Assert.Equal(789UL, (await opening!.WaitAsync(TimeSpan.FromSeconds(5), ct)).TargetRuntimeId);
            else Assert.Equal(-3, (await inventory!.WaitAsync(TimeSpan.FromSeconds(5), ct)).RequestId);
            await dispose.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(openingFirst ? 1 : 0, OpenSends(fixture));
            NoInventoryClaim(fixture);
        }
        finally
        {
            gate.Allow();
            if (opening is not null) await ObserveAsync(opening, ct);
            if (inventory is not null) await ObserveAsync(inventory, ct);
            if (dispose is not null) await dispose.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
    }

    [Fact]
    public async Task OpeningDoesNotBurnInventoryIdentityOrAwaitItsPendingResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        await Open(fixture, ct);
        Assert.Equal(-3, (await fixture.Session.SubmitInventoryPlaceAsync(s_inventory, 0, 1, 1, ct)).RequestId);
        Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(1, fixture.Session.ActionCoordinator.ClaimedCount);
        await Open(fixture, ct);
        Assert.Equal(1, fixture.Session.ItemStackTracker.PendingCount);
        Assert.Equal(1, fixture.Session.ActionCoordinator.ClaimedCount);
        await fixture.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), ct);
        NoInventoryClaim(fixture);
        Assert.Equal(2, OpenSends(fixture));
    }

    [Fact]
    public async Task InlineScreenCancellationReleasesOpeningSingletonForRetry()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextInlineCancellation();
        var operation = Open(fixture, ct);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await ApplyBoundaryAsync(fixture, "open", ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => operation.WaitAsync(TimeSpan.FromSeconds(1), ct));
            await Open(fixture, ct);
            Assert.Equal(1, OpenSends(fixture));
        }
        finally
        {
            gate.Allow();
        }
    }

    [Fact]
    public async Task InlineTerminalCancellationRejectsReentrantOpeningBeforePublishedCompletion()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeout.Token;
        await using Fixture fixture = await Fixture.CreateAsync(ct);
        SendGate gate = fixture.Transport.HoldNextInlineCancellation();
        var operation = Open(fixture, ct);
        Task<(ulong TargetRuntimeId, byte[] OutboundPayload)>? retry = null;
        bool? terminalPublishedAtRetry = null;
        Task continuation = operation.ContinueWith(_ =>
        {
            terminalPublishedAtRetry = fixture.Session.Completion.IsCompleted;
            retry = Open(fixture, ct);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            await gate.Entered.WaitAsync(ct);
            await fixture.Session.DisconnectAsync(ct);
            await continuation.WaitAsync(TimeSpan.FromSeconds(2), ct);
            await Assert.ThrowsAsync<BedrockSessionException>(() => operation);
            Assert.False(terminalPublishedAtRetry);
            Assert.NotNull(retry);
            await Assert.ThrowsAsync<InvalidOperationException>(() => retry);
            Assert.Equal(0, OpenSends(fixture));
            NoInventoryClaim(fixture);
        }
        finally
        {
            gate.Allow();
        }
    }

    private static Task<BedrockItemStackSubmitResult> StartInventory(Fixture fixture, bool raw, CancellationToken ct)
    {
        if (!raw) return fixture.Session.SubmitInventoryPlaceAsync(s_inventory, 0, 1, 1, ct).AsTask();
        int generation = fixture.Session.ItemStackTracker.Generation;
        Assert.True(fixture.Session.ActionCoordinator.TryClaim(-3, BedrockActionEmission.Standalone));
        Assert.True(fixture.Session.ItemStackTracker.TryRegister(-3, generation));
        return fixture.Session.SendItemStackRequestAsync(BedrockItemStackEgress.BuildPlace(-3, 0, 1, 1, 7, 0), -3, generation, ct).AsTask();
    }

    private static async Task ApplyBoundaryAsync(Fixture fixture, string boundary, CancellationToken ct)
    {
        if (boundary is "open" or "foreign")
        {
            await fixture.SendAsync(46, BedrockInventoryCodecs.Encode(new BedrockContainerOpenPacket(3, boundary == "open" ? (sbyte)-1 : (sbyte)0, 0, 0, 0, -1)), ct);
            await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(3) is not null && fixture.Session.Snapshot.IsReady, ct);
        }
        else if (boundary is "close" or "unknown-close")
        {
            int prior = fixture.Session.DuplicateContainerCloses;
            await fixture.SendAsync(47, BedrockInventoryCodecs.Encode(new BedrockContainerClosePacket(boundary == "close" ? (sbyte)3 : (sbyte)9, -1, true)), ct);
            if (boundary == "close") await WaitAsync(() => fixture.Session.ContainerWindows.GetWindow(3) is null && fixture.Session.Snapshot.IsReady, ct);
            else await WaitAsync(() => fixture.Session.DuplicateContainerCloses > prior, ct);
        }
        else if (boundary == "life") fixture.Session.HandleRespawn();
        else if (boundary == "world") fixture.Session.HandleWorldReplacement();
        else
        {
            await fixture.Session.DisconnectAsync(ct);
        }
    }

    private static async Task<byte[]> ReceiveOpenPayloadAsync(Fixture fixture, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            (int packetId, byte[] payload) = await fixture.ReceiveAsync(ct);
            if (packetId == 33) return payload;
        }
        throw new InvalidOperationException("The controlled transport did not deliver the normal inventory opening.");
    }

    private static async Task ObserveAsync(Task operation, CancellationToken ct)
    {
        try { await operation.WaitAsync(TimeSpan.FromSeconds(5), ct); }
        catch (BedrockSessionException) { }
    }

    private static async Task WaitAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition()) await Task.Delay(10, ct);
    }
}
