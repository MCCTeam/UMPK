using System.Security.Cryptography;
using System.Text.Json;
using Umpk.Client.Bedrock;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.Session;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Bedrock.IntegrationTests;

[Collection("Bedrock terrain capture")]
public sealed class TerrainCaptureLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkTerrainBot";

    [BedrockLiveFact]
    public async Task OfflineTerrainCaptureReachesReadyAndProvokesUpdateBlock()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_TERRAIN");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_TERRAIN", "1");
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
            BedrockSessionSnapshot ready;
            try
            {
                ready = await session.WaitUntilReadyAsync(ct);
            }
            finally
            {
                await WriteTerrainCaptureAsync(harness, session, "join").ConfigureAwait(false);
            }

            Assert.True(ready.IsReady);
            Assert.True(session.Snapshot.LevelChunksObserved > 0);
            Assert.NotNull(session.CapturedStartGameRemainder);
            Assert.NotEmpty(session.CapturedLevelChunks);
            Assert.Equal(0, session.TerrainDimension);
            Assert.True(session.TerrainHashes);
            Assert.Equal(0x28301E81E1209643UL, session.TerrainChecksum);

            bool requested = await WaitForConditionAsync(
                () => session.TerrainRequestsSent > 0 && session.RequestedTerrainColumns.Count > 0,
                TimeSpan.FromSeconds(30),
                ct);
            output.WriteLine(requested
                ? $"Session requested {session.TerrainRequestsSent} sub-chunk batch(es)."
                : "Session sent no sub-chunk request.");
            Assert.True(requested);

            await WaitForLiveColumnAsync(session, ct);
            output.WriteLine($"Store holds requested sections (snapshot: {session.TerrainSnapshot}).");

            bool proven = false;
            int beforeHash = 0;
            int afterHash = 0;
            for (int attempt = 0; attempt < 3 && !proven; attempt++)
            {
                (int x, int y, int z, int hash)? cell = FindCompleteCell(session, attempt);
                if (cell is null)
                    break;
                beforeHash = cell.Value.hash;
                output.WriteLine($"Provoking setblock at ({cell.Value.x}, {cell.Value.y}, {cell.Value.z}) over hash {beforeHash} (attempt {attempt + 1}).");
                await harness.SendCommandAsync($"setblock {cell.Value.x} {cell.Value.y} {cell.Value.z} diamond_block", ct);
                bool updateObserved = await WaitForConditionAsync(
                    () => DecodePostJoinUpdateBlocks(session).Any(position => position == (cell.Value.x, cell.Value.y, cell.Value.z)),
                    TimeSpan.FromSeconds(20),
                    ct);
                if (!updateObserved)
                {
                    output.WriteLine($"Attempt {attempt + 1} drew no UpdateBlock for its cell.");
                    continue;
                }

                afterHash = beforeHash;
                bool updateApplied = await WaitForConditionAsync(
                    () =>
                    {
                        var query = session.GetTerrainBlock(cell.Value.x, cell.Value.y, cell.Value.z);
                        if (query is not { Availability: Umpk.Client.Bedrock.State.World.BedrockCellAvailability.Complete })
                            return false;
                        afterHash = query.Cell!.Value.Primary.RawId;
                        return afterHash != beforeHash;
                    },
                    TimeSpan.FromSeconds(20),
                    ct);
                output.WriteLine(updateApplied
                    ? $"Store applied the provoked UpdateBlock ({beforeHash} to {afterHash})."
                    : $"Store did not apply the provoked UpdateBlock (still {afterHash}).");
                proven = updateApplied;
            }

            await WriteTerrainCaptureAsync(harness, session, "provoked").ConfigureAwait(false);
            Assert.True(proven, "No setblock attempt drew a position-matched UpdateBlock and applied it to the store.");
            Assert.NotNull(session.CapturedUpdateBlockPayload);
            Assert.False(session.Completion.IsCompleted);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_TERRAIN", previousCapture);
        }
    }

    private async Task WriteTerrainCaptureAsync(BedrockServerHarness harness, BedrockClientSession session, string stage)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-terrain-capture", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        byte[]? startGame = session.CapturedStartGameRemainder;
        if (startGame is not null)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"startgame-remainder-{stage}.bin"), startGame).ConfigureAwait(false);
        var chunks = session.CapturedLevelChunks;
        for (int index = 0; index < chunks.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"levelchunk-{index}-{stage}.payload.bin"), chunks[index].Payload).ConfigureAwait(false);
        byte[]? update = session.CapturedUpdateBlockPayload;
        if (update is not null)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"updateblock-{stage}.bin"), update).ConfigureAwait(false);
        var subChunks = session.CapturedSubChunkResponses;
        for (int index = 0; index < subChunks.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"subchunk-{index}-{stage}.bin"), subChunks[index]).ConfigureAwait(false);
        var metadata = new
        {
            stage,
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
            startGameRemainderBytes = startGame?.Length ?? 0,
            startGameRemainderSha256 = startGame is null ? null : Convert.ToHexString(SHA256.HashData(startGame)).ToLowerInvariant(),
            levelChunks = chunks.Select(static chunk => new
            {
                chunk.X,
                chunk.Z,
                chunk.Dimension,
                subChunkCount = chunk.SubChunkCount,
                highestSubchunkCount = chunk.HighestSubchunkCount,
                chunk.CacheEnabled,
                blobCount = chunk.BlobHashes.Count,
                payloadBytes = chunk.Payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(chunk.Payload)).ToLowerInvariant(),
            }).ToArray(),
            levelChunksObserved = session.Snapshot.LevelChunksObserved,
            updateBlockBytes = update?.Length ?? 0,
            updateBlockSha256 = update is null ? null : Convert.ToHexString(SHA256.HashData(update)).ToLowerInvariant(),
            terrainDimension = session.TerrainDimension,
            terrainHashes = session.TerrainHashes,
            terrainChecksum = session.TerrainChecksum,
            terrainWorldGeneration = session.TerrainWorldGeneration,
            terrainSnapshot = session.TerrainSnapshot.ToString(),
            terrainApplied = session.TerrainApplied,
            terrainStaleDropped = session.TerrainStaleDropped,
            terrainApplyRefused = session.TerrainApplyRefused,
            terrainWorkDropped = session.TerrainWorkDropped,
            terrainRequestsSent = session.TerrainRequestsSent,
            subChunkResultCounts = session.SubChunkResultCounts,
            subChunkResponses = subChunks.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            requestedTerrainColumns = session.RequestedTerrainColumns.Select(static column => new
            {
                column.Dimension,
                column.X,
                column.Z,
            }).ToArray(),
            levelChunkEnvelopes = session.CapturedLevelChunkEnvelopes.Select(static envelope => new
            {
                envelope.X,
                envelope.Z,
                envelope.Dimension,
                subChunkCount = envelope.SubChunkCount,
            }).ToArray(),
            postJoinUpdateBlocks = session.CapturedPostJoinUpdateBlocks.Select(static payload => Convert.ToHexString(payload)).ToArray(),
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            rejectedSendAttempts = session.RejectedSendAttempts,
            termination = session.Snapshot.Termination?.ToString(),
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, $"terrain-metadata-{stage}.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, $"terrain-capture-{stage}.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        output.WriteLine($"Terrain capture ({stage}) written to {captureRoot}.");
    }

    private async Task<(int ColumnX, int ColumnZ, int FillY)> WaitForLiveColumnAsync(BedrockClientSession session, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(90);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            foreach ((int _, int columnX, int columnZ) in session.RequestedTerrainColumns)
            {
                if (session.GetTerrainColumn(columnX, columnZ) == Umpk.Client.Bedrock.State.World.BedrockColumnAvailability.Absent)
                    continue;
                for (int y = -64; y < 320; y++)
                {
                    if (session.GetTerrainBlock(columnX * 16 + 1, y, columnZ * 16 + 1).Availability
                        == Umpk.Client.Bedrock.State.World.BedrockCellAvailability.Complete)
                        return (columnX, columnZ, y);
                }
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"No requested column gained a complete cell (snapshot: {session.TerrainSnapshot}).");
    }

    private static (int X, int Y, int Z, int Hash)? FindCompleteCell(BedrockClientSession session, int rotation)
    {
        var columns = session.RequestedTerrainColumns;
        if (columns.Count == 0)
            return null;
        for (int shift = 0; shift < columns.Count; shift++)
        {
            (int _, int columnX, int columnZ) = columns[(rotation + shift) % columns.Count];
            for (int y = -64; y < 320; y++)
            {
                var query = session.GetTerrainBlock(columnX * 16 + 1, y, columnZ * 16 + 1);
                if (query is { Availability: Umpk.Client.Bedrock.State.World.BedrockCellAvailability.Complete })
                    return (columnX * 16 + 1, y, columnZ * 16 + 1, query.Cell!.Value.Primary.RawId);
            }
        }

        return null;
    }

    private static List<(int X, int Y, int Z)> DecodePostJoinUpdateBlocks(BedrockClientSession session)
    {
        var positions = new List<(int X, int Y, int Z)>();
        foreach (byte[] payload in session.CapturedPostJoinUpdateBlocks)
        {
            Umpk.Protocol.Bedrock.BedrockUpdateBlockPacket decoded = Umpk.Protocol.Bedrock.BedrockUpdateBlockCodecs.DecodeUpdateBlock(payload);
            positions.Add((decoded.X, decoded.Y, decoded.Z));
        }

        return positions;
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
