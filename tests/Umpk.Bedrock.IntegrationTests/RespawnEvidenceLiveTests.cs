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

[Collection("Bedrock respawn evidence")]
public sealed class RespawnEvidenceLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkRespawnBot";

    [BedrockLiveFact]
    public async Task OfflineJoinRetainsRawRespawnPayloads()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_RESPAWN");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_RESPAWN", "1");
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

            bool retained = await WaitForConditionAsync(() => session.CapturedRespawnPayloads.Count > 0, TimeSpan.FromSeconds(60), ct);
            output.WriteLine(retained
                ? $"Session retained {session.CapturedRespawnPayloads.Count} raw packet 45 payload(s)."
                : "Session retained no raw packet 45 payload.");
            Assert.True(retained);

            int inbound45 = session.WireTranscript.Count(static entry => entry.Direction == BedrockWireDirection.Inbound && entry.PacketId == 45);
            output.WriteLine($"Wire transcript carries {inbound45} inbound packet 45 event(s).");
            Assert.True(inbound45 > 0);

            int outbound45 = session.WireTranscript.Count(static entry => entry.Direction == BedrockWireDirection.Outbound && entry.PacketId == 45);
            output.WriteLine($"Wire transcript carries {outbound45} outbound packet 45 event(s).");
            Assert.Equal(0, outbound45);

            IReadOnlyList<byte[]> payloads = session.CapturedRespawnPayloads;
            Assert.True(payloads.Count <= 8);
            foreach (byte[] payload in payloads)
            {
                Assert.True(payload.Length > 0);
                Assert.True(payload.Length <= 64);
                BedrockRespawnPacket decoded = BedrockRespawnCodecs.Decode(payload);
                Assert.True(float.IsFinite(decoded.Position.X));
                Assert.True(float.IsFinite(decoded.Position.Y));
                Assert.True(float.IsFinite(decoded.Position.Z));
                Assert.Equal(payload, BedrockRespawnCodecs.Encode(decoded));
                output.WriteLine($"Packet 45 payload: {payload.Length} bytes, sha256 {Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()}.");
            }

            BedrockRespawnPacket[] ordered = payloads.Select(static payload => BedrockRespawnCodecs.Decode(payload)).ToArray();
            BedrockRespawnPacket? finalState1 = ordered.LastOrDefault(static packet => packet.State == 1);
            Assert.NotNull(finalState1);
            BedrockVector3[] staging = ordered.Where(static packet => packet.State == 0).Select(static packet => packet.Position).ToArray();

            bool applied = await WaitForConditionAsync(() => LocalPosition(session) is not null && LocalPosition(session)!.Value.Equals(finalState1.Position), TimeSpan.FromSeconds(60), ct);
            Assert.True(applied);
            BedrockVector3? localPosition = LocalPosition(session);
            Assert.NotNull(localPosition);
            Assert.Equal(finalState1.Position, localPosition!.Value);
            foreach (BedrockVector3 staged in staging)
                Assert.NotEqual(staged, localPosition!.Value);
            output.WriteLine($"Local actor position {localPosition!.Value.X},{localPosition!.Value.Y},{localPosition!.Value.Z} equals final state1 and differs from {staging.Length} staging coordinate(s).");

            await WriteEvidenceAsync(harness, session, payloads, inbound45, outbound45, ct).ConfigureAwait(false);
            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_RESPAWN", previousCapture);
        }
    }

    private async Task WriteEvidenceAsync(BedrockServerHarness harness, BedrockClientSession session, IReadOnlyList<byte[]> payloads, int inbound45, int outbound45, CancellationToken ct)
    {
        var decoded = payloads.Select(static payload => BedrockRespawnCodecs.Decode(payload)).ToArray();
        BedrockVector3? localPosition = LocalPosition(session);
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
                ipv6Port = harness.IPv6Port,
                processId = harness.ProcessId,
                runDirectory = harness.RunDirectory,
            },
            protocol = 2169,
            player = PlayerName,
            inbound45Events = inbound45,
            outbound45Events = outbound45,
            rawRespawns = payloads.Select((payload, index) => new
            {
                index,
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
                state = decoded[index].State,
                x = decoded[index].Position.X,
                y = decoded[index].Position.Y,
                z = decoded[index].Position.Z,
                runtimeEntityId = decoded[index].RuntimeEntityId,
            }).ToArray(),
            localRuntimeEntityId = session.Snapshot.LocalRuntimeEntityId,
            localPosition = localPosition.HasValue ? new { x = localPosition.Value.X, y = localPosition.Value.Y, z = localPosition.Value.Z } : null,
            isReady = session.Snapshot.IsReady,
            termination = session.Snapshot.Termination?.ToString(),
        };
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-respawn-capture", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        for (int index = 0; index < payloads.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"respawn-raw-{index}.bin"), payloads[index], ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "respawn-evidence.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "respawn-evidence.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        output.WriteLine($"Respawn evidence written to {captureRoot}.");
    }

    private static BedrockVector3? LocalPosition(BedrockClientSession session)
    {
        foreach (BedrockActorSnapshot actor in session.ActorSnapshots)
        {
            if (actor.Kind == BedrockActorKind.LocalPlayer)
                return actor.Position;
        }

        return null;
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
