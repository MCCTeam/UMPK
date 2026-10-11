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

[Collection("Bedrock motion capture")]
public sealed class PlayerAuthInputExternalObserverLiveTests(ITestOutputHelper output)
{
    private const string ExternalPlayerVariable = "UMPK_BEDROCK_EXTERNAL_PLAYER";
    private const string ObserverName = "UmpkWitnessBot";
    private const string MarkerDirectory = "/tmp/umpk-bedrock-external-observer";
    private const string MarkerFileName = "ready.json";

    [BedrockLiveFact]
    public async Task ExternalClientMovementIsObservedWithTeleportControl()
    {
        string playerName = RequireExternalPlayerName(() => Environment.GetEnvironmentVariable(ExternalPlayerVariable));
        DeleteReadyMarker();
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness(creativeMode: true, lanVisibility: true);
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}, config {harness.ConfigSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", "1");
        try
        {
            await using var observerClient = CreateClient(harness.Port, ObserverName);
            await using BedrockClientSession observer = await observerClient.ConnectAsync(ct);
            Assert.True((await observer.WaitUntilReadyAsync(ct)).IsReady);

            await PrepareSharedPadAsync(harness, ct);

            WriteReadyMarker(MarkerDirectory, new ExternalObserverMarker(
                "127.0.0.1",
                harness.Port,
                harness.ExecutableSha256,
                harness.ConfigSha256,
                harness.CreativeModeRequested,
                harness.LanVisibilityRequested,
                ObserverName,
                playerName,
                DateTimeOffset.UtcNow));
            output.WriteLine($"Ready marker written to {Path.Combine(MarkerDirectory, MarkerFileName)} for player '{playerName}'. Waiting up to 15 minutes for the real client to join.");

            BedrockActorSnapshot playerSeenByObserver = await WaitForPlayerAsync(observer, playerName, TimeSpan.FromMinutes(15), ct);
            ulong playerRuntimeId = playerSeenByObserver.Handle.RuntimeEntityId;
            Assert.NotEqual(0UL, playerRuntimeId);
            output.WriteLine($"Observer sees real player '{playerName}' as runtime {playerRuntimeId}, unique {playerSeenByObserver.Handle.UniqueEntityId}.");

            observer.ResetMotionCapture();

            int teleportBefore = harness.Output.Count;
            await harness.SendCommandAsync($"teleport {playerName} 8 90 8", ct);
            Assert.True(await WaitForServerLineAsync(harness, teleportBefore, "Teleported", playerName, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the player teleport.");
            BedrockMovePlayerPacket teleportRelay = await WaitForObserverMoveAsync(observer, playerRuntimeId, ct);
            string teleportPayloadSha256 = Convert.ToHexString(SHA256.HashData(observer.CapturedMovePlayerPayloads.Last(payload => BedrockActorCodecs.DecodeMovePlayer(payload).RuntimeEntityId == playerRuntimeId))).ToLowerInvariant();
            Assert.InRange(Math.Abs(teleportRelay.Position.X - 8.5f), 0f, 0.05f);
            Assert.InRange(Math.Abs(teleportRelay.Position.Z - 8.5f), 0f, 0.05f);
            Assert.InRange(Math.Abs(teleportRelay.Position.Y - 91.62f), 0f, 0.05f);
            output.WriteLine($"Positive control: observer relayed teleport of runtime {playerRuntimeId} to ({teleportRelay.Position.X}, {teleportRelay.Position.Y}, {teleportRelay.Position.Z}) mode {(byte)teleportRelay.Mode} tick {teleportRelay.Tick}.");

            int movesBeforeWindow = CountObserverMoves(observer, playerRuntimeId);
            int motionsBeforeWindow = observer.CapturedSetEntityMotions.Count;
            int predictionsBeforeWindow = observer.CapturedMovePredictions.Count;
            output.WriteLine("Observation window: 120 s. The real player should walk and jump on the pad now.");
            await Task.Delay(TimeSpan.FromSeconds(120), ct).ConfigureAwait(false);

            IReadOnlyList<BedrockMovePlayerPacket> freshMoves = observer.CapturedMovePlayers.Where(packet => packet.RuntimeEntityId == playerRuntimeId).Skip(movesBeforeWindow).ToArray();
            double maxDisplacement = 0;
            foreach (BedrockMovePlayerPacket move in freshMoves)
            {
                double displacement = Math.Sqrt(Math.Pow(move.Position.X - teleportRelay.Position.X, 2) + Math.Pow(move.Position.Y - teleportRelay.Position.Y, 2) + Math.Pow(move.Position.Z - teleportRelay.Position.Z, 2));
                maxDisplacement = Math.Max(maxDisplacement, displacement);
            }

            ulong[] freshMoveTicks = freshMoves.Select(static packet => packet.Tick).ToArray();
            ulong[] freshMotionTicks = observer.CapturedSetEntityMotions.Skip(motionsBeforeWindow).Select(static packet => packet.Tick).ToArray();
            ulong[] freshPredictionTicks = observer.CapturedMovePredictions.Skip(predictionsBeforeWindow).Select(static packet => packet.Tick).ToArray();
            output.WriteLine($"Window result: {freshMoves.Count} fresh relays for runtime {playerRuntimeId}, max displacement {maxDisplacement:F4} blocks, move ticks {string.Join(",", freshMoveTicks.Take(20))}.");
            Assert.True(freshMoves.Count > 0, "The observer recorded no movement relays for the real player during the 120 s window. The player may have stood still or disconnected.");
            Assert.True(maxDisplacement > 0.5, $"The real player moved only {maxDisplacement:F4} blocks during the window; walk or jump on the pad for a clear signal.");

            await WriteEvidenceAsync(harness, observer, playerName, playerRuntimeId, teleportRelay, teleportPayloadSha256, freshMoves, freshMoveTicks, maxDisplacement, ct);

            Assert.True(observer.Snapshot.IsReady);
            Assert.Equal(0, observer.RejectedSendAttempts);
            await observer.DisconnectAsync(ct);
            await observerClient.DisposeAsync();
        }
        finally
        {
            DeleteReadyMarker();
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", previousCapture);
        }
    }

    private static string RequireExternalPlayerName(Func<string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);
        string? value = readVariable();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Set {ExternalPlayerVariable} to the real client's exact display name before running the external observer leg.");
        return value;
    }

    private sealed record ExternalObserverMarker(string Host, int Port, string ExecutableSha256, string ConfigSha256, bool CreativeMode, bool LanVisibility, string Observer, string ExpectedPlayer, DateTimeOffset ReadyUtc);

    private static void WriteReadyMarker(string directory, ExternalObserverMarker marker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(marker);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, MarkerFileName);
        if (File.Exists(path))
            File.Delete(path);
        File.WriteAllText(path, JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void DeleteReadyMarker()
    {
        string path = Path.Combine(MarkerDirectory, MarkerFileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static BedrockClient CreateClient(int port, string playerName) =>
        new(new BedrockClientSettings
        {
            Host = "127.0.0.1",
            Port = (ushort)port,
            CredentialProvider = new OfflineBedrockCredentialProvider(playerName, Guid.NewGuid()),
            ResourcePackPolicy = new BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions.DeclineDownloads),
            Timeouts = BedrockClientTimeouts.Default with
            {
                Dial = TimeSpan.FromSeconds(15),
                LoginSuccess = TimeSpan.FromSeconds(30),
                ResourceNegotiation = TimeSpan.FromSeconds(60),
                Ready = TimeSpan.FromSeconds(120),
            },
        });

    private async Task PrepareSharedPadAsync(BedrockServerHarness harness, CancellationToken ct)
    {
        int teleportObserverBefore = harness.Output.Count;
        await harness.SendCommandAsync($"teleport {ObserverName} 12 90 8", ct);
        Assert.True(await WaitForServerLineAsync(harness, teleportObserverBefore, "Teleported", ObserverName, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the observer pad teleport.");
        int fillBefore = harness.Output.Count;
        await harness.SendCommandAsync("fill 0 89 0 15 89 15 stone", ct);
        await WaitForServerLineAsync(harness, fillBefore, "blocks filled", string.Empty, TimeSpan.FromSeconds(20), ct);
        int airBefore = harness.Output.Count;
        await harness.SendCommandAsync("fill 0 90 0 15 92 15 air", ct);
        await WaitForServerLineAsync(harness, airBefore, "blocks filled", string.Empty, TimeSpan.FromSeconds(20), ct);
    }

    private static int CountObserverMoves(BedrockClientSession observer, ulong runtimeId) => observer.CapturedMovePlayers.Count(packet => packet.RuntimeEntityId == runtimeId);

    private async Task<BedrockMovePlayerPacket> WaitForObserverMoveAsync(BedrockClientSession observer, ulong runtimeId, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockMovePlayerPacket? match = observer.CapturedMovePlayers.LastOrDefault(packet => packet.RuntimeEntityId == runtimeId);
            if (match is not null)
                return match;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"The observer did not relay a move for runtime {runtimeId}.");
    }

    private static async Task<BedrockActorSnapshot> WaitForPlayerAsync(BedrockClientSession session, string username, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockActorSnapshot? match = session.ActorSnapshots.FirstOrDefault(actor => actor.DisplayName == username);
            if (match is not null)
                return match;
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"The real player '{username}' did not join within {budget.TotalMinutes:F0} minutes. Check the client address, port, and display name.");
    }

    private static async Task<bool> WaitForServerLineAsync(BedrockServerHarness harness, int outputBefore, string marker, string player, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            string[] fresh = harness.Output.Skip(outputBefore).ToArray();
            if (fresh.Any(line => line.Contains(marker, StringComparison.Ordinal) && (player.Length == 0 || line.Contains(player, StringComparison.Ordinal))))
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return harness.Output.Skip(outputBefore).Any(line => line.Contains(marker, StringComparison.Ordinal) && (player.Length == 0 || line.Contains(player, StringComparison.Ordinal)));
    }

    private async Task WriteEvidenceAsync(BedrockServerHarness harness, BedrockClientSession observer, string playerName, ulong playerRuntimeId, BedrockMovePlayerPacket teleportRelay, string teleportPayloadSha256, IReadOnlyList<BedrockMovePlayerPacket> freshMoves, ulong[] freshMoveTicks, double maxDisplacement, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-external-observer", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
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
                configSha256 = harness.ConfigSha256,
                creativeModeRequested = harness.CreativeModeRequested,
                lanVisibilityRequested = harness.LanVisibilityRequested,
                effectiveLanVisibility = harness.EffectiveLanVisibility,
                strictMovement = harness.EffectiveMovementStrict,
            },
            protocol = 2169,
            observer = ObserverName,
            externalPlayer = playerName,
            playerRuntimeId,
            teleportRelay = new { x = teleportRelay.Position.X, y = teleportRelay.Position.Y, z = teleportRelay.Position.Z, mode = (byte)teleportRelay.Mode, tick = teleportRelay.Tick },
            teleportPayloadSha256,
            freshObserverMoves = freshMoves.Count,
            freshMoveTicks = freshMoveTicks.Take(100).ToArray(),
            maxDisplacementBlocks = maxDisplacement,
            observerRejectedSends = observer.RejectedSendAttempts,
            observerReady = observer.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(Path.Combine(captureRoot, "external-observer.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(harness.EvidenceDirectory, "external-observer.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"External observer evidence written to {captureRoot}.");
    }
}
