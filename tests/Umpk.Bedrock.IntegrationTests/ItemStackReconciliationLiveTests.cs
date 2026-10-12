using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
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

[Trait("Category", "BedrockLive")]
internal sealed class BedrockReconciliationLiveFactAttribute : FactAttribute
{
    public BedrockReconciliationLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("UMPK_BEDROCK_LIVE") != "1" || Environment.GetEnvironmentVariable("UMPK_BEDROCK_RECONCILIATION_LIVE") != "1")
            Skip = "Set UMPK_BEDROCK_LIVE=1 and UMPK_BEDROCK_RECONCILIATION_LIVE=1 for the isolated exact-BDS reconciliation witness.";
    }
}

[Collection("Bedrock inventory capture")]
public sealed class ItemStackReconciliationLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkReconBot";
    private const short DiamondNetworkId = 306;
    private static readonly BedrockContainerAddress PlayerAddress = new(0, 0, null);
    private readonly List<byte[]> _requests = [];
    private readonly List<object> _controls = [];
    private readonly List<object> _steps = [];
    private readonly Dictionary<string, object> _artifacts = [];
    private string? _serverStartStat;
    private string? _testHostStartStat;
    private string _evidenceDirectory = string.Empty;
    private int? _playerContentCountAtAdmission;

    [BedrockReconciliationLiveFact]
    public async Task OfflineSequentialPlaceUsesAcceptedResponseWithoutContentRefresh()
    {
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        BedrockClient? client = null;
        BedrockClientSession? session = null;
        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY");
        string result = "not-started";
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", "1");
        try
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            Assert.NotEmpty(interfaces);
            Assert.All(interfaces, network =>
            {
                Assert.Equal(NetworkInterfaceType.Loopback, network.NetworkInterfaceType);
                // Linux loopback can report Unknown even when the reviewed namespace launcher enabled it.
                Assert.True(network.OperationalStatus is OperationalStatus.Up or OperationalStatus.Unknown);
            });
            _testHostStartStat = await File.ReadAllTextAsync($"/proc/{Environment.ProcessId}/stat", ct).ConfigureAwait(false);
            BedrockServerHarness.EnsureLinuxDistribution();
            await harness.StartAsync(ct).ConfigureAwait(false);
            _evidenceDirectory = harness.EvidenceDirectory;
            _serverStartStat = await File.ReadAllTextAsync($"/proc/{harness.ProcessId}/stat", ct).ConfigureAwait(false);
            Assert.Equal(BedrockServerHarness.ExpectedExecutableSha256, await HashFileAsync(Path.Combine(harness.RunDirectory, "bedrock_server"), ct).ConfigureAwait(false));
            client = new BedrockClient(new BedrockClientSettings
            {
                Host = "127.0.0.1",
                Port = checked((ushort)harness.Port),
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
            session = await client.ConnectAsync(ct).ConfigureAwait(false);
            Assert.True((await session.WaitUntilReadyAsync(ct).ConfigureAwait(false)).IsReady);
            session.ResetInventoryCapture();
            await harness.SendCommandAsync($"give {PlayerName} diamond 5", ct).ConfigureAwait(false);
            (BedrockInventorySnapshot baseline, int source, int middle, int destination, byte[] payload) = await WaitForBaselineAsync(session, ct).ConfigureAwait(false);
            await SaveArtifactAsync("baseline-49.bin", payload, ct).ConfigureAwait(false);
            Assert.Equal(payload, BedrockInventoryCodecs.Encode(BedrockInventoryCodecs.DecodeContent(payload)));
            result = "baseline-ready";

            // Opposing controls establish the candidate quantity-zero grammar on this exact server before any147 is emitted.
            await QuerySlotAsync(harness, source, 5, true, ct).ConfigureAwait(false);
            await QuerySlotAsync(harness, source, 4, false, ct).ConfigureAwait(false);
            await QuerySlotAsync(harness, source, 0, false, ct).ConfigureAwait(false);
            await QuerySlotAsync(harness, middle, 0, true, ct).ConfigureAwait(false);
            await QuerySlotAsync(harness, middle, 1, false, ct).ConfigureAwait(false);
            await QuerySlotAsync(harness, destination, 0, true, ct).ConfigureAwait(false);
            await QuerySlotAsync(harness, destination, 1, false, ct).ConfigureAwait(false);
            int contentsBefore = PlayerContentCount(session);
            _playerContentCountAtAdmission = contentsBefore;
            result = "console-count-grammar-calibrated";

            BedrockItemStackResponse first = await SubmitAndVerifyAsync(session, baseline, source, middle, -3, 4, 1, 1, ct).ConfigureAwait(false);
            BedrockItemStackResponseSlot firstDestination = ResponseSlot(first, middle);
            await VerifyConsoleCountsAsync(harness, source, middle, destination, 4, 1, 0, ct).ConfigureAwait(false);
            Assert.Equal(contentsBefore, PlayerContentCount(session));
            result = "first-production-request-qualified-without-49";
            await SaveEvidenceAsync(harness, session, result).ConfigureAwait(false);

            BedrockInventorySnapshot afterFirst = session.InventoryStore.GetSnapshot(PlayerAddress)!.Value;
            Assert.Equal(firstDestination.StackId, afterFirst.Items[middle].StackNetworkId);
            BedrockItemStackResponse second = await SubmitAndVerifyAsync(session, afterFirst, middle, destination, -5, 0, 1, 2, ct).ConfigureAwait(false);
            var secondPlace = Assert.IsType<BedrockPlacePayload>(BedrockItemStackCodecs.DecodeRequest(_requests[1]).Requests[0].Actions[0].Payload);
            Assert.Equal(firstDestination.StackId!.Value, secondPlace.Source.StackId);
            await VerifyConsoleCountsAsync(harness, source, middle, destination, 4, 0, 1, ct).ConfigureAwait(false);
            Assert.Equal(contentsBefore, PlayerContentCount(session));
            BedrockInventorySnapshot final = session.InventoryStore.GetSnapshot(PlayerAddress)!.Value;
            Assert.True(final.IsSlotKnown(source));
            Assert.Equal(afterFirst.Items[source], final.Items[source]);
            Assert.Equal(0, ResponseSlot(second, middle).Count);
            Assert.Equal(2, session.ItemStackTracker.AcceptedCount);
            Assert.Equal(0, session.ItemStackTracker.PendingCount);
            Assert.Equal(0, session.ItemStackTracker.RejectedCount);
            Assert.Equal(0, session.ItemStackTracker.UnknownCount);
            Assert.Equal(0, session.ItemStackTracker.DuplicateCount);
            Assert.Equal(0, session.ItemStackTracker.StaleCount);
            Assert.Equal(0, session.ActionCoordinator.ClaimedCount);
            Assert.Equal(2, session.WireTranscript.Count(entry => entry.PacketId == 147 && entry.Direction == BedrockWireDirection.Outbound));
            Assert.True(session.Snapshot.IsReady);
            Assert.Null(session.Snapshot.Termination);
            Assert.Empty(session.CapturedViolations);
            result = "two-production-transfers-qualified-without-49";
        }
        catch (Exception exception)
        {
            result = $"failed:{exception.GetType().Name}:{exception.Message}";
            throw;
        }
        finally
        {
            try
            {
                await SaveEvidenceAsync(harness, session, result).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (session is not null)
                        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (client is not null)
                            await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY", previousCapture);
                    }
                }
            }
        }
    }

    private async Task<BedrockItemStackResponse> SubmitAndVerifyAsync(BedrockClientSession session, BedrockInventorySnapshot before, int source, int destination, int requestId, byte sourceCount, byte destinationCount, int accepted, CancellationToken ct)
    {
        int responseBefore = session.CapturedItemStackResponsePayloads.Count;
        BedrockItemStackSubmitResult submit = await session.SubmitInventoryPlaceAsync(PlayerAddress, checked((byte)source), checked((byte)destination), 1, ct).ConfigureAwait(false);
        _requests.Add(submit.OutboundPayload);
        await SaveArtifactAsync($"request-{requestId}-147.bin", submit.OutboundPayload, ct).ConfigureAwait(false);
        Assert.Equal(requestId, submit.RequestId);
        BedrockItemStackRequestPacket decoded = BedrockItemStackCodecs.DecodeRequest(submit.OutboundPayload);
        Assert.Equal(submit.OutboundPayload, BedrockItemStackCodecs.Encode(decoded));
        BedrockItemStackRequest request = Assert.Single(decoded.Requests);
        Assert.Equal(requestId, request.RequestId);
        Assert.Equal(0, request.Cause);
        BedrockItemStackAction action = Assert.Single(request.Actions);
        Assert.Equal(BedrockItemStackActionType.Place, action.Type);
        Assert.Equal(1, action.LegacyTypeId);
        BedrockPlacePayload place = Assert.IsType<BedrockPlacePayload>(action.Payload);
        Assert.Equal(1, place.Count);
        Assert.Equal(new BedrockStackRequestSlot(new BedrockFullContainerName(28, null), checked((byte)source), before.Items[source].StackNetworkId!.Value), place.Source);
        Assert.Equal(new BedrockStackRequestSlot(new BedrockFullContainerName(28, null), checked((byte)destination), 0), place.Destination);

        byte[]? payload = null;
        await WaitUntilAsync(() =>
        {
            payload = session.CapturedItemStackResponsePayloads.Skip(responseBefore).FirstOrDefault(value => BedrockItemStackCodecs.DecodeResponse(value).Responses.Any(response => response.RequestId == requestId));
            return payload is not null;
        }, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        await SaveArtifactAsync($"response-{requestId}-148.bin", payload!, ct).ConfigureAwait(false);
        BedrockItemStackResponsePacket responsePacket = BedrockItemStackCodecs.DecodeResponse(payload!);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(responsePacket));
        BedrockItemStackResponse response = Assert.Single(responsePacket.Responses.Where(value => value.RequestId == requestId));
        _steps.Add(new { requestId, source, destination, response.Status, response.ContainersPresent, sourceCount, destinationCount });
        Assert.Equal(0, response.Status);
        Assert.True(response.ContainersPresent);
        BedrockItemStackResponseSlot sourceRow = ResponseSlot(response, source);
        BedrockItemStackResponseSlot destinationRow = ResponseSlot(response, destination);
        Assert.Equal(sourceCount, sourceRow.Count);
        Assert.Equal(destinationCount, destinationRow.Count);
        Assert.True(destinationRow.StackId is > 0);
        await WaitUntilAsync(() => session.ItemStackTracker.AcceptedCount == accepted && session.ItemStackTracker.PendingCount == 0 && !session.ActionCoordinator.IsClaimed(requestId), TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        BedrockInventorySnapshot after = session.InventoryStore.GetSnapshot(PlayerAddress)!.Value;
        AssertStoredSlot(after, source, sourceRow, before.Items[source]);
        AssertStoredSlot(after, destination, destinationRow, before.Items[source]);
        _steps.Add(new { requestId, storeSource = DescribeSlot(after, source), storeDestination = DescribeSlot(after, destination) });
        return response;
    }

    private static void AssertStoredSlot(BedrockInventorySnapshot snapshot, int slot, BedrockItemStackResponseSlot row, BedrockItemInstance retained)
    {
        Assert.True(snapshot.IsSlotKnown(slot));
        BedrockItemInstance expected = row.Count == 0 ? new(0, 0, 0, null, 0, []) : new(retained.NetworkId, row.Count, retained.MetadataValue, row.StackId, retained.BlockRuntimeId, retained.ExtraData);
        Assert.Equal(expected, snapshot.Items[slot]);
        Assert.Equal(row, snapshot.ResponseCorrections[slot]);
    }

    private static BedrockItemStackResponseSlot ResponseSlot(BedrockItemStackResponse response, int slot) =>
        Assert.Single(Assert.Single(response.Containers.Where(container => container.SlotType == new BedrockFullContainerName(28, null))).Slots.Where(row => row.Slot == slot && row.HotbarSlot == slot));

    private static int PlayerContentCount(BedrockClientSession session) => session.CapturedInventoryContents.Count(packet => packet.Container == PlayerAddress);

    private static async Task<(BedrockInventorySnapshot Snapshot, int Source, int Middle, int Destination, byte[] Payload)> WaitForBaselineAsync(BedrockClientSession session, CancellationToken ct)
    {
        (BedrockInventorySnapshot, int, int, int, byte[])? found = null;
        await WaitUntilAsync(() =>
        {
            foreach (byte[] payload in session.CapturedInventoryContentPayloads)
            {
                BedrockInventoryContentPacket packet = BedrockInventoryCodecs.DecodeContent(payload);
                if (packet.Container != PlayerAddress)
                    continue;
                int source = Enumerable.Range(0, Math.Min(9, packet.Items.Length)).FirstOrDefault(slot => packet.Items[slot].NetworkId == DiamondNetworkId && packet.Items[slot].Count == 5 && packet.Items[slot].StackNetworkId is > 0, -1);
                int[] empty = Enumerable.Range(0, Math.Min(9, packet.Items.Length)).Where(slot => packet.Items[slot].NetworkId == 0 && packet.Items[slot].Count == 0).Take(2).ToArray();
                BedrockInventorySnapshot? stored = session.InventoryStore.GetSnapshot(PlayerAddress);
                if (source < 0 || empty.Length != 2 || stored is null || !stored.Value.IsSlotKnown(source) || !stored.Value.Items[source].Equals(packet.Items[source]))
                    continue;
                found = (stored.Value, source, empty[0], empty[1], payload);
                return true;
            }
            return false;
        }, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        return found!.Value;
    }

    private async Task VerifyConsoleCountsAsync(BedrockServerHarness harness, int source, int middle, int destination, int sourceCount, int middleCount, int destinationCount, CancellationToken ct)
    {
        foreach ((int slot, int count) in new[] { (source, sourceCount), (middle, middleCount), (destination, destinationCount) })
        {
            await QuerySlotAsync(harness, slot, count, true, ct).ConfigureAwait(false);
            await QuerySlotAsync(harness, slot, count == 0 ? 1 : count == 1 ? 0 : count + 1, false, ct).ConfigureAwait(false);
        }
    }

    private async Task QuerySlotAsync(BedrockServerHarness harness, int slot, int count, bool expected, CancellationToken ct)
    {
        string command = $"testfor @a[name={PlayerName},hasitem={{item=diamond,location=slot.hotbar,slot={slot},quantity={count}}}]";
        int before = harness.Output.Count;
        bool? observed = null;
        string[] lines = [];
        try
        {
            await harness.SendCommandAsync(command, ct).ConfigureAwait(false);
            await WaitUntilAsync(() =>
            {
                lines = harness.Output.Skip(before).ToArray();
                if (lines.Any(line => line.Contains($"Found {PlayerName}", StringComparison.Ordinal)))
                    observed = true;
                else if (lines.Any(static line => line.Contains("No targets matched selector", StringComparison.Ordinal)))
                    observed = false;
                else if (lines.Any(static line => line.Contains("Syntax error", StringComparison.OrdinalIgnoreCase) || line.Contains("Unknown command", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The exact server rejected the calibrated count-selector candidate; no fallback grammar is permitted.");
                return observed.HasValue;
            }, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }
        finally
        {
            _controls.Add(new { command, expected, observed, lines });
        }
        Assert.Equal(expected, observed!.Value);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (condition())
                return;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        throw new TimeoutException($"The reconciliation witness condition did not arrive within {budget}.");
    }

    private async Task SaveArtifactAsync(string name, byte[] payload, CancellationToken ct)
    {
        string path = Path.Combine(_evidenceDirectory, name);
        await File.WriteAllBytesAsync(path, payload, ct).ConfigureAwait(false);
        byte[] readback = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        Assert.Equal(payload, readback);
        string hash = Hash(payload);
        Assert.Equal(hash, Hash(readback));
        _artifacts[name] = new { name, bytes = payload.Length, sha256 = hash, readbackVerified = true };
    }

    private async Task SaveEvidenceAsync(BedrockServerHarness harness, BedrockClientSession? session, string result)
    {
        using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = saveTimeout.Token;
        if (_evidenceDirectory.Length == 0)
            _evidenceDirectory = harness.EvidenceDirectory.Length == 0 ? Path.Combine(harness.EvidenceRootDirectory, $"reconciliation-start-failure-{Guid.NewGuid():N}") : harness.EvidenceDirectory;
        Directory.CreateDirectory(_evidenceDirectory);
        if (session is not null)
        {
            byte[][] responses = session.CapturedItemStackResponsePayloads.ToArray();
            byte[][] contents = session.CapturedInventoryContentPayloads.ToArray();
            byte[][] slots = session.CapturedInventorySlotPayloads.ToArray();
            for (int index = 0; index < _requests.Count; index++)
                await SaveArtifactAsync($"all-147-{index}.bin", _requests[index], ct).ConfigureAwait(false);
            for (int index = 0; index < responses.Length; index++)
                await SaveArtifactAsync($"all-148-{index}.bin", responses[index], ct).ConfigureAwait(false);
            for (int index = 0; index < contents.Length; index++)
                await SaveArtifactAsync($"all-49-{index}.bin", contents[index], ct).ConfigureAwait(false);
            for (int index = 0; index < slots.Length; index++)
                await SaveArtifactAsync($"all-50-{index}.bin", slots[index], ct).ConfigureAwait(false);
        }
        var libraries = new List<object>();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name?.StartsWith("Umpk.", StringComparison.Ordinal) == true && !assembly.IsDynamic))
            libraries.Add(new { assembly = assembly.GetName().Name, path = assembly.Location, version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion, sha256 = await HashFileAsync(assembly.Location, ct).ConfigureAwait(false) });
        string sourcePath = TestSourcePath();
        BedrockInventorySnapshot? stored = session?.InventoryStore.GetSnapshot(PlayerAddress);
        var metadata = new
        {
            result,
            player = PlayerName,
            serverExecutableSha256 = harness.ExecutableSha256,
            harness.ConfigSha256,
            harness.SelectedSourceDistribution,
            harness.EvidenceDirectory,
            savedEvidenceDirectory = _evidenceDirectory,
            harness.RunDirectory,
            harness.ProcessId,
            serverStartStat = _serverStartStat,
            testHostProcessId = Environment.ProcessId,
            testHostStartStat = _testHostStartStat,
            networkNamespace = new FileInfo($"/proc/{Environment.ProcessId}/ns/net").LinkTarget,
            harness.Port,
            harness.IPv6Port,
            sourcePath,
            sourceSha256 = File.Exists(sourcePath) ? await HashFileAsync(sourcePath, ct).ConfigureAwait(false) : null,
            libraries,
            controls = _controls,
            steps = _steps,
            artifacts = _artifacts.Where(pair => pair.Key is not "itemstack-reconciliation.json" and not "itemstack-reconciliation-artifacts.json").Select(pair => pair.Value).ToArray(),
            accepted = session?.ItemStackTracker.AcceptedCount,
            rejected = session?.ItemStackTracker.RejectedCount,
            pending = session?.ItemStackTracker.PendingCount,
            claims = session?.ActionCoordinator.ClaimedCount,
            ready = session?.Snapshot.IsReady,
            termination = session?.Snapshot.Termination?.ToString(),
            playerContentCount = session is null ? 0 : PlayerContentCount(session),
            playerContentCountAtAdmission = _playerContentCountAtAdmission,
            finalHotbar = stored is null ? null : Enumerable.Range(0, Math.Min(9, stored.Value.Items.Length)).Select(slot => DescribeSlot(stored.Value, slot)).ToArray(),
            violations = session?.CapturedViolations.Select(value => new { value.PacketId, value.Severity, value.Reason }).ToArray(),
            wireTranscript = session?.WireTranscript.Select(value => new { value.PacketId, value.Direction }).ToArray(),
            serverOutput = harness.Output,
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(metadata, new JsonSerializerOptions { WriteIndented = true });
        await SaveArtifactAsync("itemstack-reconciliation.json", json, ct).ConfigureAwait(false);
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(_artifacts.Where(pair => pair.Key != "itemstack-reconciliation-artifacts.json").Select(pair => pair.Value).ToArray(), new JsonSerializerOptions { WriteIndented = true });
        await SaveArtifactAsync("itemstack-reconciliation-artifacts.json", manifest, ct).ConfigureAwait(false);
        output.WriteLine($"Reconciliation evidence {Path.Combine(_evidenceDirectory, "itemstack-reconciliation.json")}; SHA256 {Hash(json)}; {result}.");
    }

    private static object DescribeSlot(BedrockInventorySnapshot snapshot, int slot)
    {
        BedrockItemInstance item = snapshot.Items[slot];
        BedrockItemStackResponseSlot? correction = snapshot.ResponseCorrections.IsDefault ? null : snapshot.ResponseCorrections[slot];
        return new { slot, known = snapshot.IsSlotKnown(slot), item.NetworkId, item.Count, item.MetadataValue, item.StackNetworkId, item.BlockRuntimeId, extraDataHex = Convert.ToHexString(item.ExtraData).ToLowerInvariant(), correction = correction is null ? null : new { correction.Slot, correction.HotbarSlot, correction.Count, correction.StackId, correction.CustomName, correction.FilteredCustomName, correction.DurabilityCorrection } };
    }

    private static string TestSourcePath([CallerFilePath] string path = "") => path;

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)).ToLowerInvariant();
    }
}
