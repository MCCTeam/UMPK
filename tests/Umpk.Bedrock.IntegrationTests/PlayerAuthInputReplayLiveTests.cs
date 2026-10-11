using System.Buffers.Binary;
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

/// <summary>Replays verified Bedrock client movement frames against an isolated server and records whether an independent observer sees movement or correction.</summary>
[Collection("Bedrock motion capture")]
public sealed class PlayerAuthInputReplayLiveTests(ITestOutputHelper output)
{
    private const string ActorName = "UmpkActorBot";
    private const string ObserverName = "UmpkObserverBot";
    private const string DefaultCaptureRelativePath = ".agent-state/private-captures/phase12-2026-09-20/genuine-144.bin";
    private const string DefaultBundleSha256 = "6d98051c8ad4d7d4256f9e3795b19dfdbe07be80fd1b099cf08f3b195d4266f4";
    private const int DefaultFirstSeq = 68;
    private const int DefaultFrameCount = 8;
    private const ulong DefaultFirstTick = 71;
    private const ulong DefaultLastTick = 78;
    private const float EyeToFeetOffset = 1.62f;

    /// <summary>Checks the source bytes and server setup before sending a short client trace through the normal session path and recording what the server does.</summary>
    [BedrockLiveFact]
    public async Task GenuineFramesReplayThroughBytePreservingEgress()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        string captureBin = ResolveCapturePath();
        int firstSeq = ReadIntEnv("UMPK_BEDROCK_REPLAY_FIRST_SEQ", DefaultFirstSeq);
        int frameCount = ReadIntEnv("UMPK_BEDROCK_REPLAY_FRAME_COUNT", DefaultFrameCount);
        string expectedBundleSha = Environment.GetEnvironmentVariable("UMPK_BEDROCK_REPLAY_BUNDLE_SHA256") ?? DefaultBundleSha256;
        ReplayBundle bundle = LoadVerifiedFrames(captureBin, firstSeq, frameCount, expectedBundleSha);
        List<(byte[] Raw, BedrockPlayerAuthInputPacket Decoded, ulong Tick, string Sha)> frames = bundle.Frames;
        Assert.Equal(frameCount, frames.Count);
        output.WriteLine($"Replay window: {frameCount} genuine frames from {Path.GetFileName(captureBin)} SEQ {firstSeq}..{firstSeq + frameCount - 1}, ticks {frames[0].Tick}..{frames[^1].Tick}, first eye ({frames[0].Decoded.Position.X}, {frames[0].Decoded.Position.Y}, {frames[0].Decoded.Position.Z}), bundle {bundle.BundleSha256}.");
        float feetX = frames[0].Decoded.Position.X;
        float feetY = frames[0].Decoded.Position.Y - EyeToFeetOffset;
        float feetZ = frames[0].Decoded.Position.Z;
        output.WriteLine($"Teleport feet derived from first genuine eye minus {EyeToFeetOffset} eye offset assumption: ({feetX}, {feetY}, {feetZ}).");

        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness(creativeMode: true);
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}, config {harness.ConfigSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", "1");
        try
        {
            await using var actorClient = CreateClient(harness.Port, ActorName);
            await using BedrockClientSession actor = await actorClient.ConnectAsync(ct);
            Assert.True((await actor.WaitUntilReadyAsync(ct)).IsReady);

            await using var observerClient = CreateClient(harness.Port, ObserverName);
            await using BedrockClientSession observer = await observerClient.ConnectAsync(ct);
            Assert.True((await observer.WaitUntilReadyAsync(ct)).IsReady);

            BedrockActorSnapshot actorSeenByObserver = await WaitForPlayerAsync(observer, ActorName, ct);
            ulong actorRuntimeId = actorSeenByObserver.Handle.RuntimeEntityId;
            Assert.NotEqual(0UL, actorRuntimeId);
            output.WriteLine($"Observer sees {ActorName} as runtime {actorRuntimeId}.");

            GroundBox ground = ComputeGroundBox(frames, feetY);
            int chunkLoadBefore = harness.Output.Count;
            await harness.SendCommandAsync($"teleport {ActorName} {feetX - 2} {feetY + 4} {feetZ - 2}", ct);
            Assert.True(await WaitForServerLineAsync(harness, chunkLoadBefore, "Teleported", ActorName, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the staging teleport that loads the replay ground chunks.");
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            await PrepareGroundAsync(harness, ground, ct);

            observer.ResetMotionCapture();
            actor.ResetMotionCapture();

            int teleportBefore = harness.Output.Count;
            await harness.SendCommandAsync($"teleport {ActorName} {feetX} {feetY} {feetZ}", ct);
            Assert.True(await WaitForServerLineAsync(harness, teleportBefore, "Teleported", ActorName, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the actor teleport.");
            int observerTeleportBefore = harness.Output.Count;
            await harness.SendCommandAsync($"teleport {ObserverName} {feetX + 4} {feetY} {feetZ + 4}", ct);
            Assert.True(await WaitForServerLineAsync(harness, observerTeleportBefore, "Teleported", ObserverName, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the observer teleport.");
            BedrockMovePlayerPacket teleportRelay = await WaitForObserverMoveAsync(observer, actorRuntimeId, ct);
            output.WriteLine($"Positive control: observer relayed teleport to ({teleportRelay.Position.X}, {teleportRelay.Position.Y}, {teleportRelay.Position.Z}) mode {(byte)teleportRelay.Mode} tick {teleportRelay.Tick}.");
            Assert.InRange(Math.Abs(teleportRelay.Position.X - frames[0].Decoded.Position.X), 0f, 0.05f);
            Assert.InRange(Math.Abs(teleportRelay.Position.Y - frames[0].Decoded.Position.Y), 0f, 0.1f);
            Assert.InRange(Math.Abs(teleportRelay.Position.Z - frames[0].Decoded.Position.Z), 0f, 0.05f);

            int movesAfterTeleport = CountObserverMoves(observer, actorRuntimeId);
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            int movesAfterQuiet = CountObserverMoves(observer, actorRuntimeId);
            Assert.Equal(movesAfterTeleport, movesAfterQuiet);
            output.WriteLine($"Negative control: no observer relay for runtime {actorRuntimeId} during 3 s without sends.");

            int movesBeforeProbe = CountObserverMoves(observer, actorRuntimeId);
            int motionsBeforeProbe = observer.CapturedSetEntityMotions.Count;
            int predictionsBeforeProbe = observer.CapturedMovePredictions.Count;
            var sentPayloads = new List<byte[]>(frames.Count);
            foreach ((byte[] raw, BedrockPlayerAuthInputPacket decoded, ulong tick, string _) in frames)
            {
                BedrockPlayerAuthInputSubmitResult submit = await actor.SubmitPlayerAuthInputAsync(decoded, ct);
                Assert.Equal(tick, submit.Tick);
                Assert.Equal(raw, submit.OutboundPayload);
                sentPayloads.Add(submit.OutboundPayload);
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

            IReadOnlyList<BedrockMovePlayerPacket> freshMoves = observer.CapturedMovePlayers.Where(packet => packet.RuntimeEntityId == actorRuntimeId).Skip(movesBeforeProbe).ToArray();
            double maxDisplacement = 0;
            foreach (BedrockMovePlayerPacket move in freshMoves)
            {
                double displacement = Math.Sqrt(Math.Pow(move.Position.X - teleportRelay.Position.X, 2) + Math.Pow(move.Position.Y - teleportRelay.Position.Y, 2) + Math.Pow(move.Position.Z - teleportRelay.Position.Z, 2));
                maxDisplacement = Math.Max(maxDisplacement, displacement);
            }

            ulong[] freshMoveTicks = freshMoves.Select(static packet => packet.Tick).ToArray();
            ulong[] freshMotionTicks = observer.CapturedSetEntityMotions.Skip(motionsBeforeProbe).Select(static packet => packet.Tick).ToArray();
            ulong[] freshPredictionTicks = observer.CapturedMovePredictions.Skip(predictionsBeforeProbe).Select(static packet => packet.Tick).ToArray();
            ulong firstTick = frames[0].Tick;
            ulong lastTick = frames[^1].Tick;
            bool tickEcho = freshMoveTicks.Any(tick => tick >= firstTick && tick <= lastTick) || freshMotionTicks.Any(tick => tick >= firstTick && tick <= lastTick) || freshPredictionTicks.Any(tick => tick >= firstTick && tick <= lastTick);
            output.WriteLine($"Replay: {freshMoves.Count} fresh observer relays (packet 19) for runtime {actorRuntimeId}, {freshMotionTicks.Length} fresh packet 40 ticks [{string.Join(",", freshMotionTicks)}], {freshPredictionTicks.Length} fresh packet 161 ticks [{string.Join(",", freshPredictionTicks)}], max displacement {maxDisplacement:F4} blocks, tick echo {tickEcho}, ticks {string.Join(",", freshMoveTicks)}.");

            await WriteEvidenceAsync(harness, actor, actorRuntimeId, teleportRelay, sentPayloads, bundle, ground, feetX, feetY, feetZ, freshMoves, freshMoveTicks, freshMotionTicks, freshPredictionTicks, maxDisplacement, tickEcho, ct);

            Assert.True(actor.Snapshot.IsReady);
            Assert.True(observer.Snapshot.IsReady);
            Assert.Equal(0, actor.RejectedSendAttempts);
            Assert.Equal(0, observer.RejectedSendAttempts);
            await actor.DisconnectAsync(ct);
            await observer.DisconnectAsync(ct);
            await actorClient.DisposeAsync();
            await observerClient.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", previousCapture);
        }
    }

    /// <summary>Resolves the explicit genuine capture bundle path from UMPK_BEDROCK_REPLAY_CAPTURE or the default worktree bundle location.</summary>
    private static string ResolveCapturePath()
    {
        string? explicitPath = Environment.GetEnvironmentVariable("UMPK_BEDROCK_REPLAY_CAPTURE");
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath;
        string probe = AppContext.BaseDirectory;
        for (int level = 0; level < 12; level++)
        {
            string candidate = Path.Combine(probe, DefaultCaptureRelativePath);
            if (File.Exists(candidate))
                return candidate;
            string? parent = Directory.GetParent(probe)?.FullName;
            if (parent is null || parent == probe)
                break;
            probe = parent;
        }
        string fallback = Path.Combine(Directory.GetCurrentDirectory(), DefaultCaptureRelativePath);
        if (File.Exists(fallback))
            return fallback;
        throw new FileNotFoundException($"The genuine capture bundle was not found. Set UMPK_BEDROCK_REPLAY_CAPTURE to the explicit genuine-144.bin path (default {DefaultCaptureRelativePath}).");
    }

    /// <summary>Reads a bounded integer environment override or returns the supplied default value.</summary>
    private static int ReadIntEnv(string name, int defaultValue)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;
        if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value))
            throw new InvalidOperationException($"Environment {name} value '{raw}' is not a valid integer.");
        return value;
    }

    /// <summary>Loads and verifies the selected genuine window with file-hash provenance, per-frame jsonl hashes, strict decode round trips, and straight-walk shape asserts.</summary>
    private ReplayBundle LoadVerifiedFrames(string captureBin, int firstSeq, int frameCount, string expectedBundleSha)
    {
        Assert.True(File.Exists(captureBin), $"The genuine capture bundle was not found at {captureBin}.");
        byte[] data = File.ReadAllBytes(captureBin);
        string bundleSha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        Assert.Equal(expectedBundleSha.ToLowerInvariant(), bundleSha);
        var records = new List<byte[]>();
        int pos = 0;
        while (pos < data.Length)
        {
            Assert.True(pos + 4 <= data.Length, "The capture bundle ends with a truncated length prefix.");
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
            Assert.True(pos + 4 + len <= data.Length, "The capture bundle ends with a truncated frame.");
            records.Add(data[(pos + 4)..(pos + 4 + len)]);
            pos += 4 + len;
        }
        Assert.True(pos == data.Length, "The capture bundle has trailing bytes after the last frame.");
        Assert.InRange(firstSeq, 1, records.Count);
        Assert.InRange(frameCount, 1, records.Count);
        long lastSeq = (long)firstSeq + frameCount - 1;
        Assert.True(lastSeq <= records.Count, $"The requested SEQ window {firstSeq}..{lastSeq} exceeds the bundle with {records.Count} frames.");

        string jsonlPath = Path.ChangeExtension(captureBin, ".jsonl");
        Assert.True(File.Exists(jsonlPath), $"The matching jsonl provenance file was not found at {jsonlPath}.");
        List<string> metaLines = File.ReadAllLines(jsonlPath).ToList();
        Assert.True(metaLines.Count == records.Count, $"The jsonl has {metaLines.Count} lines but the bin has {records.Count} frames.");

        var frames = new List<(byte[] Raw, BedrockPlayerAuthInputPacket Decoded, ulong Tick, string Sha)>();
        for (int seq = firstSeq; seq < firstSeq + frameCount; seq++)
        {
            int index = seq - 1;
            byte[] record = records[index];
            Assert.True(record.Length >= 2 && record[0] == 0x90 && record[1] == 0x01, $"SEQ {seq} does not carry the packet 144 header 90 01.");
            string line = metaLines[index];
            Assert.Equal(seq, ExtractInt(line, "seq"));
            Assert.Equal(record.Length, ExtractInt(line, "len"));
            Assert.Equal(2, ExtractInt(line, "headerLen"));
            Assert.Contains("\"p144\":true", line, StringComparison.Ordinal);
            string expectedSha = ExtractString(line, "sha256");
            string actualSha = Convert.ToHexString(SHA256.HashData(record)).ToLowerInvariant();
            Assert.Equal(expectedSha.ToLowerInvariant(), actualSha);
            byte[] payload = record[2..];
            BedrockPlayerAuthInputPacket decoded = BedrockPlayerAuthInputCodecs.Decode(payload);
            Assert.Equal(payload, BedrockPlayerAuthInputCodecs.Encode(decoded));
            frames.Add((payload, decoded, decoded.Tick, actualSha));
        }

        for (int index = 1; index < frames.Count; index++)
            Assert.True(frames[index].Tick > frames[index - 1].Tick, "The replay window ticks must be strictly increasing.");
        if (firstSeq == DefaultFirstSeq && frameCount == DefaultFrameCount)
        {
            Assert.Equal(DefaultFirstTick, frames[0].Tick);
            Assert.Equal(DefaultLastTick, frames[^1].Tick);
        }
        foreach ((byte[] _, BedrockPlayerAuthInputPacket decoded, ulong _, string _) in frames)
        {
            Assert.True(decoded.Inputs.HasValue && decoded.Inputs.Value.Length == 3 && decoded.Inputs.Value[0] == 10 && decoded.Inputs.Value[1] == 48 && decoded.Inputs.Value[2] == 50, "The selected window must carry INPUTS [10,48,50].");
            Assert.Equal(0f, decoded.MoveVector.X);
            Assert.Equal(1f, decoded.MoveVector.Z);
            Assert.Equal(0f, decoded.AnalogueMoveVector.X);
            Assert.Equal(0f, decoded.AnalogueMoveVector.Z);
            Assert.Equal(0f, decoded.RawMoveVector.X);
            Assert.Equal(1f, decoded.RawMoveVector.Z);
            Assert.True(decoded.TransactionPresent, "The selected window must carry present-but-null transaction tails.");
            Assert.Null(decoded.Transaction);
            Assert.True(decoded.ItemStackRequestPresent, "The selected window must carry present-but-null item-stack tails.");
            Assert.True(decoded.BlockActionsPresent, "The selected window must carry present-but-null block-action tails.");
            Assert.True(decoded.VehicleRotationPresent, "The selected window must carry present-but-null vehicle-rotation tails.");
            Assert.True(decoded.PredictedVehiclePresent, "The selected window must carry present-but-null predicted-vehicle tails.");
        }

        return new ReplayBundle(captureBin, jsonlPath, bundleSha, firstSeq, frames);
    }

    /// <summary>Computes an integer stone-ground and air box that encloses the selected genuine eye path with margin.</summary>
    private static GroundBox ComputeGroundBox(List<(byte[] Raw, BedrockPlayerAuthInputPacket Decoded, ulong Tick, string Sha)> frames, float feetY)
    {
        float minX = frames.Min(static frame => frame.Decoded.Position.X);
        float maxX = frames.Max(static frame => frame.Decoded.Position.X);
        float minZ = frames.Min(static frame => frame.Decoded.Position.Z);
        float maxZ = frames.Max(static frame => frame.Decoded.Position.Z);
        int groundY = (int)Math.Floor(feetY) - 1;
        return new GroundBox((int)Math.Floor(minX) - 2, groundY, (int)Math.Floor(minZ) - 2, (int)Math.Ceiling(maxX) + 2, (int)Math.Ceiling(maxZ) + 2);
    }

    /// <summary>Builds flat stone ground and clear air around the path and confirms both fills before any teleport.</summary>
    private async Task PrepareGroundAsync(BedrockServerHarness harness, GroundBox ground, CancellationToken ct)
    {
        int stoneBefore = harness.Output.Count;
        await harness.SendCommandAsync($"fill {ground.X1} {ground.GroundY} {ground.Z1} {ground.X2} {ground.GroundY} {ground.Z2} stone", ct);
        Assert.True(await WaitForServerLineAsync(harness, stoneBefore, "blocks filled", string.Empty, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the stone ground fill.");
        int airBefore = harness.Output.Count;
        await harness.SendCommandAsync($"fill {ground.X1} {ground.GroundY + 1} {ground.Z1} {ground.X2} {ground.GroundY + 3} {ground.Z2} air", ct);
        Assert.True(await WaitForServerLineAsync(harness, airBefore, "blocks filled", string.Empty, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the air clearance fill.");
        output.WriteLine($"Ground prepared: stone y={ground.GroundY} x=[{ground.X1},{ground.X2}] z=[{ground.Z1},{ground.Z2}], air y=[{ground.GroundY + 1},{ground.GroundY + 3}].");
    }

    /// <summary>Extracts a quoted string value for the named jsonl key without a JSON dependency on private capture shape.</summary>
    private static string ExtractString(string line, string key)
    {
        string marker = $"\"{key}\":\"";
        int start = line.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return line[start..line.IndexOf('"', start)];
    }

    /// <summary>Extracts an integer value for the named jsonl key without a JSON dependency on private capture shape.</summary>
    private static int ExtractInt(string line, string key)
    {
        string marker = $"\"{key}\":";
        int start = line.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        int end = start;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '-'))
            end++;
        return int.Parse(line[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Creates an offline client pinned to the isolated harness port with extended join timeouts.</summary>
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

    /// <summary>Counts observer packet-19 relays for the actor runtime identity only.</summary>
    private static int CountObserverMoves(BedrockClientSession observer, ulong runtimeId) => observer.CapturedMovePlayers.Count(packet => packet.RuntimeEntityId == runtimeId);

    /// <summary>Waits for the observer to relay any packet-19 move for the actor runtime identity.</summary>
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

    /// <summary>Waits for the named player to appear in the observer actor snapshot set.</summary>
    private static async Task<BedrockActorSnapshot> WaitForPlayerAsync(BedrockClientSession session, string username, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockActorSnapshot? match = session.ActorSnapshots.FirstOrDefault(actor => actor.DisplayName == username);
            if (match is not null)
                return match;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"The session did not observe player '{username}'.");
    }

    /// <summary>Waits for a fresh BDS console line containing the marker and optional player name.</summary>
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

    /// <summary>Writes replay evidence outside Git to both the harness directory and a temp replay root.</summary>
    private async Task WriteEvidenceAsync(BedrockServerHarness harness, BedrockClientSession actor, ulong actorRuntimeId, BedrockMovePlayerPacket teleportRelay, List<byte[]> sentPayloads, ReplayBundle bundle, GroundBox ground, float feetX, float feetY, float feetZ, IReadOnlyList<BedrockMovePlayerPacket> freshMoves, ulong[] freshMoveTicks, ulong[] freshMotionTicks, ulong[] freshPredictionTicks, double maxDisplacement, bool tickEcho, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-replay", Path.GetFileName(harness.EvidenceDirectory));
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
                strictMovement = harness.EffectiveMovementStrict,
            },
            protocol = 2169,
            actor = ActorName,
            observer = ObserverName,
            actorRuntimeId,
            capture = new { file = Path.GetFileName(bundle.BinPath), fullPathSha = bundle.BundleSha256, expectedBundleSha256 = (Environment.GetEnvironmentVariable("UMPK_BEDROCK_REPLAY_BUNDLE_SHA256") ?? DefaultBundleSha256).ToLowerInvariant(), firstSeq = bundle.FirstSeq, frameCount = bundle.Frames.Count, jsonl = Path.GetFileName(bundle.JsonlPath) },
            replayTicks = new { first = bundle.Frames[0].Tick, last = bundle.Frames[^1].Tick },
            firstEye = new { x = bundle.Frames[0].Decoded.Position.X, y = bundle.Frames[0].Decoded.Position.Y, z = bundle.Frames[0].Decoded.Position.Z },
            lastEye = new { x = bundle.Frames[^1].Decoded.Position.X, y = bundle.Frames[^1].Decoded.Position.Y, z = bundle.Frames[^1].Decoded.Position.Z },
            eyeToFeetOffsetAssumption = EyeToFeetOffset,
            teleportFeet = new { x = feetX, y = feetY, z = feetZ },
            ground = new { x1 = ground.X1, groundY = ground.GroundY, z1 = ground.Z1, x2 = ground.X2, airTop = ground.GroundY + 3, z2 = ground.Z2 },
            frameShas = bundle.Frames.Select(static frame => frame.Sha).ToArray(),
            sentPayloadSha256 = Convert.ToHexString(SHA256.HashData(sentPayloads.SelectMany(static payload => payload).ToArray())).ToLowerInvariant(),
            teleportRelay = new { x = teleportRelay.Position.X, y = teleportRelay.Position.Y, z = teleportRelay.Position.Z, mode = (byte)teleportRelay.Mode, tick = teleportRelay.Tick },
            freshObserverMoves = freshMoves.Count,
            freshMoveTicks,
            freshMotionTicks,
            freshPredictionTicks,
            maxDisplacementBlocks = maxDisplacement,
            tickEcho,
            actorRejectedSends = actor.RejectedSendAttempts,
            actorReady = actor.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(Path.Combine(captureRoot, "replay-metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(harness.EvidenceDirectory, "replay-proof.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"Replay evidence written to {captureRoot}.");
    }

    /// <summary>Carries the verified bundle identity alongside the selected decoded window.</summary>
    private sealed record ReplayBundle(string BinPath, string JsonlPath, string BundleSha256, int FirstSeq, List<(byte[] Raw, BedrockPlayerAuthInputPacket Decoded, ulong Tick, string Sha)> Frames);

    /// <summary>Carries integer ground-box coordinates for the stone pad and cleared air column.</summary>
    private sealed record GroundBox(int X1, int GroundY, int Z1, int X2, int Z2);
}
