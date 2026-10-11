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

[Collection("Bedrock terrain capture")]
public sealed class UpdateSubChunkBlocksLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkSubChunkBot";

    [BedrockLiveFact]
    public async Task BulkFillBroadcastsUpdateSubChunkBlocks()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_TERRAIN");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_TERRAIN", "1");
        string fillLabel = $"execute as @a[name={PlayerName}] at @s run fill ~1 ~ ~1 ~3 ~ ~3 diamond_block";
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
            try
            {
                BedrockSessionSnapshot ready = await session.WaitUntilReadyAsync(ct);

                Assert.True(ready.IsReady);
                Assert.True(session.Snapshot.IsReady);
                Assert.True(session.Snapshot.Termination is null);
                Assert.True(session.Snapshot.LevelChunksObserved > 0);

                bool requested = await WaitForConditionAsync(
                    () => session.TerrainRequestsSent > 0 && session.RequestedTerrainColumns.Count > 0,
                    TimeSpan.FromSeconds(30),
                    ct);
                Assert.True(requested, "The session sent no sub-chunk request.");

                int outputBefore = harness.Output.Count;
                session.ResetSubChunkBlocksCapture();
                await harness.SendCommandAsync(fillLabel, ct);

                Assert.True(await WaitForPositiveFillCountAsync(harness, outputBefore, TimeSpan.FromSeconds(20), ct), $"The BDS did not report a positive fill count. Tail: {string.Join(" | ", harness.Output.Skip(outputBefore).TakeLast(5))}");
                (byte[] matched, IReadOnlyList<(int X, int Y, int Z)> cells, uint runtimeId) = await WaitForBulkFillRegionAsync(session, TimeSpan.FromSeconds(20), ct);
                Assert.Equal(9, cells.Count);
                output.WriteLine($"Player-relative fill drew a nine-cell packet 172 ({matched.Length} bytes) with runtime {runtimeId} at ({cells[0].X}, {cells[0].Y}, {cells[0].Z}).");
                AssertFillSucceeded(harness, outputBefore);
                await AssertStoreAppliedProvokedFillAsync(session, cells, runtimeId, output, ct).ConfigureAwait(false);
                await WriteCaptureAsync(harness, session, cells, runtimeId, ct).ConfigureAwait(false);
                Assert.True(session.Snapshot.IsReady);
                Assert.False(session.Completion.IsCompleted);
                Assert.Null(session.Snapshot.Termination);

                await session.DisconnectAsync(ct);
            }
            catch (Exception failure)
            {
                await WriteSubChunkFailureCaptureAsync(harness, session, failure, fillLabel, ct).ConfigureAwait(false);
                throw;
            }
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_TERRAIN", previousCapture);
        }
    }

    private async Task WriteCaptureAsync(BedrockServerHarness harness, BedrockClientSession session, IReadOnlyList<(int X, int Y, int Z)> cells, uint runtimeId, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-subchunkblocks-capture", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        var payloads = session.CapturedSubChunkBlocks;
        for (int index = 0; index < payloads.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"updatesubchunkblocks-{index}.bin"), payloads[index], ct).ConfigureAwait(false);
        var responses = session.CapturedSubChunkResponses;
        for (int index = 0; index < responses.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"subchunk-response-{index}.bin"), responses[index], ct).ConfigureAwait(false);
        if (session.CapturedStartGameRemainder is byte[] startGame)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, "startgame-remainder.bin"), startGame, ct).ConfigureAwait(false);
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
            packetId = 172,
            player = PlayerName,
            fill = new
            {
                command = $"execute as @a[name={PlayerName}] at @s run fill ~1 ~ ~1 ~3 ~ ~3 diamond_block",
                cells = cells.Select(static cell => new { x = cell.X, y = cell.Y, z = cell.Z }).ToArray(),
                runtimeId,
            },
            vectors = payloads.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
                payloadHex = Convert.ToHexString(payload).ToLowerInvariant(),
            }).ToArray(),
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            terrainSnapshot = session.TerrainSnapshot.ToString(),
            rejectedSendAttempts = session.RejectedSendAttempts,
            termination = session.Snapshot.Termination?.ToString(),
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "updatesubchunkblocks-metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "updatesubchunkblocks-capture.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"Packet 172 capture written to {captureRoot}.");
    }

    private async Task WriteSubChunkFailureCaptureAsync(BedrockServerHarness harness, BedrockClientSession session, Exception failure, string fillLabel, CancellationToken ct)
    {
        const int maxPayloads = 8;
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-subchunkblocks-capture", Path.GetFileName(harness.EvidenceDirectory) + "-failure");
        Directory.CreateDirectory(captureRoot);
        IReadOnlyList<byte[]> responses = session.CapturedSubChunkResponses;
        int count = Math.Min(responses.Count, maxPayloads);
        for (int index = 0; index < count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"subchunk-response-{index}.bin"), responses[index], ct).ConfigureAwait(false);
        IReadOnlyList<byte[]> blocks = session.CapturedSubChunkBlocks;
        int blockCount = Math.Min(blocks.Count, maxPayloads);
        for (int index = 0; index < blockCount; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"updatesubchunkblocks-{index}.bin"), blocks[index], ct).ConfigureAwait(false);
        var metadata = new
        {
            failure = failure.ToString(),
            fill = fillLabel,
            bdsOutputTail = harness.Output.TakeLast(30).ToArray(),
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
            subChunkResponses = responses.Take(count).Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            subChunkBlocks = blocks.Take(blockCount).Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            terrainSnapshot = session.TerrainSnapshot.ToString(),
            terrainApplied = session.TerrainApplied,
            terrainRefused = session.TerrainApplyRefused,
            terrainStale = session.TerrainStaleDropped,
            rejectedSendAttempts = session.RejectedSendAttempts,
            termination = session.Snapshot.Termination?.ToString(),
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "subchunk-failure.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "subchunk-failure.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"Packet 172 and 174 failure capture ({blockCount} blocks, {count} responses) written to {captureRoot}.");
    }

    private static async Task<bool> WaitForPositiveFillCountAsync(BedrockServerHarness harness, int outputBefore, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            string[] fresh = harness.Output.Skip(outputBefore).ToArray();
            if (fresh.Any(static line => line.Contains("Cannot place blocks outside of the world", StringComparison.Ordinal) || line.Contains("0 blocks filled", StringComparison.Ordinal)))
                return false;
            if (fresh.Any(static line => System.Text.RegularExpressions.Regex.IsMatch(line, "[1-9][0-9]* blocks filled")))
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return harness.Output.Skip(outputBefore).Any(static line => System.Text.RegularExpressions.Regex.IsMatch(line, "[1-9][0-9]* blocks filled"));
    }

    private static async Task<(byte[] Matched, IReadOnlyList<(int X, int Y, int Z)> Cells, uint RuntimeId)> WaitForBulkFillRegionAsync(BedrockClientSession session, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach (byte[] captured in session.CapturedSubChunkBlocks)
            {
                BedrockUpdateSubChunkBlocksPacket packet = BedrockUpdateSubChunkBlocksCodecs.Decode(captured);
                if (BulkFillPacketMatcher.TryMatchNineCellRegion(packet, out IReadOnlyList<(int X, int Y, int Z)> cells, out uint runtimeId))
                    return (captured, cells, runtimeId);
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        foreach (byte[] captured in session.CapturedSubChunkBlocks)
        {
            BedrockUpdateSubChunkBlocksPacket packet = BedrockUpdateSubChunkBlocksCodecs.Decode(captured);
            if (BulkFillPacketMatcher.TryMatchNineCellRegion(packet, out IReadOnlyList<(int X, int Y, int Z)> cells, out uint runtimeId))
                return (captured, cells, runtimeId);
        }

        throw new TimeoutException($"No post-command packet 172 carried an exact nine-cell region (captured: {session.CapturedSubChunkBlocks.Count}, post-join 172s: {session.PostJoinPacketIds.Count(id => id == 172)}, termination: {session.Snapshot.Termination}, applied: {session.TerrainApplied}, refused: {session.TerrainApplyRefused}, stale: {session.TerrainStaleDropped}).");
    }

    private async Task AssertStoreAppliedProvokedFillAsync(BedrockClientSession session, IReadOnlyList<(int X, int Y, int Z)> cells, uint runtimeId, ITestOutputHelper testOutput, CancellationToken ct)
    {
        bool applied = await WaitForConditionAsync(
            () =>
            {
                foreach ((int X, int Y, int Z) position in cells)
                {
                    var query = session.GetTerrainBlock(position.X, position.Y, position.Z);
                    if (query is not { Availability: Umpk.Client.Bedrock.State.World.BedrockCellAvailability.Complete } complete
                        || complete.Cell!.Value.Primary.RawId != unchecked((int)runtimeId))
                        return false;
                }

                return true;
            },
            TimeSpan.FromSeconds(20),
            ct).ConfigureAwait(false);
        Assert.True(applied, "The store did not apply the derived fill runtimes within the budget.");
        testOutput.WriteLine($"Store applied all {cells.Count} derived edits with runtime {runtimeId}.");
    }

    private void AssertFillSucceeded(BedrockServerHarness harness, int outputBefore)
    {
        string[] fresh = harness.Output.Skip(outputBefore).ToArray();
        foreach (string line in fresh)
            output.WriteLine($"BDS: {line}");
        Assert.DoesNotContain(fresh, static line => line.Contains("Cannot place blocks outside of the world", StringComparison.Ordinal));
        Assert.DoesNotContain(fresh, static line => line.Contains("0 blocks filled", StringComparison.Ordinal));
        Assert.Contains(fresh, static line => System.Text.RegularExpressions.Regex.IsMatch(line, "[1-9][0-9]* blocks filled"));
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
}
