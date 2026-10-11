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

[Collection("Bedrock inventory capture")]
public sealed class CreativeContentCaptureLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkCreativeBot";

    [BedrockLiveFact]
    public async Task CreativeJoinRetainsRawCreativeContentPayload()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness(creativeMode: true);
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}, config {harness.ConfigSha256}).");

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_CREATIVE");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_CREATIVE", "1");
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
            Assert.True(session.Snapshot.LevelChunksObserved > 0);

            int inbound145 = session.WireTranscript.Count(static entry => entry.Direction == BedrockWireDirection.Inbound && entry.PacketId == 145);
            output.WriteLine(inbound145 > 0
                ? $"Wire transcript carries {inbound145} inbound packet 145 event(s)."
                : "Wire transcript carries no inbound packet 145 event.");
            Assert.True(inbound145 > 0);

            bool retained = await WaitForConditionAsync(() => session.CapturedCreativeContentPayloads.Count > 0, TimeSpan.FromSeconds(60), ct);
            output.WriteLine(retained
                ? $"Session retained {session.CapturedCreativeContentPayloads.Count} raw packet 145 payload(s)."
                : "Session retained no raw packet 145 payload.");
            Assert.True(retained, "The session observed packet 145 on the wire but retained no raw payload.");

            IReadOnlyList<byte[]> payloads = session.CapturedCreativeContentPayloads;
            foreach (byte[] payload in payloads)
            {
                Assert.True(payload.Length > 0);
                output.WriteLine($"Packet 145 payload: {payload.Length} bytes, sha256 {Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()}, prefix {Convert.ToHexString(payload.AsSpan(0, Math.Min(payload.Length, 16)))}.");
            }

            Assert.Single(payloads);
            BedrockCreativeContentPacket decoded = BedrockCreativeContentCodecs.Decode(payloads[0]);
            Assert.Equal(payloads[0], BedrockCreativeContentCodecs.Encode(decoded));
            Assert.Equal(123, decoded.Groups.Length);
            Assert.Equal(1875, decoded.Entries.Length);
            Assert.Equal(1, decoded.Entries[0].EntryId);
            Assert.Equal(5, decoded.Entries[0].Item.NetworkId);
            Assert.Equal(1, decoded.Entries[0].Item.Count);
            Assert.Equal(0u, decoded.Entries[0].Item.MetadataValue);
            Assert.Equal(0, decoded.Entries[0].GroupIndex);
            Assert.Equal(1875, decoded.Entries[^1].EntryId);
            Assert.Equal(122, decoded.Entries[^1].GroupIndex);
            for (int index = 0; index < decoded.Entries.Length; index++)
                Assert.Equal(index + 1, decoded.Entries[index].EntryId);
            output.WriteLine($"Packet 145 decodes to {decoded.Groups.Length} groups and {decoded.Entries.Length} entries with sequential entry ids and a byte-identical round trip.");

            await WriteCaptureAsync(harness, session, payloads, ct).ConfigureAwait(false);
            Assert.True(session.Snapshot.IsReady);
            Assert.False(session.Completion.IsCompleted);
            Assert.Null(session.Snapshot.Termination);

            await session.DisconnectAsync(ct);
            await client.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_CREATIVE", previousCapture);
        }
    }

    private async Task WriteCaptureAsync(BedrockServerHarness harness, BedrockClientSession session, IReadOnlyList<byte[]> payloads, CancellationToken ct)
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), "umpk-bedrock-creative-capture", Path.GetFileName(harness.EvidenceDirectory));
        Directory.CreateDirectory(captureRoot);
        for (int index = 0; index < payloads.Count; index++)
            await File.WriteAllBytesAsync(Path.Combine(captureRoot, $"creativecontent-raw-{index}.bin"), payloads[index], ct).ConfigureAwait(false);
        var metadata = new
        {
            bds = new
            {
                packageVersion = "1.26.45.1-1",
                sourceArtifactSha256 = BedrockServerHarness.ExpectedSourceSha256,
                executableSha256 = harness.ExecutableSha256,
                expectedExecutableSha256 = BedrockServerHarness.ExpectedExecutableSha256,
                configSha256 = harness.ConfigSha256,
                creativeModeRequested = harness.CreativeModeRequested,
                ipv4Port = harness.Port,
                processId = harness.ProcessId,
            },
            protocol = 2169,
            player = PlayerName,
            rawCreativePayloads = payloads.Select(static payload => new
            {
                payloadBytes = payload.Length,
                payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            }).ToArray(),
            inbound145Events = session.WireTranscript.Count(static entry => entry.Direction == BedrockWireDirection.Inbound && entry.PacketId == 145),
            postJoinPacketIds = session.PostJoinPacketIds.GroupBy(static id => id).ToDictionary(static group => group.Key, static group => group.Count()),
            termination = session.Snapshot.Termination?.ToString(),
            isReady = session.Snapshot.IsReady,
        };
        await File.WriteAllTextAsync(
            Path.Combine(captureRoot, "creative-capture.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(harness.EvidenceDirectory, "creative-capture.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
        output.WriteLine($"Creative capture written to {captureRoot}.");
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
