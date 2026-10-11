using System.Text.Json;
using Umpk.Client.Bedrock;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.Session;
using Umpk.Client.Bedrock.State.Actors;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Bedrock.IntegrationTests;

public sealed class ActorObservationsLiveTests(ITestOutputHelper output)
{
    private const string FirstPlayerName = "UmpkLiveA";
    private const string SecondPlayerName = "UmpkLiveB";

    [BedrockLiveFact]
    public async Task TwoPlayerSessionExposesAuthoritativeActorObservations()
    {
        BedrockServerHarness.EnsureLinuxDistribution();
        using var overallTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        CancellationToken ct = overallTimeout.Token;
        await using var harness = new BedrockServerHarness();
        await harness.StartAsync(ct);
        output.WriteLine($"BDS ready on IPv4 port {harness.Port} (pid {harness.ProcessId}, exe {harness.ExecutableSha256}).");

        await using var firstClient = CreateClient(harness.Port, FirstPlayerName);
        await using BedrockClientSession first = await firstClient.ConnectAsync(ct);
        try
        {
            await RunTwoPlayerScenarioAsync(harness, firstClient, first, ct);
        }
        catch (Exception exception)
        {
            await WriteFailureEvidenceAsync(harness, first, exception, ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RunTwoPlayerScenarioAsync(
        BedrockServerHarness harness,
        BedrockClient firstClient,
        BedrockClientSession first,
        CancellationToken ct)
    {
        BedrockSessionSnapshot firstReady = await first.WaitUntilReadyAsync(ct);
        Assert.True(firstReady.IsReady);

        await using var secondClient = CreateClient(harness.Port, SecondPlayerName);
        await using BedrockClientSession second = await secondClient.ConnectAsync(ct);
        BedrockSessionSnapshot secondReady = await second.WaitUntilReadyAsync(ct);
        Assert.True(secondReady.IsReady);

        BedrockActorSnapshot secondSeenByFirst = await WaitForPlayerAsync(first, SecondPlayerName, ct);
        BedrockActorSnapshot firstSeenBySecond = await WaitForPlayerAsync(second, FirstPlayerName, ct);

        output.WriteLine($"First session observes {SecondPlayerName} as runtime {secondSeenByFirst.Handle.RuntimeEntityId}, unique {secondSeenByFirst.Handle.UniqueEntityId}.");
        output.WriteLine($"Second session observes {FirstPlayerName} as runtime {firstSeenBySecond.Handle.RuntimeEntityId}, unique {firstSeenBySecond.Handle.UniqueEntityId}.");

        await RunObservationProvocationsAsync(harness, first, second, ct);

        await second.DisconnectAsync(ct);
        bool removalObserved = await WaitForConditionAsync(
            () => first.ActorSnapshots.All(static actor => actor.DisplayName != SecondPlayerName),
            TimeSpan.FromSeconds(30),
            ct);
        output.WriteLine(removalObserved
            ? $"First session observed the removal of {SecondPlayerName}."
            : $"First session still retains {SecondPlayerName} after disconnect; {first.StaleActorRemovals} stale removals counted.");

        int assertions = 0;
        Assert.False(first.Completion.IsCompleted); assertions++;
        Assert.NotEqual(0UL, secondSeenByFirst.Handle.RuntimeEntityId); assertions++;
        Assert.NotEqual(0L, secondSeenByFirst.Handle.UniqueEntityId); assertions++;
        Assert.Equal(first.Snapshot.ActorWorldGeneration, secondSeenByFirst.Handle.WorldGeneration); assertions++;
        Assert.True(removalObserved); assertions++;

        try
        {
            await WriteEvidenceAsync(harness, first, second, ct).ConfigureAwait(false);
        }
        finally
        {
            await first.DisconnectAsync(ct);
            await firstClient.DisposeAsync();
            await secondClient.DisposeAsync();
        }

        BedrockTermination secondTermination = await second.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.LocalDisconnect, secondTermination.Reason); assertions++;
        BedrockTermination firstTermination = await first.Completion.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(BedrockSessionEndReason.LocalDisconnect, firstTermination.Reason); assertions++;

        IReadOnlyList<string> playerLines = harness.FindPlayerLines(SecondPlayerName);
        Assert.NotEmpty(playerLines); assertions++;
        output.WriteLine($"BDS observed the second player: {string.Join(" | ", playerLines.Take(3))}");
        Assert.True(assertions > 0);
    }

    private async Task RunObservationProvocationsAsync(
        BedrockServerHarness harness,
        BedrockClientSession first,
        BedrockClientSession second,
        CancellationToken ct)
    {
        await harness.SendCommandAsync($"effect {SecondPlayerName} speed 120 1 true", ct);
        bool effectObserved = await WaitForConditionAsync(
            () => first.ActorSnapshots.Any(static actor => actor.DisplayName == SecondPlayerName && actor.Effects.Length > 0),
            TimeSpan.FromSeconds(30),
            ct);
        output.WriteLine(effectObserved
            ? $"First session observed a provoked effect on {SecondPlayerName}."
            : $"First session observed no provoked effect on {SecondPlayerName}; post-join IDs: {string.Join(",", first.PostJoinPacketIds.Distinct().Order())}.");
        Assert.True(effectObserved);
        Assert.Contains(28, first.PostJoinPacketIds);

        bool attributeObserved = await WaitForConditionAsync(
            () => second.ActorSnapshots.Any(static actor =>
                actor.Kind == BedrockActorKind.LocalPlayer && actor.Attributes.Any(static attribute => attribute.Modifiers.Length > 0)),
            TimeSpan.FromSeconds(60),
            ct);
        output.WriteLine(attributeObserved
            ? $"Second session observed its own provoked attribute modifier."
            : $"Second session observed no attribute modifier; post-join IDs: {string.Join(",", second.PostJoinPacketIds.Distinct().Order())}.");
        Assert.True(attributeObserved);
        Assert.Contains(29, second.PostJoinPacketIds);

        await RunMobEquipmentProvocationsAsync(harness, first, ct);
        await RunLinkProvocationAsync(harness, first, ct);
    }

    private async Task RunMobEquipmentProvocationsAsync(
        BedrockServerHarness harness,
        BedrockClientSession first,
        CancellationToken ct)
    {
        BedrockActorSnapshot? anchor = first.ActorSnapshots.FirstOrDefault(static actor => actor.DisplayName == SecondPlayerName);
        if (anchor?.Position is not { } anchorPosition)
        {
            output.WriteLine($"First session lost {SecondPlayerName} before the mob provocation; equipment stays hand-vector bound.");
            return;
        }

        int summonX = (int)MathF.Floor(anchorPosition.X);
        int summonY = (int)MathF.Floor(anchorPosition.Y);
        int summonZ = (int)MathF.Floor(anchorPosition.Z);
        await harness.SendCommandAsync($"summon zombie {summonX} {summonY} {summonZ}", ct);
        bool zombieObserved = await WaitForConditionAsync(
            () => first.ActorSnapshots.Any(static actor => actor.EntityType == "minecraft:zombie"),
            TimeSpan.FromSeconds(30),
            ct);
        output.WriteLine(zombieObserved
            ? "First session observed a summoned zombie."
            : "First session observed no summoned zombie; equipment stays hand-vector bound.");
        Assert.True(zombieObserved);

        await harness.SendCommandAsync("replaceitem entity @e[type=zombie,c=1] slot.weapon.mainhand 0 iron_sword 1", ct);
        bool heldObserved = await WaitForConditionAsync(
            () => first.ActorSnapshots.Any(static actor =>
                actor.EntityType == "minecraft:zombie" && actor.HeldItem is not null && actor.HeldItem.Item.NetworkId != 0),
            TimeSpan.FromSeconds(30),
            ct);
        output.WriteLine(heldObserved
            ? "First session observed provoked zombie held equipment."
            : "First session observed no zombie held equipment.");
        Assert.True(heldObserved);
        Assert.Contains(31, first.PostJoinPacketIds);

        await harness.SendCommandAsync("replaceitem entity @e[type=zombie,c=1] slot.armor.head 0 iron_helmet 1", ct);
        bool armorObserved = await WaitForConditionAsync(
            () => first.ActorSnapshots.Any(static actor =>
                actor.EntityType == "minecraft:zombie" && actor.Armor is not null && actor.Armor.Helmet.NetworkId != 0),
            TimeSpan.FromSeconds(30),
            ct);
        output.WriteLine(armorObserved
            ? "First session observed provoked zombie armor equipment."
            : "First session observed no zombie armor equipment.");
        Assert.True(armorObserved);
        Assert.Contains(32, first.PostJoinPacketIds);
    }

    private async Task RunLinkProvocationAsync(
        BedrockServerHarness harness,
        BedrockClientSession first,
        CancellationToken ct)
    {
        BedrockActorSnapshot? target = first.ActorSnapshots.FirstOrDefault(static actor => actor.DisplayName == SecondPlayerName);
        if (target?.Position is { } position)
        {
            int summonX = (int)MathF.Floor(position.X);
            int summonY = (int)MathF.Floor(position.Y);
            int summonZ = (int)MathF.Floor(position.Z);
            await harness.SendCommandAsync($"summon horse {summonX} {summonY} {summonZ}", ct);
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            await harness.SendCommandAsync($"ride {SecondPlayerName} start_riding @e[type=horse,c=1]", ct);
            bool linkObserved = await WaitForConditionAsync(
                () => first.ActorSnapshots.Any(static actor =>
                    actor.DisplayName == SecondPlayerName && actor.RidingRuntimeEntityId is not null)
                    || first.PostJoinPacketIds.Contains(41),
                TimeSpan.FromSeconds(30),
                ct);
            output.WriteLine(linkObserved
                ? $"First session observed a provoked rider link for {SecondPlayerName}."
                : $"First session observed no rider link for {SecondPlayerName}; the link leg stays open.");
        }
        else
        {
            output.WriteLine($"First session lost {SecondPlayerName} before the link provocation; the link leg stays open.");
        }
    }

    private async Task WriteFailureEvidenceAsync(
        BedrockServerHarness harness,
        BedrockClientSession first,
        Exception exception,
        CancellationToken ct)
    {
        output.WriteLine($"Live actor failure: {exception}");
        string termination = first.Completion.IsCompletedSuccessfully
            ? first.Completion.Result.ToString()
            : "termination-pending";
        var evidence = new
        {
            failure = exception.ToString(),
            termination,
            snapshot = first.Snapshot.ToString(),
            transcriptTail = first.WireTranscript.TakeLast(40).Select(static evt => new
            {
                direction = evt.Direction.ToString(),
                packetId = evt.PacketId,
            }).ToArray(),
            actorCount = first.ActorSnapshots.Count,
            rejectedSendAttempts = first.RejectedSendAttempts,
        };
        string path = Path.Combine(harness.EvidenceDirectory, "actors-failure.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"Failure evidence written to {path}.");
        output.WriteLine($"Transcript tail: {string.Join(" ", evidence.transcriptTail.Select(static evt => $"{(evt.direction == "Inbound" ? '<' : '>')}{evt.packetId}"))}");
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

    private async Task WriteEvidenceAsync(
        BedrockServerHarness harness,
        BedrockClientSession first,
        BedrockClientSession second,
        CancellationToken ct)
    {
        static object DescribeActor(BedrockActorSnapshot actor) => new
        {
            kind = actor.Kind.ToString(),
            displayName = actor.DisplayName,
            uuid = actor.Uuid?.ToString(),
            entityType = actor.EntityType,
            runtimeEntityId = actor.Handle.RuntimeEntityId,
            uniqueEntityId = actor.Handle.UniqueEntityId,
            worldGeneration = actor.Handle.WorldGeneration,
            position = actor.Position?.ToString(),
            velocity = actor.Velocity?.ToString(),
            lastServerTick = actor.LastServerTick,
            metadataObserved = actor.MetadataObserved,
            metadataEntries = actor.Metadata.Length,
            baseAttributes = actor.BaseAttributes.Length,
            properties = actor.Properties.Ints.Length + actor.Properties.Floats.Length,
            abilities = actor.Abilities.Length,
            locationHidden = actor.LocationHidden,
            effects = actor.Effects.Select(static effect => new { effect.EffectId, effect.Amplifier, effect.Duration }).ToArray(),
            attributes = actor.Attributes.Select(static attribute => new { attribute.Name, attribute.Current, attribute.Max }).ToArray(),
            heldItem = actor.HeldItem is null ? null : new { networkId = actor.HeldItem.Item.NetworkId, count = actor.HeldItem.Item.Count },
            armorHelmet = actor.Armor is null ? (int?)null : actor.Armor.Helmet.NetworkId,
            ridingRuntimeEntityId = actor.RidingRuntimeEntityId,
        };
        var evidence = new
        {
            bds = new
            {
                packageVersion = "1.26.45.1-1",
                sourceArtifactSha256 = BedrockServerHarness.ExpectedSourceSha256,
                executableSha256 = harness.ExecutableSha256,
                expectedExecutableSha256 = BedrockServerHarness.ExpectedExecutableSha256,
                ipv4Port = harness.Port,
                ipv6Port = harness.IPv6Port,
                processId = harness.ProcessId,
                runDirectory = harness.RunDirectory,
            },
            protocol = 2169,
            first = new
            {
                player = FirstPlayerName,
                snapshot = DescribeSnapshot(first.Snapshot),
                actors = first.ActorSnapshots.Select(DescribeActor).ToArray(),
                actorEvents = first.ActorEvents
                    .GroupBy(static evt => evt.Kind.ToString())
                    .ToDictionary(static group => group.Key, static group => group.Count()),
                wireWitnesses = first.ActorWireWitnesses.Select(static witness => new
                {
                    packetId = witness.PacketId,
                    firstBytesHex = witness.FirstBytesHex,
                    totalLength = witness.TotalLength,
                }).ToArray(),
                postJoinPacketIds = first.PostJoinPacketIds
                    .GroupBy(static id => id)
                    .ToDictionary(static group => group.Key, static group => group.Count()),
                rejectedSendAttempts = first.RejectedSendAttempts,
                unknownActorUpdates = first.UnknownActorUpdates,
                staleActorRemovals = first.StaleActorRemovals,
            },
            second = new
            {
                player = SecondPlayerName,
                snapshot = DescribeSnapshot(second.Snapshot),
                actors = second.ActorSnapshots.Select(DescribeActor).ToArray(),
                rejectedSendAttempts = second.RejectedSendAttempts,
            },
            serverPlayerLines = harness.FindPlayerLines(SecondPlayerName).ToArray(),
        };
        string path = Path.Combine(harness.EvidenceDirectory, "actors-evidence.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        output.WriteLine($"Actor evidence written to {path}.");

        static object DescribeSnapshot(BedrockSessionSnapshot snapshot) => new
        {
            snapshot.IsReady,
            snapshot.LocalEntityId,
            snapshot.LocalRuntimeEntityId,
            snapshot.ActorCount,
            snapshot.ActorWorldGeneration,
            snapshot.ActorObservationsAvailable,
            snapshot.LevelChunksObserved,
            Termination = snapshot.Termination?.ToString(),
        };
    }
}
