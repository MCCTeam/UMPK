using System.Text.Json;
using Umpk.Client.Bedrock;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.Session;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Bedrock.IntegrationTests;

public sealed class OfflineJoinLiveTests(ITestOutputHelper output)
{
    private const string PlayerName = "UmpkLiveBot";

    [BedrockLiveFact]
    public async Task OfflineJoinAgainstIsolatedBdsReachesReady()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

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

        BedrockSessionSnapshot snapshot;
        try
        {
            snapshot = await session.WaitUntilReadyAsync(ct);
        }
        finally
        {
            await WriteEvidenceAsync(harness, session, PlayerName).ConfigureAwait(false);
        }

        int assertions = 0;
        Assert.True(snapshot.IsReady); assertions++;
        Assert.True(snapshot.IdentityAccepted && snapshot.ResourcesComplete); assertions++;
        Assert.True(snapshot.WorldKnown && snapshot.SpawnAuthorized); assertions++;
        Assert.True(snapshot.ItemRegistryAvailable && snapshot.BiomeRegistryAvailable && snapshot.EntityRegistryAvailable); assertions++;
        Assert.NotEqual(0UL, snapshot.LocalRuntimeEntityId); assertions++;
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Outbound && evt.PacketId == 113); assertions++;
        Assert.Contains(session.WireTranscript, static evt => evt.Direction == BedrockWireDirection.Inbound && evt.PacketId == 11); assertions++;
        IReadOnlyList<string> playerLines = harness.FindPlayerLines(PlayerName);
        Assert.NotEmpty(playerLines); assertions++;
        output.WriteLine($"Ready with runtime {snapshot.LocalRuntimeEntityId}, encryption={snapshot.EncryptionInstalled}, chunks={snapshot.LevelChunksObserved}.");
        output.WriteLine($"BDS observed the player: {string.Join(" | ", playerLines.Take(3))}");
        output.WriteLine($"Wire transcript ({session.WireTranscript.Count} events): {FormatTranscript(session.WireTranscript)}");
        Assert.True(assertions > 0);

        await session.DisconnectAsync(ct);
        await client.DisposeAsync();
    }

    private static async Task WriteEvidenceAsync(BedrockServerHarness harness, BedrockClientSession session, string playerName)
    {
        BedrockSessionSnapshot snapshot = session.Snapshot;
        var evidence = new
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
            player = playerName,
            protocol = 2169,
            snapshot = new
            {
                snapshot.IsReady,
                snapshot.TransportConnected,
                snapshot.CompressionInstalled,
                snapshot.LoginSent,
                snapshot.EncryptionInstalled,
                snapshot.IdentityAccepted,
                snapshot.ResourcesComplete,
                snapshot.WorldKnown,
                snapshot.SpawnAuthorized,
                snapshot.ItemRegistryAvailable,
                snapshot.BiomeRegistryAvailable,
                snapshot.EntityRegistryAvailable,
                snapshot.LocalPlayerInitializationSent,
                snapshot.LocalEntityId,
                snapshot.LocalRuntimeEntityId,
                snapshot.LevelChunksObserved,
                Termination = snapshot.Termination?.ToString(),
            },
            transcript = session.WireTranscript.Select(static evt => new
            {
                direction = evt.Direction.ToString(),
                packetId = evt.PacketId,
                timestamp = evt.Timestamp,
            }).ToArray(),
            rejectedSendAttempts = session.RejectedSendAttempts,
            serverPlayerLines = harness.FindPlayerLines(playerName).ToArray(),
        };
        string path = Path.Combine(harness.EvidenceDirectory, "evidence.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
    }

    private static string FormatTranscript(IReadOnlyList<BedrockWireEvent> transcript) =>
        string.Join(" ", transcript.Select(static evt => $"{(evt.Direction == BedrockWireDirection.Inbound ? '<' : '>')}{evt.PacketId}"));
}
