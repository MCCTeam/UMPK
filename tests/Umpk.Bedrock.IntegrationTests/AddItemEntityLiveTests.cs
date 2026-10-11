using System.Security.Cryptography;
using System.Text.Json;
using Umpk.Client.Bedrock;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.Session;
using Umpk.Client.Bedrock.State.Actors;
using Umpk.Protocol.Bedrock;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Bedrock.IntegrationTests;

[Collection("Bedrock actor capture")]
public sealed class AddItemEntityLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkItemBot";
    private const string CaptureVariable = "UMPK_BEDROCK_CAPTURE_ACTORS";

    /// <summary>Proves clientbound AddItemEntity packet 15 with a live BDS sheep drop and a byte-identical codec round trip.</summary>
    [BedrockLiveFact]
    public async Task OfflineSheepDropExposesPacket15RoundTrip()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable(CaptureVariable);
        Environment.SetEnvironmentVariable(CaptureVariable, "1");
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

            session.ResetAddItemEntityCapture();

            output.WriteLine($"Provoking a sheep drop four blocks from {PlayerName} with a server-side execute summon.");
            int outputBefore = harness.Output.Count;
            await harness.SendCommandAsync($"execute as {PlayerName} at @s run summon sheep ~4 ~ ~", ct);
            bool sheepObserved = await WaitForConditionAsync(
                () => session.ActorSnapshots.Any(static actor => actor.EntityType == "minecraft:sheep"),
                TimeSpan.FromSeconds(30),
                ct);
            output.WriteLine(sheepObserved
                ? "Session observed the summoned sheep."
                : $"Session observed no summoned sheep; post-join IDs: {string.Join(",", session.PostJoinPacketIds.Distinct().Order())}. BDS console: {string.Join(" | ", harness.Output.Skip(outputBefore).TakeLast(10))}.");
            Assert.True(sheepObserved, "The BDS execute summon produced no sheep actor for this session.");

            await harness.SendCommandAsync("kill @e[type=sheep,c=1]", ct);
            BedrockActorSnapshot? item = await WaitForDroppedItemAsync(session, TimeSpan.FromSeconds(60), ct);
            output.WriteLine(item is null
                ? "Session observed no dropped-item actor after the sheep kill; the drop leg fails instead of passing silently."
                : $"Session observed a dropped item with network {item.DroppedItem!.NetworkId} count {item.DroppedItem.Count} runtime {item.Handle.RuntimeEntityId}.");
            Assert.True(item is not null, "The BDS sheep kill produced no dropped-item actor for this session.");

            int inbound15 = session.WireTranscript.Count(static entry => entry.Direction == BedrockWireDirection.Inbound && entry.PacketId == 15);
            output.WriteLine($"Wire transcript carries {inbound15} inbound packet 15 event(s).");
            Assert.True(inbound15 > 0);
            Assert.Contains(15, session.PostJoinPacketIds);

            BedrockActorWireWitness? witness = session.ActorWireWitnesses.FirstOrDefault(static entry => entry.PacketId == 15);
            output.WriteLine(witness is null
                ? "Session recorded no packet 15 actor witness."
                : $"Session recorded a packet 15 actor witness of {witness.TotalLength} bytes.");
            Assert.NotNull(witness);

            IReadOnlyList<byte[]> payloads = session.CapturedAddItemEntityPayloads;
            output.WriteLine(payloads.Count > 0
                ? $"Session retained {payloads.Count} raw packet 15 payload(s) under {CaptureVariable}=1."
                : "Session retained no raw packet 15 payload; the capture seam stayed shut.");
            Assert.NotEmpty(payloads);

            var decodedSummaries = new List<object>();
            bool matchedObservedItem = false;
            foreach (byte[] payload in payloads)
            {
                Assert.True(payload.Length > 0);
                BedrockAddItemEntityPacket decoded = BedrockActorCodecs.DecodeAddItemEntity(payload);
                bool roundTrip = payload.SequenceEqual(BedrockActorCodecs.Encode(decoded));
                Assert.True(roundTrip, $"The packet 15 payload for runtime {decoded.RuntimeEntityId} did not re-encode byte-identically.");
                if (decoded.RuntimeEntityId == item!.Handle.RuntimeEntityId && decoded.UniqueEntityId == item.Handle.UniqueEntityId)
                    matchedObservedItem = true;
                decodedSummaries.Add(new
                {
                    payloadBytes = payload.Length,
                    payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
                    networkId = decoded.Item.NetworkId,
                    count = decoded.Item.Count,
                    runtimeEntityId = decoded.RuntimeEntityId,
                    uniqueEntityId = decoded.UniqueEntityId,
                    isFromFishing = decoded.IsFromFishing,
                    roundTrip,
                });
                output.WriteLine($"Packet 15 payload: {payload.Length} bytes with item network {decoded.Item.NetworkId} and round trip {roundTrip}.");
            }

            Assert.True(matchedObservedItem, "No captured packet 15 payload matches the observed dropped-item actor identity.");
            Assert.True(item.DroppedItem!.NetworkId != 0, "The observed dropped item carries an air network id.");

            await WriteEvidenceAsync(harness, session, witness!, item, inbound15, decodedSummaries, ct).ConfigureAwait(false);
            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);
            Assert.Null(session.Snapshot.Termination);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(CaptureVariable, previousCapture);
        }
    }

    private static async Task<BedrockActorSnapshot?> WaitForDroppedItemAsync(BedrockClientSession session, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockActorSnapshot? item = session.ActorSnapshots.FirstOrDefault(static actor => actor.Kind == BedrockActorKind.Item && actor.DroppedItem is not null && actor.DroppedItem.NetworkId != 0);
            if (item is not null)
                return item;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return session.ActorSnapshots.FirstOrDefault(static actor => actor.Kind == BedrockActorKind.Item && actor.DroppedItem is not null && actor.DroppedItem.NetworkId != 0);
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

    private async Task WriteEvidenceAsync(
        BedrockServerHarness harness,
        BedrockClientSession session,
        BedrockActorWireWitness witness,
        BedrockActorSnapshot item,
        int inbound15,
        List<object> decodedSummaries,
        CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-add-item", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        IReadOnlyList<byte[]> payloads = session.CapturedAddItemEntityPayloads;
        for (int index = 0; index < payloads.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"add-item-entity-raw-{index}.bin"), payloads[index], ct).ConfigureAwait(false);
        var evidence = new
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
            packetCount = inbound15,
            postJoin15 = session.PostJoinPacketIds.Count(static id => id == 15),
            capturedPayloads = payloads.Count,
            itemNetworkId = item.DroppedItem!.NetworkId,
            itemCount = item.DroppedItem.Count,
            itemRuntimeEntityId = item.Handle.RuntimeEntityId,
            itemUniqueEntityId = item.Handle.UniqueEntityId,
            payloadHash = Convert.ToHexString(SHA256.HashData(payloads[0])).ToLowerInvariant(),
            payloadBytes = payloads[0].Length,
            payloads = decodedSummaries.ToArray(),
            roundTrip = true,
            witnessBytes = witness.TotalLength,
            rejectedSendAttempts = session.RejectedSendAttempts,
            isReady = session.Snapshot.IsReady,
            result = "pass",
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "add-item-entity.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "add-item-entity-evidence.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        output.WriteLine($"AddItemEntity evidence written to {captureRoot}.");
    }
}
