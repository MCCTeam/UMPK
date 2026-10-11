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

[Collection("Bedrock motion capture")]
public sealed class PlayerAuthInputLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkAuthBot";

    [BedrockLiveFact]
    public async Task IdleAdmissionProvesServerSurvival()
    {
        await RunProbeAsync(strictMovement: false, "idle", static async (session, harness, ct, report) =>
        {
            session.ResetMotionCapture();
            ulong tick = 100;
            for (int index = 0; index < 10; index++)
            {
                BedrockPlayerAuthInputPacket input = Idle(report.Eye, tick, [], new BedrockVector3(0f, 0f, 0f));
                BedrockPlayerAuthInputSubmitResult submit = await session.SubmitPlayerAuthInputAsync(input, ct);
                Assert.Equal(tick, submit.Tick);
                Assert.Equal(submit.OutboundPayload, BedrockPlayerAuthInputCodecs.Encode(BedrockPlayerAuthInputCodecs.Decode(submit.OutboundPayload)));
                tick++;
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
            }

            report.Sends = 10;
            report.LastTick = tick - 1;
        });
    }

    [BedrockLiveFact]
    public async Task ImpossibleDeltaNegativeControlDefault()
    {
        await RunProbeAsync(strictMovement: false, "impossible-delta", static async (session, harness, ct, report) =>
        {
            session.ResetMotionCapture();
            ulong tick = 200;
            BedrockPlayerAuthInputPacket idle = Idle(report.Eye, tick, [], new BedrockVector3(0f, 0f, 0f));
            await session.SubmitPlayerAuthInputAsync(idle, ct);
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
            tick++;
            BedrockPlayerAuthInputPacket impossible = Idle(report.Eye, tick, [], new BedrockVector3(0f, 5f, 0f));
            BedrockPlayerAuthInputSubmitResult submit = await session.SubmitPlayerAuthInputAsync(impossible, ct);
            Assert.Equal(tick, submit.Tick);
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            report.Sends = 2;
            report.LastTick = tick;
        });
    }

    [BedrockLiveFact]
    public async Task ImpossibleDeltaNegativeControlStrict()
    {
        await RunProbeAsync(strictMovement: true, "impossible-delta-strict", static async (session, harness, ct, report) =>
        {
            session.ResetMotionCapture();
            ulong tick = 300;
            BedrockPlayerAuthInputPacket idle = Idle(report.Eye, tick, [], new BedrockVector3(0f, 0f, 0f));
            await session.SubmitPlayerAuthInputAsync(idle, ct);
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
            tick++;
            BedrockPlayerAuthInputPacket impossible = Idle(report.Eye, tick, [], new BedrockVector3(0f, 5f, 0f));
            BedrockPlayerAuthInputSubmitResult submit = await session.SubmitPlayerAuthInputAsync(impossible, ct);
            Assert.Equal(tick, submit.Tick);
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            report.Sends = 2;
            report.LastTick = tick;
        });
    }

    [BedrockLiveFact]
    public async Task ForwardWalkProbe()
    {
        await RunProbeAsync(strictMovement: false, "forward", static async (session, harness, ct, report) =>
        {
            session.ResetMotionCapture();
            ulong tick = 400;
            BedrockVector3 position = report.Eye;
            for (int index = 0; index < 20; index++)
            {
                BedrockPlayerAuthInputPacket input = Forward(position, tick);
                await session.SubmitPlayerAuthInputAsync(input, ct);
                tick++;
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
            }

            report.Sends = 20;
            report.LastTick = tick - 1;
        });
    }

    [BedrockLiveFact]
    public async Task FallingProbe()
    {
        await RunProbeAsync(strictMovement: false, "falling", static async (session, harness, ct, report) =>
        {
            session.ResetMotionCapture();
            ulong tick = 500;
            BedrockVector3 position = report.Eye with { Y = report.Eye.Y + 10f };
            BedrockVector3 previous = position;
            for (int index = 0; index < 20; index++)
            {
                BedrockVector3 delta = new(position.X - previous.X, position.Y - previous.Y, position.Z - previous.Z);
                previous = position;
                BedrockPlayerAuthInputPacket input = Idle(position, tick, [], delta);
                await session.SubmitPlayerAuthInputAsync(input, ct);
                tick++;
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
            }

            report.Sends = 20;
            report.LastTick = tick - 1;
        });
    }

    [BedrockLiveFact]
    public async Task JumpProbe()
    {
        await RunProbeAsync(strictMovement: false, "jump", static async (session, harness, ct, report) =>
        {
            session.ResetMotionCapture();
            ulong tick = 600;
            for (int index = 0; index < 20; index++)
            {
                IReadOnlyList<int> inputs = index == 0 ? [3, 61] : [61];
                BedrockPlayerAuthInputPacket input = Idle(report.Eye, tick, inputs, new BedrockVector3(0f, 0f, 0f));
                await session.SubmitPlayerAuthInputAsync(input, ct);
                tick++;
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
            }

            report.Sends = 20;
            report.LastTick = tick - 1;
        });
    }

    [BedrockLiveFact]
    public async Task SmallTickIdleDefault()
    {
        await RunProbeAsync(strictMovement: false, "small-tick-idle", static async (session, harness, ct, report) =>
        {
            session.ResetMotionCapture();
            ulong tick = 0;
            for (int index = 0; index < 10; index++)
            {
                BedrockPlayerAuthInputPacket input = Idle(report.Eye, tick, [], new BedrockVector3(0f, 0f, 0f));
                BedrockPlayerAuthInputSubmitResult submit = await session.SubmitPlayerAuthInputAsync(input, ct);
                Assert.Equal(tick, submit.Tick);
                Assert.Equal(submit.OutboundPayload, BedrockPlayerAuthInputCodecs.Encode(BedrockPlayerAuthInputCodecs.Decode(submit.OutboundPayload)));
                tick++;
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
            }

            report.Sends = 10;
            report.LastTick = tick - 1;
        });
    }

    [BedrockLiveFact]
    public async Task SmallTickImpossibleDeltaStrict()
    {
        await RunProbeAsync(strictMovement: true, "small-tick-impossible-strict", static async (session, harness, ct, report) =>
        {
            session.ResetMotionCapture();
            ulong tick = 0;
            BedrockPlayerAuthInputPacket idle = Idle(report.Eye, tick, [], new BedrockVector3(0f, 0f, 0f));
            await session.SubmitPlayerAuthInputAsync(idle, ct);
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
            tick++;
            BedrockPlayerAuthInputPacket impossible = Idle(report.Eye, tick, [], new BedrockVector3(0f, 5f, 0f));
            BedrockPlayerAuthInputSubmitResult submit = await session.SubmitPlayerAuthInputAsync(impossible, ct);
            Assert.Equal(tick, submit.Tick);
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            report.Sends = 2;
            report.LastTick = tick;
        });
    }

    [BedrockLiveFact]
    public async Task PositionOffsetCorrectionStrict()
    {
        await RunProbeAsync(strictMovement: true, "position-offset-strict", static async (session, harness, ct, report) =>
        {
            report.ExcludeTickZeroFromEcho = true;
            session.ResetMotionCapture();
            ulong tick = 700;
            BedrockPlayerAuthInputPacket anchor = Idle(report.Eye, tick, [], new BedrockVector3(0f, 0f, 0f));
            await session.SubmitPlayerAuthInputAsync(anchor, ct);
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
            tick++;
            BedrockVector3 offset = new(report.Eye.X, report.Eye.Y + 2f, report.Eye.Z);
            BedrockPlayerAuthInputPacket lied = Idle(offset, tick, [], new BedrockVector3(0f, 2f, 0f));
            BedrockPlayerAuthInputSubmitResult submit = await session.SubmitPlayerAuthInputAsync(lied, ct);
            Assert.Equal(tick, submit.Tick);
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            report.Sends = 2;
            report.LastTick = tick;
        });
    }

    [BedrockLiveFact]
    public async Task ServerTickAnchoredIdleProbe()
    {
        await RunProbeAsync(strictMovement: false, "server-tick-anchored", static async (session, harness, ct, report) =>
        {
            report.ExcludeTickZeroFromEcho = true;
            byte[]? remainder = session.CapturedStartGameRemainder;
            Assert.NotNull(remainder);
            BedrockStartGameTrailer trailer = BedrockStartGameTrailerCodecs.DecodeTrailer(remainder);
            Assert.Equal(remainder, BedrockStartGameTrailerCodecs.EncodeTrailer(trailer));
            report.StartGameCurrentTick = trailer.CurrentTick;
            report.StartGameRewindHistorySize = trailer.RewindHistorySize;
            report.StartGameRemainderSha256 = Convert.ToHexString(SHA256.HashData(remainder)).ToLowerInvariant();
            ulong startTick = trailer.CurrentTick >= 0 ? (ulong)trailer.CurrentTick : 0UL;
            report.TickConversion = trailer.CurrentTick >= 0 ? "direct-cast-world-tick" : "negative-to-zero";
            session.ResetMotionCapture();
            ulong tick = startTick;
            for (int index = 0; index < 10; index++)
            {
                BedrockPlayerAuthInputPacket input = Idle(report.Eye, tick, [], new BedrockVector3(0f, 0f, 0f));
                BedrockPlayerAuthInputSubmitResult submit = await session.SubmitPlayerAuthInputAsync(input, ct);
                Assert.Equal(tick, submit.Tick);
                Assert.Equal(submit.OutboundPayload, BedrockPlayerAuthInputCodecs.Encode(BedrockPlayerAuthInputCodecs.Decode(submit.OutboundPayload)));
                tick++;
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
            }

            report.Sends = 10;
            report.LastTick = tick - 1;
        }, captureTerrain: true);
    }

    private static BedrockPlayerAuthInputPacket Idle(BedrockVector3 eye, ulong tick, IReadOnlyList<int> inputs, BedrockVector3 delta) =>
        new(0f, 0f, eye, new BedrockVector2(0f, 0f), 0f, inputs, 1, 0, 2, new BedrockVector2(0f, 0f), tick, delta, new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));

    private static BedrockPlayerAuthInputPacket Forward(BedrockVector3 eye, ulong tick) =>
        new(0f, 0f, eye, new BedrockVector2(0f, 1f), 0f, [10], 1, 0, 2, new BedrockVector2(0f, 0f), tick, new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f), new BedrockVector3(0f, 0f, 0f), new BedrockVector2(0f, 0f));

    private async Task RunProbeAsync(bool strictMovement, string probe, Func<BedrockClientSession, BedrockServerHarness, CancellationToken, ProbeReport, Task> drive, bool captureTerrain = false)
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        Exception? lastFailure = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            BedrockClientSession? session = null;
            BedrockServerHarness? harness = null;
            var report = new ProbeReport { Probe = probe, StrictMovement = strictMovement };
            try
            {
                harness = new BedrockServerHarness(strictMovement);
                await harness.StartAsync(ct);
                output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}, strict {harness.EffectiveMovementStrict}, config {harness.ConfigSha256}, attempt {attempt}).");
                string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
                Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", "1");
                string? previousTerrainCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_TERRAIN");
                if (captureTerrain)
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
                    await using BedrockClientSession owned = await client.ConnectAsync(ct);
                    session = owned;
                    BedrockSessionSnapshot ready = await session.WaitUntilReadyAsync(ct);
                    Assert.True(ready.IsReady);
                    await PrepareFlatPadAsync(harness, session, ct);
                    report.Eye = await ReadEyeAsync(session, ct);
                    await drive(session, harness, ct, report);
                    CapturePreTeleportObservations(session, report);
                    await VerifySurvivalAsync(harness, session, ct, report);
                    CaptureObservations(session, report);
                    await WriteEvidenceAsync(harness, session, report, ct);
                    Assert.True(session.Snapshot.IsReady);
                    Assert.False(session.Completion.IsCompleted);
                    Assert.Equal(0, session.RejectedSendAttempts);
                    await session.DisconnectAsync(ct);
                    await client.DisposeAsync();
                    if (session is not null)
                        await session.DisposeAsync().ConfigureAwait(false);
                    if (harness is not null)
                        await harness.DisposeAsync().ConfigureAwait(false);
                    return;
                }
                finally
                {
                    Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", previousCapture);
                    if (captureTerrain)
                        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_TERRAIN", previousTerrainCapture);
                }
            }
            catch (Exception failure)
            {
                if (harness is not null && session is not null)
                    await WriteFailureAsync(harness, session, report, failure, ct).ConfigureAwait(false);
                if (session is not null)
                    await session.DisposeAsync().ConfigureAwait(false);
                if (harness is not null)
                    await harness.DisposeAsync().ConfigureAwait(false);
                bool is110 = failure.ToString().Contains("Packet 110", StringComparison.Ordinal);
                if (is110 && attempt < 2)
                {
                    output.WriteLine($"Known untolerated pre-ready packet 110 terminated attempt {attempt}; retrying once more.");
                    lastFailure = failure;
                    continue;
                }

                throw;
            }
        }

        throw lastFailure ?? new InvalidOperationException("The probe failed without a recorded error.");
    }

    private async Task PrepareFlatPadAsync(BedrockServerHarness harness, BedrockClientSession session, CancellationToken ct)
    {
        int outputBefore = harness.Output.Count;
        await harness.SendCommandAsync($"teleport {PlayerName} 8 90 8", ct);
        Assert.True(await WaitForServerLineAsync(harness, outputBefore, "Teleported", PlayerName, TimeSpan.FromSeconds(20), ct), "The BDS did not confirm the teleport command.");
        int fillBefore = harness.Output.Count;
        await harness.SendCommandAsync($"fill 0 89 0 15 89 15 stone", ct);
        await WaitForServerLineAsync(harness, fillBefore, "blocks filled", string.Empty, TimeSpan.FromSeconds(20), ct);
        int airBefore = harness.Output.Count;
        await harness.SendCommandAsync($"fill 0 90 0 15 92 15 air", ct);
        await WaitForServerLineAsync(harness, airBefore, "blocks filled", string.Empty, TimeSpan.FromSeconds(20), ct);
        int modeBefore = harness.Output.Count;
        await harness.SendCommandAsync($"gamemode survival {PlayerName}", ct);
        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        int effectBefore = harness.Output.Count;
        await harness.SendCommandAsync($"effect {PlayerName} clear", ct);
        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        output.WriteLine($"Pad prepared. Mode tail: {string.Join(" | ", harness.Output.Skip(modeBefore).TakeLast(2))}.");
    }

    private async Task<BedrockVector3> ReadEyeAsync(BedrockClientSession session, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (session.CapturedMovePlayers.Count > 0)
                return session.CapturedMovePlayers[^1].Position;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return new BedrockVector3(8.5f, 91.62001f, 8.5f);
    }

    private async Task VerifySurvivalAsync(BedrockServerHarness harness, BedrockClientSession session, CancellationToken ct, ProbeReport report)
    {
        int outputBefore = harness.Output.Count;
        await harness.SendCommandAsync($"teleport {PlayerName} 8 90 8", ct);
        bool teleported = await WaitForServerLineAsync(harness, outputBefore, "Teleported", PlayerName, TimeSpan.FromSeconds(20), ct);
        Assert.True(teleported, "The BDS did not confirm the survival teleport command.");
        report.SurvivalTeleportConfirmed = teleported;
        bool moveObserved = await WaitForConditionAsync(() => session.MovePlayerObservationCount > 0, TimeSpan.FromSeconds(15), ct);
        report.SurvivalMoveObserved = moveObserved;
        output.WriteLine(moveObserved ? "Survival teleport answered with packet 19." : "Survival teleport answered with no packet 19 within 15 s.");
    }

    private void CapturePreTeleportObservations(BedrockClientSession session, ProbeReport report)
    {
        report.PreTeleportMoveTicks = session.CapturedMovePlayers.Select(static packet => packet.Tick).ToArray();
        report.PreTeleportMotionTicks = session.CapturedSetEntityMotions.Select(static packet => packet.Tick).ToArray();
        report.PreTeleportPredictionTicks = session.CapturedMovePredictions.Select(static packet => packet.Tick).ToArray();
        ulong sentLow = report.Sends > 0 ? report.LastTick - (ulong)(report.Sends - 1) : report.LastTick;
        bool InSentRange(ulong tick, ulong low, ulong high) => tick >= low && tick <= high && (!report.ExcludeTickZeroFromEcho || tick != 0);
        report.PreTeleportEchoObserved = report.PreTeleportMoveTicks.Any(tick => InSentRange(tick, sentLow, report.LastTick))
            || report.PreTeleportMotionTicks.Any(tick => InSentRange(tick, sentLow, report.LastTick))
            || report.PreTeleportPredictionTicks.Any(tick => InSentRange(tick, sentLow, report.LastTick));
        output.WriteLine(report.PreTeleportEchoObserved
            ? $"Pre-teleport echo candidate: a packet 19, 40, or 161 arrived before the survival teleport with a sent tick in [{sentLow}, {report.LastTick}] (move [{string.Join(",", report.PreTeleportMoveTicks)}], motion [{string.Join(",", report.PreTeleportMotionTicks)}], prediction [{string.Join(",", report.PreTeleportPredictionTicks)}])."
            : $"No pre-teleport echo: nothing arrived before the survival teleport with a sent tick in [{sentLow}, {report.LastTick}] (move [{string.Join(",", report.PreTeleportMoveTicks)}], motion [{string.Join(",", report.PreTeleportMotionTicks)}], prediction [{string.Join(",", report.PreTeleportPredictionTicks)}]).");
    }

    private void CaptureObservations(BedrockClientSession session, ProbeReport report)
    {
        report.MoveObservations = session.MovePlayerObservationCount;
        report.MotionObservations = session.SetEntityMotionObservationCount;
        report.PredictionObservations = session.MovePredictionObservationCount;
        report.PostJoinIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count());
        report.MoveTicks = session.CapturedMovePlayers.Select(static packet => packet.Tick).ToArray();
        report.MotionTicks = session.CapturedSetEntityMotions.Select(static packet => packet.Tick).ToArray();
        report.PredictionTicks = session.CapturedMovePredictions.Select(static packet => packet.Tick).ToArray();
        report.Violations = session.CapturedViolations.Select(static violation => $"{violation.PacketId}:{violation.Severity}:{violation.Reason}").ToArray();
        report.RejectedSends = session.RejectedSendAttempts;
        report.IsReady = session.Snapshot.IsReady;
        BedrockMovePlayerPacket? lastMove = session.CapturedMovePlayers.Count > 0 ? session.CapturedMovePlayers[^1] : null;
        report.AuthoritativePosition = lastMove?.Position;
        ulong sentLow = report.Sends > 0 ? report.LastTick - (ulong)(report.Sends - 1) : report.LastTick;
        bool InSentRange(ulong tick, ulong low, ulong high) => tick >= low && tick <= high && (!report.ExcludeTickZeroFromEcho || tick != 0);
        report.TickEchoObserved = report.MoveTicks.Any(tick => InSentRange(tick, sentLow, report.LastTick))
            || report.MotionTicks.Any(tick => InSentRange(tick, sentLow, report.LastTick))
            || report.PredictionTicks.Any(tick => InSentRange(tick, sentLow, report.LastTick));
        output.WriteLine(report.TickEchoObserved
            ? $"Tick echo observed: a packet 19, 40, or 161 carries a sent tick in [{sentLow}, {report.LastTick}]."
            : $"No tick echo: no packet 19, 40, or 161 carries a sent tick in [{sentLow}, {report.LastTick}] (move [{string.Join(",", report.MoveTicks)}], motion [{string.Join(",", report.MotionTicks)}], prediction [{string.Join(",", report.PredictionTicks)}]).");
        for (int index = 0; index < session.CapturedMovePlayerPayloads.Count; index++)
        {
            byte[] payload = session.CapturedMovePlayerPayloads[index];
            BedrockMovePlayerPacket decodedMove = BedrockActorCodecs.DecodeMovePlayer(payload);
            Assert.Equal(payload, BedrockActorCodecs.Encode(decodedMove));
        }

        for (int index = 0; index < session.CapturedSetEntityMotionPayloads.Count; index++)
        {
            byte[] payload = session.CapturedSetEntityMotionPayloads[index];
            BedrockSetEntityMotionPacket decodedMotion = BedrockActorCodecs.DecodeSetEntityMotion(payload);
            Assert.Equal(payload, BedrockActorCodecs.Encode(decodedMotion));
        }

        for (int index = 0; index < session.CapturedMovePredictionPayloads.Count; index++)
        {
            byte[] payload = session.CapturedMovePredictionPayloads[index];
            BedrockCorrectPlayerMovePredictionPacket decoded = BedrockPlayerAuthInputCodecs.DecodePrediction(payload);
            Assert.Equal(payload, BedrockPlayerAuthInputCodecs.Encode(decoded));
        }
    }

    private async Task WriteEvidenceAsync(BedrockServerHarness harness, BedrockClientSession session, ProbeReport report, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-player-auth-input", Path.GetFileName(harness.EvidenceDirectory), report.Probe);
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
            player = PlayerName,
            probe = report.Probe,
            sends = report.Sends,
            lastTick = report.LastTick,
            startGameCurrentTick = report.StartGameCurrentTick,
            startGameRewindHistorySize = report.StartGameRewindHistorySize,
            startGameRemainderSha256 = report.StartGameRemainderSha256,
            tickConversion = report.TickConversion,
            excludeTickZeroFromEcho = report.ExcludeTickZeroFromEcho,
            authoritativePosition = report.AuthoritativePosition is BedrockVector3 authoritative
                ? new { x = authoritative.X, y = authoritative.Y, z = authoritative.Z }
                : null,
            eye = new { x = report.Eye.X, y = report.Eye.Y, z = report.Eye.Z },
            survivalTeleportConfirmed = report.SurvivalTeleportConfirmed,
            survivalMoveObserved = report.SurvivalMoveObserved,
            moveObservations = report.MoveObservations,
            motionObservations = report.MotionObservations,
            predictionObservations = report.PredictionObservations,
            moveTicks = report.MoveTicks,
            motionTicks = report.MotionTicks,
            predictionTicks = report.PredictionTicks,
            postJoinPacketIds = report.PostJoinIds,
            violations = report.Violations,
            rejectedSendAttempts = report.RejectedSends,
            tickEchoObserved = report.TickEchoObserved,
            preTeleportMoveTicks = report.PreTeleportMoveTicks,
            preTeleportMotionTicks = report.PreTeleportMotionTicks,
            preTeleportPredictionTicks = report.PreTeleportPredictionTicks,
            preTeleportEchoObserved = report.PreTeleportEchoObserved,
            isReady = report.IsReady,
        };
        await File.WriteAllTextAsync(Path.Combine(captureRoot, "auth-input-metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(harness.EvidenceDirectory, $"auth-input-{report.Probe}.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"Auth-input {report.Probe} evidence written to {captureRoot} (strict {harness.EffectiveMovementStrict}, config {harness.ConfigSha256}).");
    }

    private async Task WriteFailureAsync(BedrockServerHarness harness, BedrockClientSession session, ProbeReport report, Exception failure, CancellationToken ct)
    {
        output.WriteLine($"Live auth-input failure on probe {report.Probe}: {failure}");
        var evidence = new
        {
            probe = report.Probe,
            failure = failure.ToString(),
            isReady = session.Snapshot.IsReady,
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
        };
        await File.WriteAllTextAsync(Path.Combine(harness.EvidenceDirectory, $"auth-input-{report.Probe}-failure.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
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

    private sealed class ProbeReport
    {
        internal string Probe { get; set; } = string.Empty;

        internal bool StrictMovement { get; set; }

        internal BedrockVector3 Eye { get; set; }

        internal int Sends { get; set; }

        internal ulong LastTick { get; set; }

        internal long? StartGameCurrentTick { get; set; }

        internal int? StartGameRewindHistorySize { get; set; }

        internal string? StartGameRemainderSha256 { get; set; }

        internal string? TickConversion { get; set; }

        internal bool ExcludeTickZeroFromEcho { get; set; }

        internal BedrockVector3? AuthoritativePosition { get; set; }

        internal bool SurvivalTeleportConfirmed { get; set; }

        internal bool SurvivalMoveObserved { get; set; }

        internal int MoveObservations { get; set; }

        internal int MotionObservations { get; set; }

        internal int PredictionObservations { get; set; }

        internal Dictionary<int, int> PostJoinIds { get; set; } = [];

        internal ulong[] MoveTicks { get; set; } = [];

        internal ulong[] MotionTicks { get; set; } = [];

        internal ulong[] PredictionTicks { get; set; } = [];

        internal string[] Violations { get; set; } = [];

        internal int RejectedSends { get; set; }

        internal bool TickEchoObserved { get; set; }

        internal ulong[] PreTeleportMoveTicks { get; set; } = [];

        internal ulong[] PreTeleportMotionTicks { get; set; } = [];

        internal ulong[] PreTeleportPredictionTicks { get; set; } = [];

        internal bool PreTeleportEchoObserved { get; set; }

        internal bool IsReady { get; set; }
    }
}
