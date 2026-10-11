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
public sealed class PlayerAuthInputObserverLiveTests(ITestOutputHelper output)
{
    private const string ActorName = "UmpkActorBot";
    private const string ObserverName = "UmpkObserverBot";

    [BedrockLiveFact]
    public async Task ObserverSeesTeleportPositiveControlAndNoWalkingRelay()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}, config {harness.ConfigSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", "1");
        try
        {
            await using var actorClient = CreateClient(harness.Port, ActorName);
            await using BedrockClientSession actor = await actorClient.ConnectAsync(ct);
            BedrockSessionSnapshot actorReady = await actor.WaitUntilReadyAsync(ct);
            Assert.True(actorReady.IsReady);

            await using var observerClient = CreateClient(harness.Port, ObserverName);
            await using BedrockClientSession observer = await observerClient.ConnectAsync(ct);
            BedrockSessionSnapshot observerReady = await observer.WaitUntilReadyAsync(ct);
            Assert.True(observerReady.IsReady);

            BedrockActorSnapshot actorSeenByObserver = await WaitForPlayerAsync(observer, ActorName, ct);
            ulong actorRuntimeId = actorSeenByObserver.Handle.RuntimeEntityId;
            Assert.NotEqual(0UL, actorRuntimeId);
            output.WriteLine($"Observer sees {ActorName} as runtime {actorRuntimeId}, unique {actorSeenByObserver.Handle.UniqueEntityId}.");

            await PrepareSharedPadAsync(harness, ct);

            observer.ResetMotionCapture();
            actor.ResetMotionCapture();

            int teleportBefore = harness.Output.Count;
            await harness.SendCommandAsync($"teleport {ActorName} 8 90 8", ct);
            Assert.True(await WaitForServerLineAsync(harness, teleportBefore, "Teleported", ActorName, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the actor teleport.");
            BedrockMovePlayerPacket teleportRelay = await WaitForObserverMoveAsync(observer, actorRuntimeId, ct);
            byte[] teleportPayload = observer.CapturedMovePlayerPayloads.Last(payload => BedrockActorCodecs.DecodeMovePlayer(payload).RuntimeEntityId == actorRuntimeId);
            Assert.Equal(teleportPayload, BedrockActorCodecs.Encode(BedrockActorCodecs.DecodeMovePlayer(teleportPayload)));
            Assert.InRange(Math.Abs(teleportRelay.Position.X - 8.5f), 0f, 0.05f);
            Assert.InRange(Math.Abs(teleportRelay.Position.Z - 8.5f), 0f, 0.05f);
            Assert.InRange(Math.Abs(teleportRelay.Position.Y - 91.62f), 0f, 0.05f);
            output.WriteLine($"Positive control: observer relayed teleport to ({teleportRelay.Position.X}, {teleportRelay.Position.Y}, {teleportRelay.Position.Z}) mode {(byte)teleportRelay.Mode} tick {teleportRelay.Tick}.");

            BedrockVector3 actorEye = await ReadActorEyeAsync(actor, ct);
            int observerMovesAfterTeleport = CountObserverMoves(observer, actorRuntimeId);
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            int observerMovesAfterQuiet = CountObserverMoves(observer, actorRuntimeId);
            Assert.Equal(observerMovesAfterTeleport, observerMovesAfterQuiet);
            output.WriteLine($"Negative control: no observer relay for runtime {actorRuntimeId} during 3 s without sends.");

            int movesBeforeProbe = CountObserverMoves(observer, actorRuntimeId);
            int motionsBeforeProbe = observer.CapturedSetEntityMotions.Count;
            int predictionsBeforeProbe = observer.CapturedMovePredictions.Count;

            ulong tick = 400;
            var sentPayloads = new List<byte[]>(20);
            for (int index = 0; index < 20; index++)
            {
                BedrockPlayerAuthInputPacket input = Forward(actorEye, tick);
                BedrockPlayerAuthInputSubmitResult submit = await actor.SubmitPlayerAuthInputAsync(input, ct);
                Assert.Equal(tick, submit.Tick);
                Assert.Equal(submit.OutboundPayload, BedrockPlayerAuthInputCodecs.Encode(BedrockPlayerAuthInputCodecs.Decode(submit.OutboundPayload)));
                sentPayloads.Add(submit.OutboundPayload);
                tick++;
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

            IReadOnlyList<BedrockMovePlayerPacket> freshMoves = observer.CapturedMovePlayers.Where(packet => packet.RuntimeEntityId == actorRuntimeId).Skip(movesBeforeProbe).ToArray();
            double maxDisplacement = 0;
            foreach (BedrockMovePlayerPacket move in freshMoves)
            {
                double displacement = Math.Sqrt(Math.Pow(move.Position.X - teleportRelay.Position.X, 2) + Math.Pow(move.Position.Y - teleportRelay.Position.Y, 2) + Math.Pow(move.Position.Z - teleportRelay.Position.Z, 2));
                maxDisplacement = Math.Max(maxDisplacement, displacement);
                byte[] payload = observer.CapturedMovePlayerPayloads.First(candidate => BedrockActorCodecs.DecodeMovePlayer(candidate).Equals(move));
                Assert.Equal(payload, BedrockActorCodecs.Encode(BedrockActorCodecs.DecodeMovePlayer(payload)));
            }

            ulong[] freshMoveTicks = freshMoves.Select(static packet => packet.Tick).ToArray();
            ulong[] freshMotionTicks = observer.CapturedSetEntityMotions.Skip(motionsBeforeProbe).Select(static packet => packet.Tick).ToArray();
            ulong[] freshPredictionTicks = observer.CapturedMovePredictions.Skip(predictionsBeforeProbe).Select(static packet => packet.Tick).ToArray();
            bool tickEcho = freshMoveTicks.Any(tick => tick is >= 400 and <= 419) || freshMotionTicks.Any(tick => tick is >= 400 and <= 419) || freshPredictionTicks.Any(tick => tick is >= 400 and <= 419);
            output.WriteLine($"Probe: {freshMoves.Count} fresh observer relays for runtime {actorRuntimeId}, max displacement {maxDisplacement:F4} blocks, tick echo {tickEcho}.");
            Assert.False(tickEcho);
            Assert.InRange(maxDisplacement, 0, 0.1);

            await WriteEvidenceAsync(harness, observer, actor, actorRuntimeId, teleportRelay, teleportPayload, sentPayloads, freshMoves, freshMoveTicks, maxDisplacement, tickEcho, ct);

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

    private static BedrockPlayerAuthInputPacket Forward(BedrockVector3 eye, ulong tick) =>
        new(0f, 0f, eye, new BedrockVector2(0f, 1f), 0f, [10], 1, 0, 2, new BedrockVector2(0f, 0f), tick, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));

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
        int teleportActorBefore = harness.Output.Count;
        await harness.SendCommandAsync($"teleport {ActorName} 8 90 8", ct);
        Assert.True(await WaitForServerLineAsync(harness, teleportActorBefore, "Teleported", ActorName, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the actor pad teleport.");
        int teleportObserverBefore = harness.Output.Count;
        await harness.SendCommandAsync($"teleport {ObserverName} 12 90 8", ct);
        Assert.True(await WaitForServerLineAsync(harness, teleportObserverBefore, "Teleported", ObserverName, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the observer pad teleport.");
        int fillBefore = harness.Output.Count;
        await harness.SendCommandAsync("fill 0 89 0 15 89 15 stone", ct);
        await WaitForServerLineAsync(harness, fillBefore, "blocks filled", string.Empty, TimeSpan.FromSeconds(20), ct);
        int airBefore = harness.Output.Count;
        await harness.SendCommandAsync("fill 0 90 0 15 92 15 air", ct);
        await WaitForServerLineAsync(harness, airBefore, "blocks filled", string.Empty, TimeSpan.FromSeconds(20), ct);
        int actorModeBefore = harness.Output.Count;
        await harness.SendCommandAsync($"gamemode survival {ActorName}", ct);
        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        await harness.SendCommandAsync($"gamemode survival {ObserverName}", ct);
        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        await harness.SendCommandAsync($"effect {ActorName} clear", ct);
        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        output.WriteLine($"Shared pad prepared. Mode tail: {string.Join(" | ", harness.Output.Skip(actorModeBefore).TakeLast(2))}.");
    }

    private async Task<BedrockVector3> ReadActorEyeAsync(BedrockClientSession actor, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (actor.CapturedMovePlayers.Count > 0)
            {
                return actor.CapturedMovePlayers[^1].Position;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return new BedrockVector3(8.5f, 91.62001f, 8.5f);
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
            {
                return match;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"The observer did not relay a move for runtime {runtimeId}.");
    }

    private static async Task<BedrockActorSnapshot> WaitForPlayerAsync(BedrockClientSession session, string username, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockActorSnapshot? match = session.ActorSnapshots.FirstOrDefault(actor => actor.DisplayName == username);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"The session did not observe player '{username}'.");
    }

    private static async Task<bool> WaitForServerLineAsync(BedrockServerHarness harness, int outputBefore, string marker, string player, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            string[] fresh = harness.Output.Skip(outputBefore).ToArray();
            if (fresh.Any(line => line.Contains(marker, StringComparison.Ordinal) && (player.Length == 0 || line.Contains(player, StringComparison.Ordinal))))
            {
                return true;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return harness.Output.Skip(outputBefore).Any(line => line.Contains(marker, StringComparison.Ordinal) && (player.Length == 0 || line.Contains(player, StringComparison.Ordinal)));
    }

    private async Task WriteEvidenceAsync(BedrockServerHarness harness, BedrockClientSession observer, BedrockClientSession actor, ulong actorRuntimeId, BedrockMovePlayerPacket teleportRelay, byte[] teleportPayload, List<byte[]> sentPayloads, IReadOnlyList<BedrockMovePlayerPacket> freshMoves, ulong[] freshMoveTicks, double maxDisplacement, bool tickEcho, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-observer", Path.GetFileName(harness.EvidenceDirectory));
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
                strictMovement = harness.EffectiveMovementStrict,
            },
            protocol = 2169,
            actor = ActorName,
            observer = ObserverName,
            actorRuntimeId,
            teleportRelay = new { x = teleportRelay.Position.X, y = teleportRelay.Position.Y, z = teleportRelay.Position.Z, mode = (byte)teleportRelay.Mode, tick = teleportRelay.Tick },
            teleportPayloadSha256 = Convert.ToHexString(SHA256.HashData(teleportPayload)).ToLowerInvariant(),
            sentTicks = new { first = 400UL, last = 419UL, count = sentPayloads.Count },
            sentPayloadSha256 = Convert.ToHexString(SHA256.HashData(sentPayloads.SelectMany(static payload => payload).ToArray())).ToLowerInvariant(),
            freshObserverMoves = freshMoves.Count,
            freshMoveTicks,
            maxDisplacementBlocks = maxDisplacement,
            tickEcho,
            actorRejectedSends = actor.RejectedSendAttempts,
            observerRejectedSends = observer.RejectedSendAttempts,
            actorReady = actor.Snapshot.IsReady,
            observerReady = observer.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(Path.Combine(captureRoot, "observer-metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(harness.EvidenceDirectory, "observer-proof.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"Observer evidence written to {captureRoot}.");
    }
}
