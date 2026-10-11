using System.Security.Cryptography;
using System.Text.Json;
using Umpk.Client.Bedrock;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.Session;
using Umpk.Protocol.Bedrock;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Bedrock.IntegrationTests;

[Collection("Bedrock inventory capture")]
public sealed class InventoryCaptureLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkInventoryBot";
    private const short DiamondNetworkId = 306;
    private const ushort DiamondCount = 5;
    private const ushort ReplaceItemDiamondCount = 1;
    private const string ReplaceItemCommand = "replaceitem entity UmpkInventoryBot slot.inventory 0 diamond 1";
    private const string ReplaceItemSuccessText = "Replaced slot.inventory slot 0 of UmpkInventoryBot with 1 * Diamond";

    [BedrockLiveFact]
    public async Task OfflineInventoryCaptureReachesReadyAndRecordsContent()
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
            Assert.True(session.Snapshot.LevelChunksObserved > 0);

            bool contentObserved = await WaitForConditionAsync(() => session.CapturedInventoryContents.Count > 0, TimeSpan.FromSeconds(30), ct);
            output.WriteLine(contentObserved
                ? $"Session captured {session.CapturedInventoryContents.Count} inventory content packet(s)."
                : "Session captured no inventory content packet.");
            Assert.True(contentObserved);

            IReadOnlyList<BedrockInventoryContentPacket> contents = session.CapturedInventoryContents;
            foreach (BedrockInventoryContentPacket packet in contents)
            {
                byte[] encoded = BedrockInventoryCodecs.Encode(packet);
                BedrockInventoryContentPacket roundTrip = BedrockInventoryCodecs.DecodeContent(encoded);
                Assert.Equal(packet, roundTrip);
                Assert.Equal(packet.WindowId, packet.Container.WindowId);
            }

            output.WriteLine($"Content addresses: {string.Join(" | ", contents.Select(static packet => $"window {packet.WindowId} container {packet.Container.ContainerId} dynamic {(packet.Container.DynamicContainerId.HasValue ? packet.Container.DynamicContainerId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")} items {packet.Items.Length}"))}.");

            IReadOnlyList<byte[]> rawContents = session.CapturedInventoryContentPayloads;
            foreach (byte[] payload in rawContents)
            {
                BedrockInventoryContentPacket decoded = BedrockInventoryCodecs.DecodeContent(payload);
                Assert.Equal(payload, BedrockInventoryCodecs.Encode(decoded));
            }

            output.WriteLine($"Post-join raw content payloads: {rawContents.Count}.");

            bool hotbarObserved = await WaitForConditionAsync(() => session.HotbarObservationCount > 0, TimeSpan.FromSeconds(30), ct);
            output.WriteLine(hotbarObserved
                ? $"Session observed {session.HotbarObservationCount} player hotbar packet(s)."
                : "Session observed no player hotbar packet.");
            Assert.True(hotbarObserved, "The session observed no protocol 2169 packet 48 on the exact join flow.");
            Assert.Contains(48, session.PostJoinPacketIds.Concat(session.WireTranscript.Where(static entry => entry.Direction == BedrockWireDirection.Inbound).Select(static entry => entry.PacketId)));

            IReadOnlyList<BedrockPlayerHotbarPacket> hotbars = session.CapturedHotbars;
            Assert.NotEmpty(hotbars);
            foreach (BedrockPlayerHotbarPacket packet in hotbars)
            {
                byte[] encoded = BedrockInventoryCodecs.Encode(packet);
                BedrockPlayerHotbarPacket roundTrip = BedrockInventoryCodecs.DecodePlayerHotbar(encoded);
                Assert.Equal(packet, roundTrip);
                output.WriteLine($"Hotbar packet: selected {packet.SelectedSlot} window {packet.WindowId} select {packet.SelectSlot}.");
            }

            IReadOnlyList<byte[]> rawHotbars = session.CapturedHotbarPayloads;
            Assert.NotEmpty(rawHotbars);
            foreach (byte[] payload in rawHotbars)
            {
                BedrockPlayerHotbarPacket decoded = BedrockInventoryCodecs.DecodePlayerHotbar(payload);
                Assert.Equal(payload, BedrockInventoryCodecs.Encode(decoded));
            }

            output.WriteLine($"Raw hotbar payloads: {rawHotbars.Count}, all byte-identical round trips.");
            Assert.Equal(hotbars[0], BedrockInventoryCodecs.DecodePlayerHotbar(rawHotbars[0]));

            int slotsBefore = session.CapturedInventorySlots.Count;
            int outputBefore = harness.Output.Count;
            session.ResetInventoryCapture();
            Assert.Empty(session.CapturedInventoryContents);
            Assert.Empty(session.CapturedInventorySlots);

            await harness.SendCommandAsync($"give {PlayerName} diamond 5", ct);
            bool giveSucceeded = await WaitForGiveSuccessAsync(harness, outputBefore, PlayerName, TimeSpan.FromSeconds(20), ct);
            output.WriteLine(giveSucceeded
                ? $"BDS confirmed the give command for {PlayerName}."
                : $"BDS did not confirm the give command. Tail: {string.Join(" | ", harness.Output.Skip(outputBefore).TakeLast(5))}.");
            Assert.True(giveSucceeded, $"The BDS did not confirm 'give {PlayerName} diamond 5'. Tail: {string.Join(" | ", harness.Output.Skip(outputBefore).TakeLast(5))}.");

            bool diamondObserved = await WaitForConditionAsync(() => TryFindDiamondContent(session.CapturedInventoryContents) is not null, TimeSpan.FromSeconds(30), ct);
            output.WriteLine(diamondObserved
                ? $"Provocation drew packet 49 content with the granted diamond stack (contents {session.CapturedInventoryContents.Count}, slots {session.CapturedInventorySlots.Count})."
                : $"Provocation drew no packet 49 diamond content (contents {session.CapturedInventoryContents.Count}, slots {session.CapturedInventorySlots.Count}).");
            Assert.True(diamondObserved, "The session observed no protocol 2169 packet 49 content carrying the granted diamond stack after a confirmed give.");
            Assert.Contains(49, session.PostJoinPacketIds);

            BedrockInventoryContentPacket diamondContent = TryFindDiamondContent(session.CapturedInventoryContents)!;
            output.WriteLine($"Diamond content: window {diamondContent.WindowId} container {diamondContent.Container.ContainerId} items {diamondContent.Items.Length}.");
            Assert.Equal(diamondContent.WindowId, diamondContent.Container.WindowId);

            bool diamondPayloadRoundTrips = false;
            foreach (byte[] payload in session.CapturedInventoryContentPayloads)
            {
                BedrockInventoryContentPacket decoded = BedrockInventoryCodecs.DecodeContent(payload);
                Assert.Equal(payload, BedrockInventoryCodecs.Encode(decoded));
                if (decoded.Items.Any(static item => item.NetworkId == DiamondNetworkId && item.Count == DiamondCount))
                    diamondPayloadRoundTrips = true;
            }

            Assert.True(diamondPayloadRoundTrips, "No raw protocol 2169 packet 49 payload round-trips byte-identically with the granted diamond stack.");

            Umpk.Client.Bedrock.State.Inventory.BedrockInventorySnapshot? stored = session.InventoryStore.GetSnapshot(diamondContent.Container);
            Assert.NotNull(stored);
            Assert.Contains(stored.Value.Items, static item => item.NetworkId == DiamondNetworkId && item.Count == DiamondCount);

            foreach (BedrockInventorySlotPacket packet in session.CapturedInventorySlots)
            {
                byte[] encoded = BedrockInventoryCodecs.Encode(packet);
                BedrockInventorySlotPacket roundTrip = BedrockInventoryCodecs.DecodeSlot(encoded);
                Assert.Equal(packet, roundTrip);
                if (packet.Container.HasValue)
                    Assert.Equal(packet.WindowId, packet.Container.Value.WindowId);
                output.WriteLine($"Slot packet: window {packet.WindowId} slot {packet.Slot} container {(packet.Container.HasValue ? packet.Container.Value.ContainerId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "absent")} storage {(packet.StorageItem is null ? "absent" : "present")}.");
            }

            foreach (byte[] payload in session.CapturedInventorySlotPayloads)
            {
                BedrockInventorySlotPacket decoded = BedrockInventoryCodecs.DecodeSlot(payload);
                Assert.Equal(payload, BedrockInventoryCodecs.Encode(decoded));
            }

            output.WriteLine(session.CapturedInventorySlots.Count == 0
                ? "No packet 50 arrived on this leg, so slot evidence stays absent."
                : $"Packet 50 arrived {session.CapturedInventorySlots.Count} time(s) with byte-identical round trips.");
            output.WriteLine($"Slots observed before provocation review: {slotsBefore}.");

            bool transactionObserved = await WaitForConditionAsync(() => session.InventoryTransactionObservationCount > 0, TimeSpan.FromSeconds(30), ct);
            output.WriteLine(transactionObserved
                ? $"Session observed {session.InventoryTransactionObservationCount} inventory transaction packet(s)."
                : "Session observed no inventory transaction packet.");
            Assert.True(transactionObserved, "The session observed no protocol 2169 packet 30 on the exact join flow.");
            Assert.Contains(30, session.PostJoinPacketIds);
            Assert.NotNull(session.LastInventoryTransaction);

            IReadOnlyList<BedrockInventoryTransactionPacket> transactions = session.CapturedInventoryTransactions;
            Assert.NotEmpty(transactions);
            foreach (BedrockInventoryTransactionPacket packet in transactions)
            {
                byte[] encoded = BedrockInventoryTransactionCodecs.Encode(packet);
                BedrockInventoryTransactionPacket roundTrip = BedrockInventoryTransactionCodecs.Decode(encoded);
                Assert.Equal(packet, roundTrip);
                output.WriteLine($"Transaction packet: type {(packet.TransactionType.HasValue ? ((int)packet.TransactionType.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) : "absent")} actions {packet.Actions.Length} legacy {packet.Legacy.RequestId}.");
            }

            IReadOnlyList<byte[]> rawTransactions = session.CapturedInventoryTransactionPayloads;
            Assert.NotEmpty(rawTransactions);
            foreach (byte[] payload in rawTransactions)
            {
                BedrockInventoryTransactionPacket decoded = BedrockInventoryTransactionCodecs.Decode(payload);
                Assert.Equal(payload, BedrockInventoryTransactionCodecs.Encode(decoded));
            }

            output.WriteLine($"Raw transaction payloads: {rawTransactions.Count}, all byte-identical round trips.");
            Assert.Equal(transactions[0], BedrockInventoryTransactionCodecs.Decode(rawTransactions[0]));

            await WriteCaptureAsync(harness, session, contents, ct).ConfigureAwait(false);
            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);
            Assert.Null(session.Snapshot.Termination);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", previousCapture);
        }
    }

    [BedrockLiveFact]
    public async Task OfflineReplaceItemProvocationClassifiesPacket50Witness()
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

            int preContents = session.CapturedInventoryContents.Count;
            int preSlots = session.CapturedInventorySlots.Count;
            int preRawSlots = session.CapturedInventorySlotPayloads.Count;
            output.WriteLine($"Pre-command capture: contents {preContents}, slots {preSlots}, raw slots {preRawSlots}.");
            session.ResetInventoryCapture();
            Assert.Empty(session.CapturedInventoryContents);
            Assert.Empty(session.CapturedInventorySlots);

            int outputBefore = harness.Output.Count;
            await harness.SendCommandAsync(ReplaceItemCommand, ct);
            bool replaceSucceeded = await WaitForReplaceItemSuccessAsync(harness, outputBefore, TimeSpan.FromSeconds(20), ct);
            string[] freshOutput = harness.Output.Skip(outputBefore).ToArray();
            string tail = string.Join(" | ", freshOutput.TakeLast(5));
            output.WriteLine(replaceSucceeded
                ? $"BDS confirmed the replaceitem command for {PlayerName}."
                : $"BDS did not confirm the replaceitem command. Tail: {tail}.");
            Assert.True(replaceSucceeded, $"The BDS did not confirm '{ReplaceItemCommand}'. Tail: {tail}.");
            bool diamondObserved = await WaitForConditionAsync(() => TryFindStackContent(session.CapturedInventoryContents, DiamondNetworkId, ReplaceItemDiamondCount) is not null || TryFindStackSlot(session.CapturedInventorySlots, DiamondNetworkId, ReplaceItemDiamondCount) is not null, TimeSpan.FromSeconds(30), ct);
            bool commandFailed = HasCommandFailure(freshOutput);
            bool successHinted = freshOutput.Any(static line => line.Contains("Replac", StringComparison.OrdinalIgnoreCase));
            output.WriteLine(diamondObserved
                ? $"Replaceitem drew an authoritative diamond update (contents {session.CapturedInventoryContents.Count}, slots {session.CapturedInventorySlots.Count}). Tail: {tail}."
                : $"Replaceitem drew no authoritative diamond update (contents {session.CapturedInventoryContents.Count}, slots {session.CapturedInventorySlots.Count}). Tail: {tail}.");
            Assert.False(commandFailed, $"The BDS rejected '{ReplaceItemCommand}'. Tail: {tail}.");
            Assert.True(diamondObserved, $"The session observed no authoritative diamond update after '{ReplaceItemCommand}'. Tail: {tail}.");

            bool via49 = TryFindStackContent(session.CapturedInventoryContents, DiamondNetworkId, ReplaceItemDiamondCount) is not null;
            bool via50 = TryFindStackSlot(session.CapturedInventorySlots, DiamondNetworkId, ReplaceItemDiamondCount) is not null;
            output.WriteLine($"Update channel: packet49={via49}, packet50={via50}, successHint={successHinted}.");
            Assert.True(via49, $"The session observed no protocol 2169 packet 49 content carrying the replaced diamond after '{ReplaceItemCommand}'. Tail: {tail}.");
            Assert.Contains(49, session.PostJoinPacketIds);

            BedrockInventoryContentPacket diamondContent = TryFindStackContent(session.CapturedInventoryContents, DiamondNetworkId, ReplaceItemDiamondCount)!;
            output.WriteLine($"Diamond content: window {diamondContent.WindowId} container {diamondContent.Container.ContainerId} items {diamondContent.Items.Length}.");
            Assert.Equal(diamondContent.WindowId, diamondContent.Container.WindowId);

            bool diamondContentRoundTrips = false;
            foreach (byte[] payload in session.CapturedInventoryContentPayloads)
            {
                BedrockInventoryContentPacket decoded = BedrockInventoryCodecs.DecodeContent(payload);
                Assert.Equal(payload, BedrockInventoryCodecs.Encode(decoded));
                if (decoded.Items.Any(static item => item.NetworkId == DiamondNetworkId && item.Count == ReplaceItemDiamondCount))
                    diamondContentRoundTrips = true;
            }

            Assert.True(diamondContentRoundTrips, "No raw protocol 2169 packet 49 payload round-trips byte-identically with the replaced diamond.");

            Umpk.Client.Bedrock.State.Inventory.BedrockInventorySnapshot? stored = session.InventoryStore.GetSnapshot(diamondContent.Container);
            Assert.NotNull(stored);
            Assert.Contains(stored.Value.Items, static item => item.NetworkId == DiamondNetworkId && item.Count == ReplaceItemDiamondCount);

            IReadOnlyList<byte[]> rawSlots = session.CapturedInventorySlotPayloads;
            if (via50)
            {
                bool diamondSlotRoundTrips = false;
                foreach (byte[] payload in rawSlots)
                {
                    BedrockInventorySlotPacket decoded = BedrockInventoryCodecs.DecodeSlot(payload);
                    Assert.Equal(payload, BedrockInventoryCodecs.Encode(decoded));
                    if (decoded.Container.HasValue)
                        Assert.Equal(decoded.WindowId, decoded.Container.Value.WindowId);
                    if (decoded.Item.NetworkId == DiamondNetworkId && decoded.Item.Count == ReplaceItemDiamondCount)
                    {
                        diamondSlotRoundTrips = true;
                        output.WriteLine($"Diamond slot: window {decoded.WindowId} slot {decoded.Slot} container {(decoded.Container.HasValue ? decoded.Container.Value.ContainerId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "absent")}.");
                    }
                }

                Assert.True(diamondSlotRoundTrips, "No raw protocol 2169 packet 50 payload round-trips byte-identically with the replaced diamond.");
            }
            else
            {
                foreach (byte[] payload in rawSlots)
                {
                    BedrockInventorySlotPacket decoded = BedrockInventoryCodecs.DecodeSlot(payload);
                    Assert.Equal(payload, BedrockInventoryCodecs.Encode(decoded));
                }

                output.WriteLine("Only packet 49 carried the replaceitem update, so this leg is a bounded negative for packet 50.");
            }

            await WritePacket50WitnessAsync(harness, session, preContents, preSlots, via49, via50, successHinted, tail, ct).ConfigureAwait(false);
            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", previousCapture);
        }
    }

    private async Task WritePacket50WitnessAsync(BedrockServerHarness harness, BedrockClientSession session, int preContents, int preSlots, bool via49, bool via50, bool successHinted, string tail, CancellationToken ct)
    {
        string witnessRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-packet50-witness", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(witnessRoot);
        IReadOnlyList<byte[]> rawSlots = session.CapturedInventorySlotPayloads;
        for (int index = 0; index < rawSlots.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(witnessRoot, $"inventoryslot-raw-{index}.bin"), rawSlots[index], ct).ConfigureAwait(false);
        var metadata = new
        {
            bds = new
            {
                packageVersion = "1.26.45.1-1",
                sourceArtifactSha256 = BedrockServerHarness.ExpectedSourceSha256,
                executableSha256 = harness.ExecutableSha256,
                expectedExecutableSha256 = BedrockServerHarness.ExpectedExecutableSha256,
                configSha256 = harness.ConfigSha256,
                ipv4Port = harness.Port,
                processId = harness.ProcessId,
            },
            protocol = 2169,
            player = PlayerName,
            command = ReplaceItemCommand,
            preCommandContents = preContents,
            preCommandSlots = preSlots,
            postCommandContents = session.CapturedInventoryContents.Count,
            postCommandSlots = session.CapturedInventorySlots.Count,
            rawSlotPayloads = rawSlots.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            viaPacket49 = via49,
            viaPacket50 = via50,
            successHinted,
            classification = via50 ? "packet50-witness" : "bounded-negative-for-packet50",
            outputTail = tail,
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(witnessRoot, "packet50-witness.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        output.WriteLine($"Packet 50 witness metadata written to {witnessRoot} (classification {metadata.classification}).");
    }

    private async Task WriteCaptureAsync(BedrockServerHarness harness, BedrockClientSession session, IReadOnlyList<BedrockInventoryContentPacket> joinContents, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-inventory-capture", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        for (int index = 0; index < joinContents.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"inventorycontent-join-{index}.bin"), BedrockInventoryCodecs.Encode(joinContents[index]), ct).ConfigureAwait(false);
        var rawContents = session.CapturedInventoryContentPayloads;
        for (int index = 0; index < rawContents.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"inventorycontent-raw-{index}.bin"), rawContents[index], ct).ConfigureAwait(false);
        var slots = session.CapturedInventorySlots;
        for (int index = 0; index < slots.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"inventoryslot-{index}.bin"), BedrockInventoryCodecs.Encode(slots[index]), ct).ConfigureAwait(false);
        var rawSlots = session.CapturedInventorySlotPayloads;
        for (int index = 0; index < rawSlots.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"inventoryslot-raw-{index}.bin"), rawSlots[index], ct).ConfigureAwait(false);
        var hotbars = session.CapturedHotbars;
        for (int index = 0; index < hotbars.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"playerhotbar-{index}.bin"), BedrockInventoryCodecs.Encode(hotbars[index]), ct).ConfigureAwait(false);
        var rawHotbars = session.CapturedHotbarPayloads;
        for (int index = 0; index < rawHotbars.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"playerhotbar-raw-{index}.bin"), rawHotbars[index], ct).ConfigureAwait(false);
        var transactions = session.CapturedInventoryTransactions;
        for (int index = 0; index < transactions.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"inventorytransaction-{index}.bin"), BedrockInventoryTransactionCodecs.Encode(transactions[index]), ct).ConfigureAwait(false);
        var rawTransactions = session.CapturedInventoryTransactionPayloads;
        for (int index = 0; index < rawTransactions.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"inventorytransaction-raw-{index}.bin"), rawTransactions[index], ct).ConfigureAwait(false);
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
            joinContents = joinContents.Select(static packet => new
            {
                windowId = packet.WindowId,
                containerId = packet.Container.ContainerId,
                dynamicContainerId = packet.Container.DynamicContainerId,
                itemCount = packet.Items.Length,
                payloadBytes = BedrockInventoryCodecs.Encode(packet).Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(BedrockInventoryCodecs.Encode(packet))).ToLowerInvariant(),
            }).ToArray(),
            rawContentPayloads = rawContents.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            slots = slots.Select(static packet => new
            {
                windowId = packet.WindowId,
                slot = packet.Slot,
                containerPresent = packet.Container.HasValue,
                containerId = packet.Container.HasValue ? packet.Container.Value.ContainerId : (byte?)null,
                dynamicContainerId = packet.Container.HasValue ? packet.Container.Value.DynamicContainerId : null,
                storagePresent = packet.StorageItem is not null,
                payloadBytes = BedrockInventoryCodecs.Encode(packet).Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(BedrockInventoryCodecs.Encode(packet))).ToLowerInvariant(),
            }).ToArray(),
            rawSlotPayloads = rawSlots.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            hotbars = hotbars.Select(static packet => new
            {
                selectedSlot = packet.SelectedSlot,
                windowId = packet.WindowId,
                selectSlot = packet.SelectSlot,
                payloadBytes = BedrockInventoryCodecs.Encode(packet).Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(BedrockInventoryCodecs.Encode(packet))).ToLowerInvariant(),
            }).ToArray(),
            rawHotbarPayloads = rawHotbars.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            hotbarObservations = session.HotbarObservationCount,
            transactionObservations = session.InventoryTransactionObservationCount,
            transactions = transactions.Select(static packet => new
            {
                transactionType = packet.TransactionType.HasValue ? (int)packet.TransactionType.Value : (int?)null,
                actionCount = packet.Actions.Length,
                legacyRequestId = packet.Legacy.RequestId,
                legacyEntries = packet.Legacy.Entries.Length,
                payloadBytes = BedrockInventoryTransactionCodecs.Encode(packet).Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(BedrockInventoryTransactionCodecs.Encode(packet))).ToLowerInvariant(),
            }).ToArray(),
            rawTransactionPayloads = rawTransactions.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            duplicateContainerCloses = session.DuplicateContainerCloses,
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            rejectedSendAttempts = session.RejectedSendAttempts,
            termination = session.Snapshot.Termination?.ToString(),
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "inventory-metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "inventory-capture.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        output.WriteLine($"Inventory capture written to {captureRoot}.");
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (condition())
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return condition();
    }

    private static BedrockInventoryContentPacket? TryFindDiamondContent(IReadOnlyList<BedrockInventoryContentPacket> contents)
    {
        foreach (BedrockInventoryContentPacket packet in contents)
        {
            foreach (BedrockItemInstance item in packet.Items)
            {
                if (item.NetworkId == DiamondNetworkId && item.Count == DiamondCount)
                    return packet;
            }
        }

        return null;
    }

    private static BedrockInventoryContentPacket? TryFindStackContent(IReadOnlyList<BedrockInventoryContentPacket> contents, short networkId, ushort count)
    {
        foreach (BedrockInventoryContentPacket packet in contents)
        {
            foreach (BedrockItemInstance item in packet.Items)
            {
                if (item.NetworkId == networkId && item.Count == count)
                    return packet;
            }
        }

        return null;
    }

    private static BedrockInventorySlotPacket? TryFindStackSlot(IReadOnlyList<BedrockInventorySlotPacket> slots, short networkId, ushort count)
    {
        foreach (BedrockInventorySlotPacket packet in slots)
        {
            if (packet.Item.NetworkId == networkId && packet.Item.Count == count)
                return packet;
        }

        return null;
    }

    private static bool HasCommandFailure(string[] freshOutput) =>
        freshOutput.Any(static line => line.Contains("Unknown command", StringComparison.OrdinalIgnoreCase) || line.Contains("Syntax error", StringComparison.OrdinalIgnoreCase) || line.Contains("No target matched", StringComparison.OrdinalIgnoreCase) || line.Contains("Failed", StringComparison.OrdinalIgnoreCase));

    private static async Task<bool> WaitForReplaceItemSuccessAsync(BedrockServerHarness harness, int outputBefore, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            string[] fresh = harness.Output.Skip(outputBefore).ToArray();
            if (fresh.Any(static line => line.Contains(ReplaceItemSuccessText, StringComparison.Ordinal)))
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return harness.Output.Skip(outputBefore).Any(static line => line.Contains(ReplaceItemSuccessText, StringComparison.Ordinal));
    }

    private static async Task<bool> WaitForGiveSuccessAsync(BedrockServerHarness harness, int outputBefore, string playerName, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            string[] fresh = harness.Output.Skip(outputBefore).ToArray();
            if (fresh.Any(line => line.Contains("Gave Diamond", StringComparison.Ordinal) && line.Contains(playerName, StringComparison.Ordinal)))
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return harness.Output.Skip(outputBefore).Any(line => line.Contains("Gave Diamond", StringComparison.Ordinal) && line.Contains(playerName, StringComparison.Ordinal));
    }
}
