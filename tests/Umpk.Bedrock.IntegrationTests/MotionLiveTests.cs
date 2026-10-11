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
public sealed class MotionLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkMotionBot";

    [BedrockLiveFact]
    public async Task TeleportProvokesMovePlayerWithExactRoundTrip()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", "1");
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
                Assert.True(session.Snapshot.LevelChunksObserved > 0);
                ulong localRuntime = session.Snapshot.LocalRuntimeEntityId;
                Assert.NotEqual(0UL, localRuntime);

                int outputBefore = harness.Output.Count;
                session.ResetMotionCapture();
                await harness.SendCommandAsync($"teleport {PlayerName} 8 90 8", ct);

                bool teleported = await WaitForServerLineAsync(harness, outputBefore, "Teleported", PlayerName, TimeSpan.FromSeconds(20), ct);
                output.WriteLine(teleported
                    ? $"BDS confirmed teleport for {PlayerName}."
                    : $"BDS did not confirm teleport. Tail: {string.Join(" | ", harness.Output.Skip(outputBefore).TakeLast(5))}.");
                Assert.True(teleported, "The BDS did not confirm the teleport command.");

                bool moveObserved = await WaitForConditionAsync(() => session.MovePlayerObservationCount > 0, TimeSpan.FromSeconds(30), ct);
                output.WriteLine(moveObserved
                    ? $"Session observed {session.MovePlayerObservationCount} MovePlayer packet(s)."
                    : $"Session observed no MovePlayer; post-join IDs: {string.Join(",", session.PostJoinPacketIds.Distinct().Order())}.");
                Assert.True(moveObserved, "The session observed no protocol 2169 packet 19 after a teleport command.");
                Assert.Contains(19, session.PostJoinPacketIds);

                IReadOnlyList<BedrockMovePlayerPacket> moves = session.CapturedMovePlayers;
                IReadOnlyList<byte[]> rawMoves = session.CapturedMovePlayerPayloads;
                Assert.NotEmpty(moves);
                Assert.NotEmpty(rawMoves);
                Assert.Equal(moves.Count, rawMoves.Count);
                for (int index = 0; index < rawMoves.Count; index++)
                {
                    BedrockMovePlayerPacket decoded = BedrockActorCodecs.DecodeMovePlayer(rawMoves[index]);
                    Assert.Equal(rawMoves[index], BedrockActorCodecs.Encode(decoded));
                    Assert.Equal(moves[index], decoded);
                    Assert.True(float.IsFinite(decoded.Position.X) && float.IsFinite(decoded.Position.Y) && float.IsFinite(decoded.Position.Z));
                    Assert.True(float.IsFinite(decoded.Pitch) && float.IsFinite(decoded.Yaw) && float.IsFinite(decoded.HeadYaw));
                    output.WriteLine($"MovePlayer {index}: runtime {decoded.RuntimeEntityId} pos ({decoded.Position.X}, {decoded.Position.Y}, {decoded.Position.Z}) pitch {decoded.Pitch} yaw {decoded.Yaw} headYaw {decoded.HeadYaw} mode {(byte)decoded.Mode} onGround {decoded.OnGround} ridden {decoded.RiddenRuntimeEntityId} cause {(decoded.TeleportCause.HasValue ? ((int)decoded.TeleportCause.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")} source {(decoded.TeleportSourceEntityType.HasValue ? decoded.TeleportSourceEntityType.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")} tick {decoded.Tick}.");
                }

                BedrockMovePlayerPacket? teleport = moves.FirstOrDefault(static packet => packet.Mode == BedrockMovePlayerMode.Teleport);
                Assert.NotNull(teleport);
                output.WriteLine($"Teleport MovePlayer Y {teleport.Position.Y} against commanded Y 90.");
                Assert.Equal(8.5f, teleport.Position.X, 0.02f);
                Assert.Equal(8.5f, teleport.Position.Z, 0.02f);
                Assert.Equal(91.62f, teleport.Position.Y, 0.05f);
                Assert.Equal(BedrockTeleportCause.Command, teleport.TeleportCause);
                Assert.Equal(1, teleport.TeleportSourceEntityType);

                await WriteCaptureAsync(harness, session, ct).ConfigureAwait(false);
                Assert.True(session.Snapshot.IsReady);
                Assert.False(session.Completion.IsCompleted);
                Assert.Null(session.Snapshot.Termination);

                await session.DisconnectAsync(ct);
            }
            catch (Exception failure)
            {
                await WriteFailureAsync(harness, session, failure, ct).ConfigureAwait(false);
                throw;
            }

            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", previousCapture);
        }
    }

    [BedrockLiveFact]
    public async Task ProvocationSeeksSetEntityMotion()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", "1");
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

                BedrockActorSnapshot? self = session.ActorSnapshots.FirstOrDefault(static actor => actor.Kind == Umpk.Client.Bedrock.State.Actors.BedrockActorKind.LocalPlayer);
                output.WriteLine(self?.Position is { } position
                    ? $"Local player at ({position.X}, {position.Y}, {position.Z})."
                    : "Local player has no position yet.");

                session.ResetMotionCapture();
                int summonOutput = harness.Output.Count;
                await harness.SendCommandAsync($"summon zombie ~2 ~ ~2", ct);
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                output.WriteLine($"Summon tail: {string.Join(" | ", harness.Output.Skip(summonOutput).TakeLast(3))}.");

                int damageOutput = harness.Output.Count;
                await harness.SendCommandAsync($"damage @e[type=zombie,c=1] 5", ct);
                bool damageConfirmed = await WaitForServerLineAsync(harness, damageOutput, "Applied damage", "Zombie", TimeSpan.FromSeconds(20), ct);
                output.WriteLine(damageConfirmed
                    ? "BDS confirmed damage to the zombie."
                    : $"BDS did not confirm damage. Tail: {string.Join(" | ", harness.Output.Skip(damageOutput).TakeLast(3))}.");
                Assert.True(damageConfirmed, "The BDS did not confirm damage to the zombie.");
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                int levitateOutput = harness.Output.Count;
                await harness.SendCommandAsync($"effect {PlayerName} levitation 5 1 true", ct);
                bool levitateConfirmed = await WaitForServerLineAsync(harness, levitateOutput, "Gave Levitation", PlayerName, TimeSpan.FromSeconds(20), ct);
                output.WriteLine(levitateConfirmed
                    ? $"BDS confirmed levitation for {PlayerName}."
                    : $"BDS did not confirm levitation. Tail: {string.Join(" | ", harness.Output.Skip(levitateOutput).TakeLast(3))}.");
                Assert.True(levitateConfirmed, "The BDS did not confirm levitation.");
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                output.WriteLine($"Damage tail: {string.Join(" | ", harness.Output.Skip(damageOutput).TakeLast(3))}.");
                output.WriteLine($"Levitate tail: {string.Join(" | ", harness.Output.Skip(levitateOutput).TakeLast(3))}.");

                bool motionObserved = await WaitForConditionAsync(() => session.SetEntityMotionObservationCount > 0, TimeSpan.FromSeconds(30), ct);
                output.WriteLine(motionObserved
                    ? $"Session observed {session.SetEntityMotionObservationCount} SetEntityMotion packet(s)."
                    : $"Session observed no SetEntityMotion; post-join IDs: {string.Join(",", session.PostJoinPacketIds.Distinct().Order())}.");
                Assert.True(motionObserved, "The session observed no protocol 2169 packet 40 after the confirmed damage and levitation provocations.");

                IReadOnlyList<BedrockSetEntityMotionPacket> motions = session.CapturedSetEntityMotions;
                IReadOnlyList<byte[]> rawMotions = session.CapturedSetEntityMotionPayloads;
                Assert.NotEmpty(motions);
                Assert.NotEmpty(rawMotions);
                for (int index = 0; index < rawMotions.Count; index++)
                {
                    BedrockSetEntityMotionPacket decoded = BedrockActorCodecs.DecodeSetEntityMotion(rawMotions[index]);
                    Assert.Equal(rawMotions[index], BedrockActorCodecs.Encode(decoded));
                    Assert.Equal(motions[index], decoded);
                    Assert.True(float.IsFinite(decoded.Velocity.X) && float.IsFinite(decoded.Velocity.Y) && float.IsFinite(decoded.Velocity.Z));
                    output.WriteLine($"SetEntityMotion {index}: runtime {decoded.RuntimeEntityId} vel ({decoded.Velocity.X}, {decoded.Velocity.Y}, {decoded.Velocity.Z}) tick {decoded.Tick}.");
                }

                Assert.Contains(40, session.PostJoinPacketIds);

                await WriteCaptureAsync(harness, session, ct).ConfigureAwait(false);
                await WriteMotionNegativeAsync(harness, session, motionObserved, ct).ConfigureAwait(false);
                Assert.True(session.Snapshot.IsReady);
                Assert.False(session.Completion.IsCompleted);

                await session.DisconnectAsync(ct);
            }
            catch (Exception failure)
            {
                await WriteFailureAsync(harness, session, failure, ct).ConfigureAwait(false);
                throw;
            }

            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", previousCapture);
        }
    }

    private async Task WriteCaptureAsync(BedrockServerHarness harness, BedrockClientSession session, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-motion-capture", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        IReadOnlyList<byte[]> rawMoves = session.CapturedMovePlayerPayloads;
        for (int index = 0; index < rawMoves.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"moveplayer-{index}.bin"), rawMoves[index], ct).ConfigureAwait(false);
        IReadOnlyList<byte[]> rawMotions = session.CapturedSetEntityMotionPayloads;
        for (int index = 0; index < rawMotions.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"setentitymotion-{index}.bin"), rawMotions[index], ct).ConfigureAwait(false);
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
            moves = session.CapturedMovePlayers.Select(static packet => new
            {
                runtimeEntityId = packet.RuntimeEntityId,
                x = packet.Position.X,
                y = packet.Position.Y,
                z = packet.Position.Z,
                pitch = packet.Pitch,
                yaw = packet.Yaw,
                headYaw = packet.HeadYaw,
                mode = (byte)packet.Mode,
                onGround = packet.OnGround,
                riddenRuntimeEntityId = packet.RiddenRuntimeEntityId,
                teleportCause = packet.TeleportCause.HasValue ? (int)packet.TeleportCause.Value : (int?)null,
                teleportSource = packet.TeleportSourceEntityType,
                tick = packet.Tick,
            }).ToArray(),
            rawMovePayloads = rawMoves.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            motions = session.CapturedSetEntityMotions.Select(static packet => new
            {
                runtimeEntityId = packet.RuntimeEntityId,
                vx = packet.Velocity.X,
                vy = packet.Velocity.Y,
                vz = packet.Velocity.Z,
                tick = packet.Tick,
            }).ToArray(),
            rawMotionPayloads = rawMotions.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            moveObservations = session.MovePlayerObservationCount,
            motionObservations = session.SetEntityMotionObservationCount,
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            rejectedSendAttempts = session.RejectedSendAttempts,
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(Path.Combine(captureRoot, "motion-metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(harness.EvidenceDirectory, "motion-capture.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"Motion capture written to {captureRoot}.");
    }

    private async Task WriteMotionNegativeAsync(BedrockServerHarness harness, BedrockClientSession session, bool motionObserved, CancellationToken ct)
    {
        var evidence = new
        {
            bds = new
            {
                packageVersion = "1.26.45.1-1",
                executableSha256 = harness.ExecutableSha256,
                ipv4Port = harness.Port,
            },
            protocol = 2169,
            player = PlayerName,
            motionObserved,
            motionObservations = session.SetEntityMotionObservationCount,
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(Path.Combine(harness.EvidenceDirectory, "motion-negative.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"Motion negative evidence written to {harness.EvidenceDirectory}.");
    }

    private async Task WriteFailureAsync(BedrockServerHarness harness, BedrockClientSession session, Exception failure, CancellationToken ct)
    {
        output.WriteLine($"Live motion failure: {failure}");
        var evidence = new
        {
            failure = failure.ToString(),
            moveObservations = session.MovePlayerObservationCount,
            motionObservations = session.SetEntityMotionObservationCount,
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(Path.Combine(harness.EvidenceDirectory, "motion-failure.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
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
            if (fresh.Any(line => line.Contains(marker, StringComparison.Ordinal) && line.Contains(player, StringComparison.Ordinal)))
                return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        return harness.Output.Skip(outputBefore).Any(line => line.Contains(marker, StringComparison.Ordinal) && line.Contains(player, StringComparison.Ordinal));
    }
}
