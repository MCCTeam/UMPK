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
public sealed class ExistingBdsExternalMovementLiveTests(ITestOutputHelper output)
{
    private const string ExistingPortVariable = "UMPK_BEDROCK_EXISTING_PORT";
    private const string ExternalPlayerVariable = "UMPK_BEDROCK_EXTERNAL_PLAYER";
    private const string MarkerDirectory = "/tmp/umpk-bedrock-external-observer";
    private const string MarkerFileName = "ready.json";
    private const string ResultFileName = "external-movement-result.json";
    private const int DefaultExistingPort = 39945;
    private const int ProtocolVersion = 2169;

    /// <summary>Attaches a detached observer to an already running BDS and proves a real movement relay.</summary>
    [BedrockLiveFact]
    public async Task ExistingBdsMovementIsObservedByDetachedObserver()
    {
        string playerName = RequireVariable(ExternalPlayerVariable);
        int port = ResolvePort();
        string observerName = $"UmpkExtObs{Guid.NewGuid():N}"[..16];
        DeleteReadyMarker();

        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken ct = overallTimeout.Token;

        string? previousCapture = Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION");
        Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", "1");
        await using var observerClient = CreateClient(port, observerName);
        await using BedrockClientSession observer = await observerClient.ConnectAsync(ct);
        try
        {
            BedrockSessionSnapshot ready = await observer.WaitUntilReadyAsync(ct);
            Assert.True(ready.IsReady);

            BedrockActorSnapshot playerSeenByObserver = await WaitForPlayerAsync(observer, playerName, TimeSpan.FromMinutes(5), ct);
            ulong playerRuntimeId = playerSeenByObserver.Handle.RuntimeEntityId;
            Assert.NotEqual(0UL, playerRuntimeId);
            output.WriteLine($"Observer sees existing player '{playerName}' as runtime {playerRuntimeId}, unique {playerSeenByObserver.Handle.UniqueEntityId}.");
            BedrockVector3? windowAnchor = playerSeenByObserver.Position;

            observer.ResetMotionCapture();
            int payloadsBeforeWindow = observer.CapturedMovePlayerPayloads.Count;
            WriteReadyMarker(new ExternalObserverMarker("127.0.0.1", port, observerName, playerName, DateTimeOffset.UtcNow));
            output.WriteLine($"Observer '{observerName}' sees '{playerName}' on 127.0.0.1:{port}. The 120 s movement window is open; marker is at {Path.Combine(MarkerDirectory, MarkerFileName)}.");
            output.WriteLine($"Observation window: 120 s for runtime {playerRuntimeId}. Waiting for on foot movement without teleport.");
            await Task.Delay(TimeSpan.FromSeconds(120), ct).ConfigureAwait(false);

            IReadOnlyList<byte[]> freshPayloads = observer.CapturedMovePlayerPayloads.Skip(payloadsBeforeWindow).ToArray();
            var decodedFresh = new List<BedrockMovePlayerPacket>(freshPayloads.Count);
            int roundTrips = 0;
            foreach (byte[] payload in freshPayloads)
            {
                BedrockMovePlayerPacket decoded = BedrockActorCodecs.DecodeMovePlayer(payload);
                Assert.Equal(payload, BedrockActorCodecs.Encode(decoded));
                roundTrips++;
                if (decoded.RuntimeEntityId == playerRuntimeId)
                {
                    decodedFresh.Add(decoded);
                }
            }

            IReadOnlyList<BedrockMovePlayerPacket> walkingMoves = decodedFresh.Where(static packet => packet.Mode != BedrockMovePlayerMode.Teleport).ToArray();
            BedrockVector3? anchor = windowAnchor ?? walkingMoves.FirstOrDefault()?.Position;
            double maxDisplacement = 0;
            foreach (BedrockMovePlayerPacket move in walkingMoves)
            {
                if (anchor is null)
                {
                    break;
                }

                double displacement = Math.Sqrt(Math.Pow(move.Position.X - anchor.Value.X, 2) + Math.Pow(move.Position.Y - anchor.Value.Y, 2) + Math.Pow(move.Position.Z - anchor.Value.Z, 2));
                maxDisplacement = Math.Max(maxDisplacement, displacement);
            }

            ulong[] walkingTicks = walkingMoves.Select(static packet => packet.Tick).ToArray();
            output.WriteLine($"Window result: {freshPayloads.Count} fresh payloads, {decodedFresh.Count} relays for runtime {playerRuntimeId}, {walkingMoves.Count} without teleport, max displacement {maxDisplacement:F4} blocks, round trips {roundTrips}, ticks {string.Join(",", walkingTicks.Take(20))}.");

            try
            {
                Assert.True(walkingMoves.Count > 0, $"The detached observer recorded no movement relays for '{playerName}' during the 120 s window. The player may have stood still or left the game.");
                Assert.True(maxDisplacement > 0.5, $"The observed displacement was only {maxDisplacement:F4} blocks. Walk on foot for a clear signal without teleporting.");
                Assert.True(observer.Snapshot.IsReady);
                Assert.Equal(0, observer.RejectedSendAttempts);
                await WriteResultAsync(port, observerName, playerName, playerRuntimeId, freshPayloads.Count, walkingMoves.Count, walkingTicks, maxDisplacement, roundTrips, observer.Snapshot.IsReady, observer.RejectedSendAttempts, failure: null, ct).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                await WriteResultAsync(port, observerName, playerName, playerRuntimeId, freshPayloads.Count, walkingMoves.Count, walkingTicks, maxDisplacement, roundTrips, observer.Snapshot.IsReady, observer.RejectedSendAttempts, failure.ToString(), ct).ConfigureAwait(false);
                throw;
            }

            await observer.DisconnectAsync(ct);
            await observerClient.DisposeAsync();
        }
        finally
        {
            DeleteReadyMarker();
            Environment.SetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION", previousCapture);
        }
    }

    private static string RequireVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Set {name} before running the detached observer leg.");
        }

        return value;
    }

    private static int ResolvePort()
    {
        string? raw = Environment.GetEnvironmentVariable(ExistingPortVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultExistingPort;
        }

        if (int.TryParse(raw, out int port) && port is > 0 and <= 65535)
        {
            return port;
        }

        throw new InvalidOperationException($"Set {ExistingPortVariable} to a UDP port between 1 and 65535.");
    }

    private sealed record ExternalObserverMarker(string Host, int Port, string Observer, string ExpectedPlayer, DateTimeOffset ReadyUtc);

    private static void WriteReadyMarker(ExternalObserverMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        Directory.CreateDirectory(MarkerDirectory);
        string path = Path.Combine(MarkerDirectory, MarkerFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void DeleteReadyMarker()
    {
        string path = Path.Combine(MarkerDirectory, MarkerFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
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

    private static async Task<BedrockActorSnapshot> WaitForPlayerAsync(BedrockClientSession session, string username, TimeSpan budget, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            BedrockActorSnapshot? match = session.ActorSnapshots.FirstOrDefault(actor => actor.DisplayName == username);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"The existing player '{username}' was not visible within {budget.TotalMinutes:F0} minutes. Check the name and join state.");
    }

    private async Task WriteResultAsync(int port, string observerName, string playerName, ulong playerRuntimeId, int freshPayloads, int walkingMoves, ulong[] walkingTicks, double maxDisplacement, int roundTrips, bool isReady, int rejectedSends, string? failure, CancellationToken ct)
    {
        Directory.CreateDirectory(MarkerDirectory);
        var result = new
        {
            protocol = ProtocolVersion,
            host = "127.0.0.1",
            port,
            observer = observerName,
            externalPlayer = playerName,
            playerRuntimeId,
            freshPayloads,
            walkingMoves,
            walkingTicks = walkingTicks.Take(100).ToArray(),
            maxDisplacementBlocks = maxDisplacement,
            movePlayerRoundTrips = roundTrips,
            observerReady = isReady,
            observerRejectedSends = rejectedSends,
            failure,
        };
        await File.WriteAllTextAsync(Path.Combine(MarkerDirectory, ResultFileName), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"Detached observer result written to {Path.Combine(MarkerDirectory, ResultFileName)}.");
    }
}
