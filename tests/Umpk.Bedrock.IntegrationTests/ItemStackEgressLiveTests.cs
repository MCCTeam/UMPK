using System.Security.Cryptography;
using System.Text.Json;
using Umpk.Client.Bedrock;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.Session;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Bedrock.IntegrationTests;

[Collection("Bedrock inventory capture")]
public sealed class ItemStackEgressLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkEgressBot";
    private const short DiamondNetworkId = 306;

    [BedrockLiveFact]
    public async Task OfflineItemStackSwapReachesAccepted()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", "1");
        try
        {
            var client = new BedrockClient(
                new BedrockClientSettings
                {
                    Host = "127.0.0.1",
                    Port = (ushort)harness.Port,
                    CredentialProvider = new OfflineBedrockCredentialProvider(PlayerName, Guid.NewGuid()),
                    ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
                    Timeouts = BedrockClientTimeouts.Default with
                    {
                        Dial = TimeSpan.FromSeconds(15),
                        LoginSuccess = TimeSpan.FromSeconds(30),
                        ResourceNegotiation = TimeSpan.FromSeconds(60),
                        Ready = TimeSpan.FromSeconds(120),
                    },
                });
            await using BedrockClientSession session = await client.ConnectAsync(ct);
            BedrockSessionSnapshot ready = await session.WaitUntilReadyAsync(ct);
            Assert.True(ready.IsReady);

            int outputBefore = harness.Output.Count;
            session.ResetInventoryCapture();
            await harness.SendCommandAsync($"give {PlayerName} diamond 5", ct);
            bool diamondSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Diamond", TimeSpan.FromSeconds(20), ct);
            Assert.True(diamondSucceeded, "The BDS did not confirm the diamond give command.");
            outputBefore = harness.Output.Count;
            await harness.SendCommandAsync($"give {PlayerName} stone 3", ct);
            bool stoneSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Stone", TimeSpan.FromSeconds(20), ct);
            Assert.True(stoneSucceeded, "The BDS did not confirm the stone give command.");

            (BedrockContainerAddress contentAddress, int firstSlot, int secondSlot, short firstNetworkId, short secondNetworkId, int firstStackId, int secondStackId) =
                await WaitForTwoDistinctHotbarStacksAsync(session, TimeSpan.FromSeconds(30), ct);
            output.WriteLine($"Baseline: content window {contentAddress.WindowId} container {contentAddress.ContainerId} slots {firstSlot}/{secondSlot} networks {firstNetworkId}/{secondNetworkId} stacks {firstStackId}/{secondStackId}.");

            session.ResetInventoryCapture();
            BedrockItemStackSubmitResult submit = await session.SubmitInventorySwapAsync(contentAddress, (byte)firstSlot, (byte)secondSlot, ct);
            output.WriteLine($"Submitted ItemStackRequest {submit.RequestId} swapping slots {firstSlot} and {secondSlot}.");

            BedrockItemStackRequestPacket outboundDecoded = BedrockItemStackCodecs.DecodeRequest(submit.OutboundPayload);
            Assert.Single(outboundDecoded.Requests);
            Assert.Equal(submit.RequestId, outboundDecoded.Requests[0].RequestId);
            Assert.Single(outboundDecoded.Requests[0].Actions);
            Assert.Equal(BedrockItemStackActionType.Swap, outboundDecoded.Requests[0].Actions[0].Type);
            Assert.Equal(2, outboundDecoded.Requests[0].Actions[0].LegacyTypeId);
            var swap = (BedrockSwapPayload)outboundDecoded.Requests[0].Actions[0].Payload;
            Assert.Equal((byte)firstSlot, swap.Source.Slot);
            Assert.Equal((byte)secondSlot, swap.Destination.Slot);
            Assert.Equal(firstStackId, swap.Source.StackId);
            Assert.Equal(secondStackId, swap.Destination.StackId);
            Assert.Equal(28, swap.Source.Container.ContainerId);
            Assert.Equal(28, swap.Destination.Container.ContainerId);
            Assert.Equal(submit.OutboundPayload, BedrockItemStackCodecs.Encode(outboundDecoded));
            Assert.Contains(147, session.WireTranscript.Where(static entry => entry.Direction == BedrockWireDirection.Outbound).Select(static entry => entry.PacketId));

            byte[]? inboundPayload = await WaitForResponsePayloadAsync(session, submit.RequestId, TimeSpan.FromSeconds(30), ct);
            Assert.NotNull(inboundPayload);
            BedrockItemStackResponsePacket inboundDecoded = BedrockItemStackCodecs.DecodeResponse(inboundPayload!);
            BedrockItemStackResponse? matching = null;
            foreach (BedrockItemStackResponse response in inboundDecoded.Responses)
            {
                if (response.RequestId == submit.RequestId)
                    matching = response;
            }

            Assert.NotNull(matching);
            Assert.True(matching!.IsOk, $"The BDS returned ERROR for swap request {submit.RequestId}; raw bytes retained under /tmp/umpk-bedrock-itemstack-egress.");
            Assert.Equal(inboundPayload!, BedrockItemStackCodecs.Encode(inboundDecoded));

            await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, TimeSpan.FromSeconds(30), ct);
            Assert.Equal(0, session.ItemStackTracker.PendingCount);
            Assert.False(session.ActionCoordinator.IsClaimed(submit.RequestId));
            Assert.Equal(1, session.ItemStackTracker.AcceptedCount);
            Assert.Equal(0, session.ItemStackTracker.RejectedCount);

            Assert.NotNull(session.ItemStackTracker.GetBaseline(28, null, (byte)firstSlot));
            Assert.NotNull(session.ItemStackTracker.GetBaseline(28, null, (byte)secondSlot));

            bool converged = await WaitForSwapConvergenceAsync(session, contentAddress, firstSlot, secondSlot, firstNetworkId, secondNetworkId, TimeSpan.FromSeconds(30), ct);
            output.WriteLine(converged
                ? $"Authoritative store converged with exchanged stacks at slots {firstSlot} and {secondSlot}."
                : "Authoritative store did not observe a packet 49 swap; tracker baseline remains the server result.");

            if (!converged)
            {
                int contentBefore = session.CapturedInventoryContents.Count;
                outputBefore = harness.Output.Count;
                await harness.SendCommandAsync($"give {PlayerName} diamond 1", ct);
                bool refreshSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Diamond", TimeSpan.FromSeconds(20), ct);
                Assert.True(refreshSucceeded, "The BDS did not confirm the refresh give command.");
                converged = await WaitForRefreshedSwapAsync(session, contentAddress, contentBefore, firstSlot, secondSlot, firstNetworkId, secondNetworkId, TimeSpan.FromSeconds(30), ct);
                output.WriteLine(converged
                    ? $"Refreshed authoritative content confirms exchanged stacks at slots {firstSlot} and {secondSlot}."
                    : "Refreshed authoritative content did not confirm the swap; tracker baseline remains the server result.");
            }

            Assert.True(converged, "The authoritative store did not confirm the accepted swap.");

            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);
            Assert.Null(session.Snapshot.Termination);

            await WriteSwapMetadataAsync(harness, session, submit, firstSlot, secondSlot, firstStackId, secondStackId, ct);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", previousCapture);
        }
    }

    [BedrockLiveFact]
    public async Task OfflineItemStackPlaceReachesAccepted()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", "1");
        try
        {
            var client = new BedrockClient(
                new BedrockClientSettings
                {
                    Host = "127.0.0.1",
                    Port = (ushort)harness.Port,
                    CredentialProvider = new OfflineBedrockCredentialProvider(PlayerName, Guid.NewGuid()),
                    ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
                    Timeouts = BedrockClientTimeouts.Default with
                    {
                        Dial = TimeSpan.FromSeconds(15),
                        LoginSuccess = TimeSpan.FromSeconds(30),
                        ResourceNegotiation = TimeSpan.FromSeconds(60),
                        Ready = TimeSpan.FromSeconds(120),
                    },
                });
            await using BedrockClientSession session = await client.ConnectAsync(ct);
            BedrockSessionSnapshot ready = await session.WaitUntilReadyAsync(ct);
            Assert.True(ready.IsReady);

            int outputBefore = harness.Output.Count;
            session.ResetInventoryCapture();
            await harness.SendCommandAsync($"give {PlayerName} diamond 5", ct);
            bool giveSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Diamond", TimeSpan.FromSeconds(20), ct);
            Assert.True(giveSucceeded, "The BDS did not confirm the give command.");

            BedrockContainerAddress? contentAddress = await WaitForDiamondContentAsync(session, TimeSpan.FromSeconds(30), ct);
            Assert.NotNull(contentAddress);
            BedrockInventorySnapshot snapshot = session.InventoryStore.GetSnapshot(contentAddress.Value)!.Value;
            int sourceSlot = -1;
            int sourceStackId = 0;
            int sourceCount = 0;
            for (int index = 0; index < snapshot.Items.Length; index++)
            {
                if (snapshot.Items[index].NetworkId == DiamondNetworkId && snapshot.Items[index].Count > 0 && snapshot.Items[index].StackNetworkId.HasValue)
                {
                    sourceSlot = index;
                    sourceStackId = snapshot.Items[index].StackNetworkId!.Value;
                    sourceCount = snapshot.Items[index].Count;
                    break;
                }
            }

            Assert.True(sourceSlot >= 0, "No authoritative diamond stack with a stack identity was observed.");
            Assert.True(sourceSlot <= BedrockItemStackEgress.MaxHotbarSlot, "The authoritative diamond stack is outside the supported hotbar range.");
            int destinationSlot = -1;
            for (int index = 0; index <= BedrockItemStackEgress.MaxHotbarSlot; index++)
            {
                if (index != sourceSlot && snapshot.Items[index].NetworkId == 0)
                {
                    destinationSlot = index;
                    break;
                }
            }

            Assert.True(destinationSlot >= 0, "No authoritative empty hotbar destination slot was observed.");
            Assert.True(sourceSlot <= BedrockItemStackEgress.MaxHotbarSlot && destinationSlot <= BedrockItemStackEgress.MaxHotbarSlot);
            output.WriteLine($"Baseline: content window {contentAddress.Value.WindowId} container {contentAddress.Value.ContainerId} source {sourceSlot} stack {sourceStackId} count {sourceCount} dest {destinationSlot}.");

            session.ResetInventoryCapture();
            BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(contentAddress.Value, (byte)sourceSlot, (byte)destinationSlot, 1, ct);
            output.WriteLine($"Submitted ItemStackRequest {submit.RequestId} moving 1 diamond from {sourceSlot} to {destinationSlot}.");

            BedrockItemStackRequestPacket outboundDecoded = BedrockItemStackCodecs.DecodeRequest(submit.OutboundPayload);
            Assert.Single(outboundDecoded.Requests);
            Assert.Equal(submit.RequestId, outboundDecoded.Requests[0].RequestId);
            Assert.Single(outboundDecoded.Requests[0].Actions);
            Assert.Equal(BedrockItemStackActionType.Place, outboundDecoded.Requests[0].Actions[0].Type);
            var place = (BedrockPlacePayload)outboundDecoded.Requests[0].Actions[0].Payload;
            Assert.Equal(1, place.Count);
            Assert.Equal((byte)sourceSlot, place.Source.Slot);
            Assert.Equal((byte)destinationSlot, place.Destination.Slot);
            Assert.Equal(sourceStackId, place.Source.StackId);
            Assert.Equal(0, place.Destination.StackId);
            Assert.Equal(28, place.Source.Container.ContainerId);
            Assert.Equal(submit.OutboundPayload, BedrockItemStackCodecs.Encode(outboundDecoded));
            Assert.Contains(147, session.WireTranscript.Where(static entry => entry.Direction == BedrockWireDirection.Outbound).Select(static entry => entry.PacketId));

            byte[]? inboundPayload = await WaitForResponsePayloadAsync(session, submit.RequestId, TimeSpan.FromSeconds(30), ct);
            IReadOnlyList<byte[]> capturedResponses = session.CapturedItemStackResponsePayloads;
            for (int index = 0; index < capturedResponses.Count; index++)
                output.WriteLine($"Captured 148[{index}] bytes {Convert.ToHexString(capturedResponses[index]).ToLowerInvariant()}.");
            if (inboundPayload is null)
            {
                string timeoutRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-itemstack-egress", Path.GetFileName(harness.EvidenceDirectory));
                Directory.CreateDirectory(timeoutRoot);
                await File.WriteAllBytesAsync(Path.Combine(timeoutRoot, $"itemstack-request-{submit.RequestId}.bin"), submit.OutboundPayload, ct);
                for (int index = 0; index < capturedResponses.Count; index++)
                    await File.WriteAllBytesAsync(Path.Combine(timeoutRoot, $"itemstack-response-{index}.bin"), capturedResponses[index], ct);
                string[] interesting = harness.Output.Where(static line => line.Contains("ItemStack", StringComparison.Ordinal) || line.Contains("result", StringComparison.OrdinalIgnoreCase) || line.Contains("violation", StringComparison.OrdinalIgnoreCase)).ToArray();
                output.WriteLine($"BDS inventory lines: {(interesting.Length == 0 ? "<none>" : string.Join(" | ", interesting))}.");
                output.WriteLine($"No 148 for request {submit.RequestId} within 30s. Accepted={session.ItemStackTracker.AcceptedCount} Rejected={session.ItemStackTracker.RejectedCount} Unknown={session.ItemStackTracker.UnknownCount} Duplicate={session.ItemStackTracker.DuplicateCount} Stale={session.ItemStackTracker.StaleCount} Pending={session.ItemStackTracker.PendingCount} Termination={session.Snapshot.Termination} Ready={session.Snapshot.IsReady} PostJoin={string.Join(",", session.PostJoinPacketIds.GroupBy(static id => id).Select(static g => $"{g.Key}:{g.Count()}"))} Responses={session.CapturedItemStackResponsePayloads.Count} Violations={string.Join(" | ", session.CapturedViolations.Select(static v => $"{v.Severity}/{v.PacketId}/{v.Reason}"))} BDS tail: {string.Join(" | ", harness.Output.TakeLast(30))}.");
            }

            Assert.NotNull(inboundPayload);
            BedrockItemStackResponsePacket inboundDecoded = BedrockItemStackCodecs.DecodeResponse(inboundPayload!);
            BedrockItemStackResponse? matching = null;
            foreach (BedrockItemStackResponse response in inboundDecoded.Responses)
            {
                if (response.RequestId == submit.RequestId)
                    matching = response;
            }

            Assert.NotNull(matching);
            if (!matching!.IsOk)
            {
                string failureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-itemstack-egress", Path.GetFileName(harness.EvidenceDirectory));
                Directory.CreateDirectory(failureRoot);
                await File.WriteAllBytesAsync(Path.Combine(failureRoot, $"itemstack-request-{submit.RequestId}.bin"), submit.OutboundPayload, ct);
                await File.WriteAllBytesAsync(Path.Combine(failureRoot, $"itemstack-response-{submit.RequestId}.bin"), inboundPayload!, ct);
                output.WriteLine($"BDS returned ERROR for request {submit.RequestId}; raw bytes retained under {failureRoot}.");
            }

            Assert.True(matching.IsOk, $"The BDS returned ERROR for request {submit.RequestId}; raw bytes retained under /tmp/umpk-bedrock-itemstack-egress.");
            Assert.Equal(inboundPayload!, BedrockItemStackCodecs.Encode(inboundDecoded));

            await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, TimeSpan.FromSeconds(30), ct);
            Assert.Equal(0, session.ItemStackTracker.PendingCount);
            Assert.False(session.ActionCoordinator.IsClaimed(submit.RequestId));
            Assert.Equal(1, session.ItemStackTracker.AcceptedCount);
            Assert.Equal(0, session.ItemStackTracker.RejectedCount);

            BedrockItemStackBaselineSlot? destBaseline = session.ItemStackTracker.GetBaseline(28, null, (byte)destinationSlot);
            Assert.NotNull(destBaseline);
            output.WriteLine($"Response baseline: dest slot {destinationSlot} count {destBaseline!.Value.Count} stack {destBaseline.Value.StackId}.");

            bool converged = await WaitForStoreConvergenceAsync(session, contentAddress.Value, destinationSlot, TimeSpan.FromSeconds(30), ct);
            output.WriteLine(converged
                ? $"Authoritative store converged with diamond at destination {destinationSlot}."
                : "Authoritative store did not observe a packet 49 move; tracker baseline remains the server result.");

            if (!converged)
            {
                int contentBefore = session.CapturedInventoryContents.Count;
                await harness.SendCommandAsync($"give {PlayerName} diamond 1", ct);
                converged = await WaitForRefreshedStoreAsync(session, contentAddress.Value, contentBefore, destinationSlot, TimeSpan.FromSeconds(30), ct);
                output.WriteLine(converged
                    ? $"Refreshed authoritative content confirms diamond at destination {destinationSlot}."
                    : "Refreshed authoritative content did not arrive; tracker baseline remains the server result.");
            }

            Assert.True(converged, "The authoritative store did not confirm the accepted move.");

            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);
            Assert.Null(session.Snapshot.Termination);

            await WriteEgressMetadataAsync(harness, session, submit, sourceSlot, destinationSlot, sourceStackId, ct);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", previousCapture);
        }
    }

    [BedrockLiveFact]
    public async Task OfflineItemStackPlaceMergeReachesAccepted()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", "1");
        try
        {
            var client = new BedrockClient(
                new BedrockClientSettings
                {
                    Host = "127.0.0.1",
                    Port = (ushort)harness.Port,
                    CredentialProvider = new OfflineBedrockCredentialProvider(PlayerName, Guid.NewGuid()),
                    ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
                    Timeouts = BedrockClientTimeouts.Default with
                    {
                        Dial = TimeSpan.FromSeconds(15),
                        LoginSuccess = TimeSpan.FromSeconds(30),
                        ResourceNegotiation = TimeSpan.FromSeconds(60),
                        Ready = TimeSpan.FromSeconds(120),
                    },
                });
            await using BedrockClientSession session = await client.ConnectAsync(ct);
            BedrockSessionSnapshot ready = await session.WaitUntilReadyAsync(ct);
            Assert.True(ready.IsReady);

            int outputBefore = harness.Output.Count;
            session.ResetInventoryCapture();
            await harness.SendCommandAsync($"give {PlayerName} diamond 5", ct);
            bool giveSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Diamond", TimeSpan.FromSeconds(20), ct);
            Assert.True(giveSucceeded, "The BDS did not confirm the give command.");

            BedrockContainerAddress? contentAddress = await WaitForDiamondContentAsync(session, TimeSpan.FromSeconds(30), ct);
            Assert.NotNull(contentAddress);
            (int sourceSlot, int splitSlot) = FindSplitSlots(session, contentAddress.Value);
            output.WriteLine($"Baseline: content window {contentAddress.Value.WindowId} container {contentAddress.Value.ContainerId} source {sourceSlot} split {splitSlot}.");

            session.ResetInventoryCapture();
            BedrockItemStackSubmitResult split = await session.SubmitInventoryPlaceAsync(contentAddress.Value, (byte)sourceSlot, (byte)splitSlot, 1, ct);
            output.WriteLine($"Submitted split ItemStackRequest {split.RequestId} moving 1 diamond from {sourceSlot} to {splitSlot}.");
            await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, TimeSpan.FromSeconds(30), ct);

            int contentBefore = session.CapturedInventoryContents.Count;
            outputBefore = harness.Output.Count;
            await harness.SendCommandAsync($"give {PlayerName} stone 1", ct);
            bool stoneSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Stone", TimeSpan.FromSeconds(20), ct);
            Assert.True(stoneSucceeded, "The BDS did not confirm the refresh give command.");
            (int mergeSource, int mergeDest, int mergeSourceStack, int mergeDestStack) =
                await WaitForTwoDiamondStacksAsync(session, contentAddress.Value, contentBefore, TimeSpan.FromSeconds(30), ct);
            output.WriteLine($"Merge baseline: source {mergeSource} stack {mergeSourceStack} dest {mergeDest} stack {mergeDestStack}.");

            session.ResetInventoryCapture();
            BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(contentAddress.Value, (byte)mergeSource, (byte)mergeDest, 1, ct);
            output.WriteLine($"Submitted merge ItemStackRequest {submit.RequestId} moving 1 diamond from {mergeSource} onto {mergeDest}.");
            int mergeContentBefore = session.CapturedInventoryContents.Count;

            BedrockItemStackRequestPacket outboundDecoded = BedrockItemStackCodecs.DecodeRequest(submit.OutboundPayload);
            Assert.Single(outboundDecoded.Requests);
            Assert.Equal(submit.RequestId, outboundDecoded.Requests[0].RequestId);
            Assert.Single(outboundDecoded.Requests[0].Actions);
            Assert.Equal(BedrockItemStackActionType.Place, outboundDecoded.Requests[0].Actions[0].Type);
            Assert.Equal(1, outboundDecoded.Requests[0].Actions[0].LegacyTypeId);
            var place = (BedrockPlacePayload)outboundDecoded.Requests[0].Actions[0].Payload;
            Assert.Equal(1, place.Count);
            Assert.Equal((byte)mergeSource, place.Source.Slot);
            Assert.Equal((byte)mergeDest, place.Destination.Slot);
            Assert.Equal(mergeSourceStack, place.Source.StackId);
            Assert.Equal(mergeDestStack, place.Destination.StackId);
            Assert.NotEqual(0, place.Destination.StackId);
            Assert.Equal(28, place.Source.Container.ContainerId);
            Assert.Equal(28, place.Destination.Container.ContainerId);
            Assert.Equal(submit.OutboundPayload, BedrockItemStackCodecs.Encode(outboundDecoded));
            Assert.Contains(147, session.WireTranscript.Where(static entry => entry.Direction == BedrockWireDirection.Outbound).Select(static entry => entry.PacketId));

            byte[]? inboundPayload = await WaitForResponsePayloadAsync(session, submit.RequestId, TimeSpan.FromSeconds(30), ct);
            Assert.NotNull(inboundPayload);
            BedrockItemStackResponsePacket inboundDecoded = BedrockItemStackCodecs.DecodeResponse(inboundPayload!);
            BedrockItemStackResponse? matching = null;
            foreach (BedrockItemStackResponse response in inboundDecoded.Responses)
            {
                if (response.RequestId == submit.RequestId)
                    matching = response;
            }

            Assert.NotNull(matching);
            Assert.True(matching!.IsOk, $"The BDS returned ERROR for merge request {submit.RequestId}; raw bytes retained under /tmp/umpk-bedrock-itemstack-merge.");
            Assert.Equal(inboundPayload!, BedrockItemStackCodecs.Encode(inboundDecoded));

            await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 2, TimeSpan.FromSeconds(30), ct);
            Assert.Equal(0, session.ItemStackTracker.PendingCount);
            Assert.False(session.ActionCoordinator.IsClaimed(submit.RequestId));
            Assert.Equal(2, session.ItemStackTracker.AcceptedCount);
            Assert.Equal(0, session.ItemStackTracker.RejectedCount);

            Assert.NotNull(session.ItemStackTracker.GetBaseline(28, null, (byte)mergeSource));
            Assert.NotNull(session.ItemStackTracker.GetBaseline(28, null, (byte)mergeDest));

            bool converged = await WaitForMergeConvergenceAsync(session, contentAddress.Value, mergeSource, mergeDest, TimeSpan.FromSeconds(30), ct);
            output.WriteLine(converged
                ? $"Authoritative store converged with the merged diamond stack at slot {mergeDest}."
                : "Authoritative store did not observe a packet 49 merge; tracker baseline remains the server result.");

            if (!converged)
            {
                contentBefore = mergeContentBefore;
                outputBefore = harness.Output.Count;
                await harness.SendCommandAsync($"give {PlayerName} stone 1", ct);
                bool refreshSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Stone", TimeSpan.FromSeconds(20), ct);
                Assert.True(refreshSucceeded, "The BDS did not confirm the refresh give command.");
                converged = await WaitForRefreshedMergeAsync(session, contentAddress.Value, contentBefore, mergeSource, mergeDest, TimeSpan.FromSeconds(30), ct);
                output.WriteLine(converged
                    ? $"Refreshed authoritative content confirms the merged diamond stack at slot {mergeDest}."
                    : "Refreshed authoritative content did not confirm the merge; tracker baseline remains the server result.");
            }

            Assert.True(converged, "The authoritative store did not confirm the accepted merge.");

            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);
            Assert.Null(session.Snapshot.Termination);

            await WriteMergeMetadataAsync(harness, session, submit, mergeSource, mergeDest, mergeSourceStack, mergeDestStack, ct);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", previousCapture);
        }
    }

    private static (int SourceSlot, int SplitSlot) FindSplitSlots(BedrockClientSession session, BedrockContainerAddress address)
    {
        BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
        Assert.NotNull(snapshot);
        int source = -1;
        int split = -1;
        for (int index = 0; index <= BedrockItemStackEgress.MaxHotbarSlot && index < snapshot.Value.Items.Length; index++)
        {
            if (snapshot.Value.Items[index].NetworkId == DiamondNetworkId && snapshot.Value.Items[index].Count == 5 && snapshot.Value.Items[index].StackNetworkId.HasValue)
                source = index;
            else if (snapshot.Value.Items[index].NetworkId == 0 && split < 0)
                split = index;
        }

        Assert.True(source >= 0, "No authoritative diamond stack with a stack identity was observed.");
        Assert.True(split >= 0, "No authoritative empty hotbar split slot was observed.");
        return (source, split);
    }

    private static async Task<(int MergeSource, int MergeDest, int MergeSourceStack, int MergeDestStack)> WaitForTwoDiamondStacksAsync(BedrockClientSession session, BedrockContainerAddress address, int contentBefore, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (session.CapturedInventoryContents.Count > contentBefore)
            {
                BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
                if (snapshot is not null)
                {
                    int first = -1;
                    int second = -1;
                    for (int index = 0; index <= BedrockItemStackEgress.MaxHotbarSlot && index < snapshot.Value.Items.Length; index++)
                    {
                        if (snapshot.Value.Items[index].NetworkId == DiamondNetworkId && snapshot.Value.Items[index].Count > 0 && snapshot.Value.Items[index].StackNetworkId.HasValue)
                        {
                            if (first < 0)
                                first = index;
                            else
                            {
                                second = index;
                                break;
                            }
                        }
                    }

                    if (first >= 0 && second >= 0)
                    {
                        int firstCount = snapshot.Value.Items[first].Count;
                        int secondCount = snapshot.Value.Items[second].Count;
                        if (firstCount == 1 || secondCount == 1)
                        {
                            int mergeSource = firstCount == 1 ? first : second;
                            int mergeDest = firstCount == 1 ? second : first;
                            return (mergeSource, mergeDest, snapshot.Value.Items[mergeSource].StackNetworkId!.Value, snapshot.Value.Items[mergeDest].StackNetworkId!.Value);
                        }
                    }
                }
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException("No refreshed authoritative content with a one-diamond split stack was observed.");
    }

    private static async Task<bool> WaitForMergeConvergenceAsync(BedrockClientSession session, BedrockContainerAddress address, int mergeSource, int mergeDest, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
            if (snapshot is not null && mergeDest < snapshot.Value.Items.Length && mergeSource < snapshot.Value.Items.Length &&
                snapshot.Value.Items[mergeSource].NetworkId == 0 &&
                snapshot.Value.Items[mergeDest].NetworkId == DiamondNetworkId && snapshot.Value.Items[mergeDest].Count == 5)
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> WaitForRefreshedMergeAsync(BedrockClientSession session, BedrockContainerAddress address, int contentBefore, int mergeSource, int mergeDest, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (session.CapturedInventoryContents.Count > contentBefore)
            {
                BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
                if (snapshot is not null && mergeDest < snapshot.Value.Items.Length && mergeSource < snapshot.Value.Items.Length &&
                    snapshot.Value.Items[mergeSource].NetworkId == 0 &&
                    snapshot.Value.Items[mergeDest].NetworkId == DiamondNetworkId && snapshot.Value.Items[mergeDest].Count == 5)
                    return true;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return false;
    }

    private async Task WriteMergeMetadataAsync(BedrockServerHarness harness, BedrockClientSession session, BedrockItemStackSubmitResult submit, int mergeSource, int mergeDest, int mergeSourceStack, int mergeDestStack, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-itemstack-merge", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"itemstack-merge-request-{submit.RequestId}.bin"), submit.OutboundPayload, ct);
        IReadOnlyList<byte[]> responses = session.CapturedItemStackResponsePayloads;
        for (int index = 0; index < responses.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"itemstack-merge-response-{index}.bin"), responses[index], ct);
        var metadata = new
        {
            bds = new
            {
                packageVersion = "1.26.45.1-1",
                sourceArtifactSha256 = BedrockServerHarness.ExpectedSourceSha256,
                executableSha256 = harness.ExecutableSha256,
                expectedExecutableSha256 = BedrockServerHarness.ExpectedExecutableSha256,
                ipv4Port = harness.Port,
                processId = harness.ProcessId,
            },
            protocol = 2169,
            player = PlayerName,
            requestId = submit.RequestId,
            action = "place-merge",
            mergeSource,
            mergeDest,
            mergeSourceStack,
            mergeDestStack,
            outboundBytes = submit.OutboundPayload.Length,
            outboundSha256 = Convert.ToHexString(SHA256.HashData(submit.OutboundPayload)).ToLowerInvariant(),
            responsePayloads = responses.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            accepted = session.ItemStackTracker.AcceptedCount,
            rejected = session.ItemStackTracker.RejectedCount,
            unknown = session.ItemStackTracker.UnknownCount,
            duplicate = session.ItemStackTracker.DuplicateCount,
            stale = session.ItemStackTracker.StaleCount,
            pending = session.ItemStackTracker.PendingCount,
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            rejectedSendAttempts = session.RejectedSendAttempts,
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "merge-metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "itemstack-merge.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        output.WriteLine($"ItemStack merge capture written to {captureRoot}.");
    }

    private static async Task<BedrockContainerAddress?> WaitForDiamondContentAsync(BedrockClientSession session, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach (BedrockInventoryContentPacket packet in session.CapturedInventoryContents)
            {
                for (int index = 0; index < packet.Items.Length; index++)
                {
                    if (packet.Items[index].NetworkId == DiamondNetworkId && packet.Items[index].Count == 5 && packet.Items[index].StackNetworkId.HasValue)
                        return packet.Container;
                }
            }

            BedrockContainerAddress candidate = new(0, 0, null);
            BedrockInventorySnapshot? stored = session.InventoryStore.GetSnapshot(candidate);
            if (stored is not null)
            {
                foreach (BedrockItemInstance item in stored.Value.Items)
                {
                    if (item.NetworkId == DiamondNetworkId && item.Count == 5 && item.StackNetworkId.HasValue)
                        return candidate;
                }
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<byte[]?> WaitForResponsePayloadAsync(BedrockClientSession session, int requestId, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach (byte[] payload in session.CapturedItemStackResponsePayloads)
            {
                BedrockItemStackResponsePacket decoded;
                try
                {
                    decoded = BedrockItemStackCodecs.DecodeResponse(payload);
                }
                catch (BedrockCodecException)
                {
                    continue;
                }

                foreach (BedrockItemStackResponse response in decoded.Responses)
                {
                    if (response.RequestId == requestId)
                        return payload;
                }
            }

            if (session.ItemStackTracker.AcceptedCount >= 1 || session.ItemStackTracker.RejectedCount >= 1)
            {
                foreach (byte[] payload in session.CapturedItemStackResponsePayloads)
                {
                    try
                    {
                        BedrockItemStackResponsePacket decoded = BedrockItemStackCodecs.DecodeResponse(payload);
                        foreach (BedrockItemStackResponse response in decoded.Responses)
                        {
                            if (response.RequestId == requestId)
                                return payload;
                        }
                    }
                    catch (BedrockCodecException)
                    {
                    }
                }
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<bool> WaitForStoreConvergenceAsync(BedrockClientSession session, BedrockContainerAddress address, int destinationSlot, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
            if (snapshot is not null && destinationSlot < snapshot.Value.Items.Length && snapshot.Value.Items[destinationSlot].NetworkId == DiamondNetworkId)
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> WaitForRefreshedStoreAsync(BedrockClientSession session, BedrockContainerAddress address, int contentBefore, int destinationSlot, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (session.CapturedInventoryContents.Count > contentBefore)
            {
                BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
                if (snapshot is not null && destinationSlot < snapshot.Value.Items.Length && snapshot.Value.Items[destinationSlot].NetworkId == DiamondNetworkId)
                    return true;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (condition())
                return;
            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        if (!condition())
            throw new TimeoutException("The awaited inventory condition did not arrive.");
    }

    private static async Task<bool> WaitForGiveSuccessAsync(BedrockServerHarness harness, int outputBefore, string playerName, string itemWord, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            string[] fresh = harness.Output.Skip(outputBefore).ToArray();
            if (fresh.Any(line => line.Contains("Gave " + itemWord, StringComparison.Ordinal) && line.Contains(playerName, StringComparison.Ordinal)))
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return harness.Output.Skip(outputBefore).Any(line => line.Contains("Gave " + itemWord, StringComparison.Ordinal) && line.Contains(playerName, StringComparison.Ordinal));
    }

    private static async Task<(BedrockContainerAddress Address, int FirstSlot, int SecondSlot, short FirstNetworkId, short SecondNetworkId, int FirstStackId, int SecondStackId)> WaitForTwoDistinctHotbarStacksAsync(BedrockClientSession session, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach (BedrockInventoryContentPacket packet in session.CapturedInventoryContents)
            {
                var found = FindTwoDistinctHotbarStacks(packet.Items);
                if (found is not null)
                    return (packet.Container, found.Value.FirstSlot, found.Value.SecondSlot, found.Value.FirstNetworkId, found.Value.SecondNetworkId, found.Value.FirstStackId, found.Value.SecondStackId);
            }

            var candidate = new BedrockContainerAddress(0, 0, null);
            BedrockInventorySnapshot? stored = session.InventoryStore.GetSnapshot(candidate);
            if (stored is not null)
            {
                var found = FindTwoDistinctHotbarStacks(stored.Value.Items);
                if (found is not null)
                    return (candidate, found.Value.FirstSlot, found.Value.SecondSlot, found.Value.FirstNetworkId, found.Value.SecondNetworkId, found.Value.FirstStackId, found.Value.SecondStackId);
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException("No two distinct occupied hotbar stacks with stack identities were observed.");
    }

    private static (int FirstSlot, int SecondSlot, short FirstNetworkId, short SecondNetworkId, int FirstStackId, int SecondStackId)? FindTwoDistinctHotbarStacks(IReadOnlyList<BedrockItemInstance> items)
    {
        int first = -1;
        for (int index = 0; index <= BedrockItemStackEgress.MaxHotbarSlot && index < items.Count; index++)
        {
            if (items[index].NetworkId != 0 && items[index].Count > 0 && items[index].StackNetworkId.HasValue)
            {
                if (first < 0)
                {
                    first = index;
                }
                else if (items[index].NetworkId != items[first].NetworkId)
                {
                    return (first, index, items[first].NetworkId, items[index].NetworkId, items[first].StackNetworkId!.Value, items[index].StackNetworkId!.Value);
                }
            }
        }

        return null;
    }

    private static async Task<bool> WaitForSwapConvergenceAsync(BedrockClientSession session, BedrockContainerAddress address, int firstSlot, int secondSlot, short firstNetworkId, short secondNetworkId, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
            if (snapshot is not null && secondSlot < snapshot.Value.Items.Length &&
                snapshot.Value.Items[firstSlot].NetworkId == secondNetworkId &&
                snapshot.Value.Items[secondSlot].NetworkId == firstNetworkId)
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> WaitForRefreshedSwapAsync(BedrockClientSession session, BedrockContainerAddress address, int contentBefore, int firstSlot, int secondSlot, short firstNetworkId, short secondNetworkId, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (session.CapturedInventoryContents.Count > contentBefore)
            {
                BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
                if (snapshot is not null && secondSlot < snapshot.Value.Items.Length &&
                    snapshot.Value.Items[firstSlot].NetworkId == secondNetworkId &&
                    snapshot.Value.Items[secondSlot].NetworkId == firstNetworkId)
                    return true;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return false;
    }

    private async Task WriteSwapMetadataAsync(BedrockServerHarness harness, BedrockClientSession session, BedrockItemStackSubmitResult submit, int firstSlot, int secondSlot, int firstStackId, int secondStackId, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-itemstack-swap", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"itemstack-swap-request-{submit.RequestId}.bin"), submit.OutboundPayload, ct);
        IReadOnlyList<byte[]> responses = session.CapturedItemStackResponsePayloads;
        for (int index = 0; index < responses.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"itemstack-swap-response-{index}.bin"), responses[index], ct);
        var metadata = new
        {
            bds = new
            {
                packageVersion = "1.26.45.1-1",
                sourceArtifactSha256 = BedrockServerHarness.ExpectedSourceSha256,
                executableSha256 = harness.ExecutableSha256,
                expectedExecutableSha256 = BedrockServerHarness.ExpectedExecutableSha256,
                ipv4Port = harness.Port,
                processId = harness.ProcessId,
            },
            protocol = 2169,
            player = PlayerName,
            requestId = submit.RequestId,
            action = "swap",
            firstSlot,
            secondSlot,
            firstStackId,
            secondStackId,
            outboundBytes = submit.OutboundPayload.Length,
            outboundSha256 = Convert.ToHexString(SHA256.HashData(submit.OutboundPayload)).ToLowerInvariant(),
            responsePayloads = responses.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            accepted = session.ItemStackTracker.AcceptedCount,
            rejected = session.ItemStackTracker.RejectedCount,
            unknown = session.ItemStackTracker.UnknownCount,
            duplicate = session.ItemStackTracker.DuplicateCount,
            stale = session.ItemStackTracker.StaleCount,
            pending = session.ItemStackTracker.PendingCount,
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            rejectedSendAttempts = session.RejectedSendAttempts,
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "swap-metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "itemstack-swap.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        output.WriteLine($"ItemStack swap capture written to {captureRoot}.");
    }

    [BedrockLiveFact]
    public async Task OfflineItemStackTakeReachesAccepted()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", "1");
        try
        {
            var client = new BedrockClient(
                new BedrockClientSettings
                {
                    Host = "127.0.0.1",
                    Port = (ushort)harness.Port,
                    CredentialProvider = new OfflineBedrockCredentialProvider(PlayerName, Guid.NewGuid()),
                    ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
                    Timeouts = BedrockClientTimeouts.Default with
                    {
                        Dial = TimeSpan.FromSeconds(15),
                        LoginSuccess = TimeSpan.FromSeconds(30),
                        ResourceNegotiation = TimeSpan.FromSeconds(60),
                        Ready = TimeSpan.FromSeconds(120),
                    },
                });
            await using BedrockClientSession session = await client.ConnectAsync(ct);
            BedrockSessionSnapshot ready = await session.WaitUntilReadyAsync(ct);
            Assert.True(ready.IsReady);

            int outputBefore = harness.Output.Count;
            session.ResetInventoryCapture();
            await harness.SendCommandAsync($"give {PlayerName} diamond 5", ct);
            bool giveSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Diamond", TimeSpan.FromSeconds(20), ct);
            Assert.True(giveSucceeded, "The BDS did not confirm the give command.");

            BedrockContainerAddress? contentAddress = await WaitForDiamondContentAsync(session, TimeSpan.FromSeconds(30), ct);
            Assert.NotNull(contentAddress);
            BedrockInventorySnapshot snapshot = session.InventoryStore.GetSnapshot(contentAddress.Value)!.Value;
            int sourceSlot = -1;
            int sourceStackId = 0;
            for (int index = 0; index < snapshot.Items.Length; index++)
            {
                if (snapshot.Items[index].NetworkId == DiamondNetworkId && snapshot.Items[index].Count == 5 && snapshot.Items[index].StackNetworkId.HasValue)
                {
                    sourceSlot = index;
                    sourceStackId = snapshot.Items[index].StackNetworkId!.Value;
                    break;
                }
            }

            Assert.True(sourceSlot >= 0, "No authoritative diamond stack with a stack identity was observed.");
            Assert.True(sourceSlot <= BedrockItemStackEgress.MaxHotbarSlot, "The authoritative diamond stack is outside the supported hotbar range.");
            int destinationSlot = -1;
            for (int index = 0; index <= BedrockItemStackEgress.MaxHotbarSlot; index++)
            {
                if (index != sourceSlot && snapshot.Items[index].NetworkId == 0)
                {
                    destinationSlot = index;
                    break;
                }
            }

            Assert.True(destinationSlot >= 0, "No authoritative empty hotbar destination slot was observed.");
            output.WriteLine($"Baseline: content window {contentAddress.Value.WindowId} container {contentAddress.Value.ContainerId} source {sourceSlot} stack {sourceStackId} count 5 dest {destinationSlot}.");

            session.ResetInventoryCapture();
            BedrockItemStackSubmitResult submit = await session.SubmitInventoryTakeAsync(contentAddress.Value, (byte)sourceSlot, (byte)destinationSlot, 2, ct);
            output.WriteLine($"Submitted Take ItemStackRequest {submit.RequestId} moving 2 diamonds from {sourceSlot} to {destinationSlot}.");
            int takeContentBefore = session.CapturedInventoryContents.Count;

            BedrockItemStackRequestPacket outboundDecoded = BedrockItemStackCodecs.DecodeRequest(submit.OutboundPayload);
            Assert.Single(outboundDecoded.Requests);
            Assert.Equal(submit.RequestId, outboundDecoded.Requests[0].RequestId);
            Assert.Single(outboundDecoded.Requests[0].Actions);
            Assert.Equal(BedrockItemStackActionType.Take, outboundDecoded.Requests[0].Actions[0].Type);
            Assert.Equal(0, outboundDecoded.Requests[0].Actions[0].LegacyTypeId);
            var take = (BedrockTakePayload)outboundDecoded.Requests[0].Actions[0].Payload;
            Assert.Equal(2, take.Count);
            Assert.Equal((byte)sourceSlot, take.Source.Slot);
            Assert.Equal((byte)destinationSlot, take.Destination.Slot);
            Assert.Equal(sourceStackId, take.Source.StackId);
            Assert.Equal(0, take.Destination.StackId);
            Assert.Equal(28, take.Source.Container.ContainerId);
            Assert.Equal(28, take.Destination.Container.ContainerId);
            Assert.Equal(submit.OutboundPayload, BedrockItemStackCodecs.Encode(outboundDecoded));
            Assert.Contains(147, session.WireTranscript.Where(static entry => entry.Direction == BedrockWireDirection.Outbound).Select(static entry => entry.PacketId));

            byte[]? inboundPayload = await WaitForResponsePayloadAsync(session, submit.RequestId, TimeSpan.FromSeconds(30), ct);
            Assert.NotNull(inboundPayload);
            BedrockItemStackResponsePacket inboundDecoded = BedrockItemStackCodecs.DecodeResponse(inboundPayload!);
            BedrockItemStackResponse? matching = null;
            foreach (BedrockItemStackResponse response in inboundDecoded.Responses)
            {
                if (response.RequestId == submit.RequestId)
                    matching = response;
            }

            Assert.NotNull(matching);
            Assert.True(matching!.IsOk, $"The BDS returned ERROR for take request {submit.RequestId}; raw bytes retained under /tmp/umpk-bedrock-itemstack-take.");
            Assert.Equal(inboundPayload!, BedrockItemStackCodecs.Encode(inboundDecoded));

            await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, TimeSpan.FromSeconds(30), ct);
            Assert.Equal(0, session.ItemStackTracker.PendingCount);
            Assert.False(session.ActionCoordinator.IsClaimed(submit.RequestId));
            Assert.Equal(1, session.ItemStackTracker.AcceptedCount);
            Assert.Equal(0, session.ItemStackTracker.RejectedCount);

            Assert.NotNull(session.ItemStackTracker.GetBaseline(28, null, (byte)destinationSlot));

            bool converged = await WaitForTakeConvergenceAsync(session, contentAddress.Value, sourceSlot, destinationSlot, TimeSpan.FromSeconds(30), ct);
            output.WriteLine(converged
                ? $"Authoritative store converged with source 3 and destination 2 at slots {sourceSlot} and {destinationSlot}."
                : "Authoritative store did not observe a packet 49 take; tracker baseline remains the server result.");

            if (!converged)
            {
                outputBefore = harness.Output.Count;
                await harness.SendCommandAsync($"give {PlayerName} stone 1", ct);
                bool refreshSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Stone", TimeSpan.FromSeconds(20), ct);
                Assert.True(refreshSucceeded, "The BDS did not confirm the refresh give command.");
                converged = await WaitForRefreshedTakeAsync(session, contentAddress.Value, takeContentBefore, sourceSlot, destinationSlot, TimeSpan.FromSeconds(30), ct);
                output.WriteLine(converged
                    ? $"Refreshed authoritative content confirms source 3 and destination 2 at slots {sourceSlot} and {destinationSlot}."
                    : "Refreshed authoritative content did not confirm the take; tracker baseline remains the server result.");
            }

            Assert.True(converged, "The authoritative store did not confirm the accepted take.");

            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);
            Assert.Null(session.Snapshot.Termination);

            await WriteTakeMetadataAsync(harness, session, submit, sourceSlot, destinationSlot, sourceStackId, ct);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", previousCapture);
        }
    }

    [BedrockLiveFact]
    public async Task OfflineItemStackTakeMergeReachesAccepted()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", "1");
        try
        {
            var client = new BedrockClient(
                new BedrockClientSettings
                {
                    Host = "127.0.0.1",
                    Port = (ushort)harness.Port,
                    CredentialProvider = new OfflineBedrockCredentialProvider(PlayerName, Guid.NewGuid()),
                    ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
                    Timeouts = BedrockClientTimeouts.Default with
                    {
                        Dial = TimeSpan.FromSeconds(15),
                        LoginSuccess = TimeSpan.FromSeconds(30),
                        ResourceNegotiation = TimeSpan.FromSeconds(60),
                        Ready = TimeSpan.FromSeconds(120),
                    },
                });
            await using BedrockClientSession session = await client.ConnectAsync(ct);
            BedrockSessionSnapshot ready = await session.WaitUntilReadyAsync(ct);
            Assert.True(ready.IsReady);

            int outputBefore = harness.Output.Count;
            session.ResetInventoryCapture();
            await harness.SendCommandAsync($"give {PlayerName} diamond 5", ct);
            bool giveSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Diamond", TimeSpan.FromSeconds(20), ct);
            Assert.True(giveSucceeded, "The BDS did not confirm the give command.");

            BedrockContainerAddress? contentAddress = await WaitForDiamondContentAsync(session, TimeSpan.FromSeconds(30), ct);
            Assert.NotNull(contentAddress);
            BedrockInventorySnapshot snapshot = session.InventoryStore.GetSnapshot(contentAddress.Value)!.Value;
            int sourceSlot = -1;
            for (int index = 0; index < snapshot.Items.Length; index++)
            {
                if (snapshot.Items[index].NetworkId == DiamondNetworkId && snapshot.Items[index].Count == 5 && snapshot.Items[index].StackNetworkId.HasValue)
                {
                    sourceSlot = index;
                    break;
                }
            }

            Assert.True(sourceSlot >= 0, "No authoritative diamond stack with a stack identity was observed.");
            Assert.True(sourceSlot <= BedrockItemStackEgress.MaxHotbarSlot, "The authoritative diamond stack is outside the supported hotbar range.");
            int destinationSlot = -1;
            for (int index = 0; index <= BedrockItemStackEgress.MaxHotbarSlot; index++)
            {
                if (index != sourceSlot && snapshot.Items[index].NetworkId == 0)
                {
                    destinationSlot = index;
                    break;
                }
            }

            Assert.True(destinationSlot >= 0, "No authoritative empty hotbar destination slot was observed.");
            output.WriteLine($"Baseline: content window {contentAddress.Value.WindowId} container {contentAddress.Value.ContainerId} source {sourceSlot} count 5 dest {destinationSlot}.");

            session.ResetInventoryCapture();
            BedrockItemStackSubmitResult split = await session.SubmitInventoryTakeAsync(contentAddress.Value, (byte)sourceSlot, (byte)destinationSlot, 2, ct);
            output.WriteLine($"Submitted Take ItemStackRequest {split.RequestId} moving 2 diamonds from {sourceSlot} to {destinationSlot}.");
            int splitContentBefore = session.CapturedInventoryContents.Count;

            await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 1, TimeSpan.FromSeconds(30), ct);
            Assert.Equal(0, session.ItemStackTracker.PendingCount);

            bool splitConverged = await WaitForTakeConvergenceAsync(session, contentAddress.Value, sourceSlot, destinationSlot, TimeSpan.FromSeconds(30), ct);
            if (!splitConverged)
            {
                outputBefore = harness.Output.Count;
                await harness.SendCommandAsync($"give {PlayerName} stone 1", ct);
                bool refreshSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Stone", TimeSpan.FromSeconds(20), ct);
                Assert.True(refreshSucceeded, "The BDS did not confirm the refresh give command.");
                splitConverged = await WaitForRefreshedTakeAsync(session, contentAddress.Value, splitContentBefore, sourceSlot, destinationSlot, TimeSpan.FromSeconds(30), ct);
            }

            Assert.True(splitConverged, "The authoritative store did not confirm the preparatory take split.");
            BedrockInventorySnapshot merged = session.InventoryStore.GetSnapshot(contentAddress.Value)!.Value;
            Assert.Equal(DiamondNetworkId, merged.Items[sourceSlot].NetworkId);
            Assert.Equal(3, merged.Items[sourceSlot].Count);
            Assert.Equal(DiamondNetworkId, merged.Items[destinationSlot].NetworkId);
            Assert.Equal(2, merged.Items[destinationSlot].Count);
            Assert.True(merged.Items[sourceSlot].StackNetworkId.HasValue && merged.Items[destinationSlot].StackNetworkId.HasValue);
            int destinationStackId = merged.Items[destinationSlot].StackNetworkId!.Value;
            output.WriteLine($"Split converged with source 3 and destination 2 at slots {sourceSlot} and {destinationSlot}.");

            int responsesBefore = session.CapturedItemStackResponsePayloads.Count;
            int mergeContentBefore = session.CapturedInventoryContents.Count;
            BedrockItemStackSubmitResult submit = await session.SubmitInventoryTakeAsync(contentAddress.Value, (byte)sourceSlot, (byte)destinationSlot, 1, ct);
            output.WriteLine($"Submitted Take-merge ItemStackRequest {submit.RequestId} moving 1 diamond from {sourceSlot} onto {destinationSlot}.");

            BedrockItemStackRequestPacket outboundDecoded = BedrockItemStackCodecs.DecodeRequest(submit.OutboundPayload);
            Assert.Single(outboundDecoded.Requests);
            Assert.Equal(submit.RequestId, outboundDecoded.Requests[0].RequestId);
            Assert.Single(outboundDecoded.Requests[0].Actions);
            Assert.Equal(BedrockItemStackActionType.Take, outboundDecoded.Requests[0].Actions[0].Type);
            Assert.Equal(0, outboundDecoded.Requests[0].Actions[0].LegacyTypeId);
            var take = (BedrockTakePayload)outboundDecoded.Requests[0].Actions[0].Payload;
            Assert.Equal(1, take.Count);
            Assert.Equal((byte)sourceSlot, take.Source.Slot);
            Assert.Equal((byte)destinationSlot, take.Destination.Slot);
            Assert.Equal(destinationStackId, take.Destination.StackId);
            Assert.NotEqual(0, take.Destination.StackId);
            Assert.Equal(28, take.Source.Container.ContainerId);
            Assert.Equal(28, take.Destination.Container.ContainerId);
            Assert.Equal(submit.OutboundPayload, BedrockItemStackCodecs.Encode(outboundDecoded));
            Assert.Contains(147, session.WireTranscript.Where(static entry => entry.Direction == BedrockWireDirection.Outbound).Select(static entry => entry.PacketId));

            await WaitUntilAsync(() => session.CapturedItemStackResponsePayloads.Count > responsesBefore, TimeSpan.FromSeconds(30), ct);
            byte[] inboundPayload = session.CapturedItemStackResponsePayloads[^1];
            BedrockItemStackResponsePacket inboundDecoded = BedrockItemStackCodecs.DecodeResponse(inboundPayload);
            BedrockItemStackResponse? matching = null;
            foreach (BedrockItemStackResponse response in inboundDecoded.Responses)
            {
                if (response.RequestId == submit.RequestId)
                    matching = response;
            }

            Assert.NotNull(matching);
            Assert.True(matching!.IsOk, $"The BDS returned ERROR for take-merge request {submit.RequestId}; raw bytes retained under /tmp/umpk-bedrock-itemstack-take-merge.");
            Assert.Equal(inboundPayload, BedrockItemStackCodecs.Encode(inboundDecoded));

            await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == 2, TimeSpan.FromSeconds(30), ct);
            Assert.Equal(0, session.ItemStackTracker.PendingCount);
            Assert.False(session.ActionCoordinator.IsClaimed(submit.RequestId));
            Assert.Equal(2, session.ItemStackTracker.AcceptedCount);
            Assert.Equal(0, session.ItemStackTracker.RejectedCount);

            bool converged = await WaitForTakeMergeCountsAsync(session, contentAddress.Value, sourceSlot, destinationSlot, 2, 3, TimeSpan.FromSeconds(30), ct);
            output.WriteLine(converged
                ? $"Authoritative store converged with source 2 and destination 3 at slots {sourceSlot} and {destinationSlot}."
                : "Authoritative store did not observe a packet 49 take-merge; tracker baseline remains the server result.");

            if (!converged)
            {
                outputBefore = harness.Output.Count;
                await harness.SendCommandAsync($"give {PlayerName} stone 1", ct);
                bool refreshSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, "Stone", TimeSpan.FromSeconds(20), ct);
                Assert.True(refreshSucceeded, "The BDS did not confirm the refresh give command.");
                converged = await WaitForRefreshedTakeMergeCountsAsync(session, contentAddress.Value, mergeContentBefore, sourceSlot, destinationSlot, 2, 3, TimeSpan.FromSeconds(30), ct);
                output.WriteLine(converged
                    ? $"Refreshed authoritative content confirms source 2 and destination 3 at slots {sourceSlot} and {destinationSlot}."
                    : "Refreshed authoritative content did not confirm the take-merge; tracker baseline remains the server result.");
            }

            Assert.True(converged, "The authoritative store did not confirm the accepted take-merge.");

            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);
            Assert.Null(session.Snapshot.Termination);

            await WriteTakeMergeMetadataAsync(harness, session, submit, sourceSlot, destinationSlot, destinationStackId, ct);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", previousCapture);
        }
    }

    private static async Task<bool> WaitForTakeMergeCountsAsync(BedrockClientSession session, BedrockContainerAddress address, int sourceSlot, int destinationSlot, int sourceCount, int destinationCount, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
            if (snapshot is not null && destinationSlot < snapshot.Value.Items.Length && sourceSlot < snapshot.Value.Items.Length &&
                snapshot.Value.Items[sourceSlot].NetworkId == DiamondNetworkId && snapshot.Value.Items[sourceSlot].Count == sourceCount &&
                snapshot.Value.Items[destinationSlot].NetworkId == DiamondNetworkId && snapshot.Value.Items[destinationSlot].Count == destinationCount)
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> WaitForRefreshedTakeMergeCountsAsync(BedrockClientSession session, BedrockContainerAddress address, int contentBefore, int sourceSlot, int destinationSlot, int sourceCount, int destinationCount, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (session.CapturedInventoryContents.Count > contentBefore)
            {
                BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
                if (snapshot is not null && destinationSlot < snapshot.Value.Items.Length && sourceSlot < snapshot.Value.Items.Length &&
                    snapshot.Value.Items[sourceSlot].NetworkId == DiamondNetworkId && snapshot.Value.Items[sourceSlot].Count == sourceCount &&
                    snapshot.Value.Items[destinationSlot].NetworkId == DiamondNetworkId && snapshot.Value.Items[destinationSlot].Count == destinationCount)
                    return true;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return false;
    }

    private async Task WriteTakeMergeMetadataAsync(BedrockServerHarness harness, BedrockClientSession session, BedrockItemStackSubmitResult submit, int sourceSlot, int destinationSlot, int destinationStackId, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-itemstack-take-merge", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"itemstack-take-merge-request-{submit.RequestId}.bin"), submit.OutboundPayload, ct);
        IReadOnlyList<byte[]> responses = session.CapturedItemStackResponsePayloads;
        for (int index = 0; index < responses.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"itemstack-take-merge-response-{index}.bin"), responses[index], ct);
        var metadata = new
        {
            bds = new
            {
                packageVersion = "1.26.45.1-1",
                sourceArtifactSha256 = BedrockServerHarness.ExpectedSourceSha256,
                executableSha256 = harness.ExecutableSha256,
                expectedExecutableSha256 = BedrockServerHarness.ExpectedExecutableSha256,
                ipv4Port = harness.Port,
                processId = harness.ProcessId,
            },
            protocol = 2169,
            player = PlayerName,
            requestId = submit.RequestId,
            action = "take-merge",
            sourceSlot,
            destinationSlot,
            destinationStackId,
            outboundBytes = submit.OutboundPayload.Length,
            outboundSha256 = Convert.ToHexString(SHA256.HashData(submit.OutboundPayload)).ToLowerInvariant(),
            responsePayloads = responses.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            accepted = session.ItemStackTracker.AcceptedCount,
            rejected = session.ItemStackTracker.RejectedCount,
            unknown = session.ItemStackTracker.UnknownCount,
            duplicate = session.ItemStackTracker.DuplicateCount,
            stale = session.ItemStackTracker.StaleCount,
            pending = session.ItemStackTracker.PendingCount,
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            rejectedSendAttempts = session.RejectedSendAttempts,
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "take-merge-metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "itemstack-take-merge.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        output.WriteLine($"ItemStack take-merge capture written to {captureRoot}.");
    }

    private static async Task<bool> WaitForTakeConvergenceAsync(BedrockClientSession session, BedrockContainerAddress address, int sourceSlot, int destinationSlot, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
            if (snapshot is not null && destinationSlot < snapshot.Value.Items.Length && sourceSlot < snapshot.Value.Items.Length &&
                snapshot.Value.Items[sourceSlot].NetworkId == DiamondNetworkId && snapshot.Value.Items[sourceSlot].Count == 3 &&
                snapshot.Value.Items[destinationSlot].NetworkId == DiamondNetworkId && snapshot.Value.Items[destinationSlot].Count == 2)
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> WaitForRefreshedTakeAsync(BedrockClientSession session, BedrockContainerAddress address, int contentBefore, int sourceSlot, int destinationSlot, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (session.CapturedInventoryContents.Count > contentBefore)
            {
                BedrockInventorySnapshot? snapshot = session.InventoryStore.GetSnapshot(address);
                if (snapshot is not null && destinationSlot < snapshot.Value.Items.Length && sourceSlot < snapshot.Value.Items.Length &&
                    snapshot.Value.Items[sourceSlot].NetworkId == DiamondNetworkId && snapshot.Value.Items[sourceSlot].Count == 3 &&
                    snapshot.Value.Items[destinationSlot].NetworkId == DiamondNetworkId && snapshot.Value.Items[destinationSlot].Count == 2)
                    return true;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return false;
    }

    private async Task WriteTakeMetadataAsync(BedrockServerHarness harness, BedrockClientSession session, BedrockItemStackSubmitResult submit, int sourceSlot, int destinationSlot, int sourceStackId, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-itemstack-take", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"itemstack-take-request-{submit.RequestId}.bin"), submit.OutboundPayload, ct);
        IReadOnlyList<byte[]> responses = session.CapturedItemStackResponsePayloads;
        for (int index = 0; index < responses.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"itemstack-take-response-{index}.bin"), responses[index], ct);
        var metadata = new
        {
            bds = new
            {
                packageVersion = "1.26.45.1-1",
                sourceArtifactSha256 = BedrockServerHarness.ExpectedSourceSha256,
                executableSha256 = harness.ExecutableSha256,
                expectedExecutableSha256 = BedrockServerHarness.ExpectedExecutableSha256,
                ipv4Port = harness.Port,
                processId = harness.ProcessId,
            },
            protocol = 2169,
            player = PlayerName,
            requestId = submit.RequestId,
            action = "take",
            sourceSlot,
            destinationSlot,
            sourceStackId,
            outboundBytes = submit.OutboundPayload.Length,
            outboundSha256 = Convert.ToHexString(SHA256.HashData(submit.OutboundPayload)).ToLowerInvariant(),
            responsePayloads = responses.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            accepted = session.ItemStackTracker.AcceptedCount,
            rejected = session.ItemStackTracker.RejectedCount,
            unknown = session.ItemStackTracker.UnknownCount,
            duplicate = session.ItemStackTracker.DuplicateCount,
            stale = session.ItemStackTracker.StaleCount,
            pending = session.ItemStackTracker.PendingCount,
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            rejectedSendAttempts = session.RejectedSendAttempts,
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "take-metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "itemstack-take.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        output.WriteLine($"ItemStack take capture written to {captureRoot}.");
    }

    private async Task WriteEgressMetadataAsync(BedrockServerHarness harness, BedrockClientSession session, BedrockItemStackSubmitResult submit, int sourceSlot, int destinationSlot, int sourceStackId, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-itemstack-egress", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"itemstack-request-{submit.RequestId}.bin"), submit.OutboundPayload, ct);
        IReadOnlyList<byte[]> responses = session.CapturedItemStackResponsePayloads;
        for (int index = 0; index < responses.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"itemstack-response-{index}.bin"), responses[index], ct);
        var metadata = new
        {
            bds = new
            {
                packageVersion = "1.26.45.1-1",
                sourceArtifactSha256 = BedrockServerHarness.ExpectedSourceSha256,
                executableSha256 = harness.ExecutableSha256,
                expectedExecutableSha256 = BedrockServerHarness.ExpectedExecutableSha256,
                ipv4Port = harness.Port,
                processId = harness.ProcessId,
            },
            protocol = 2169,
            player = PlayerName,
            requestId = submit.RequestId,
            sourceSlot,
            destinationSlot,
            sourceStackId,
            outboundBytes = submit.OutboundPayload.Length,
            outboundSha256 = Convert.ToHexString(SHA256.HashData(submit.OutboundPayload)).ToLowerInvariant(),
            responsePayloads = responses.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            accepted = session.ItemStackTracker.AcceptedCount,
            rejected = session.ItemStackTracker.RejectedCount,
            unknown = session.ItemStackTracker.UnknownCount,
            duplicate = session.ItemStackTracker.DuplicateCount,
            stale = session.ItemStackTracker.StaleCount,
            pending = session.ItemStackTracker.PendingCount,
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            rejectedSendAttempts = session.RejectedSendAttempts,
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "egress-metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "itemstack-egress.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        output.WriteLine($"ItemStack egress capture written to {captureRoot}.");
    }
}
