using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Umpk.Text;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    public static IEnumerable<object[]> AdventureComponentReleases()
    {
        string selected = Environment.GetEnvironmentVariable("UMPK_ADVENTURE_RELEASES") ?? "1.20.6,1.21.1,1.21.3,1.21.4";
        string[] releases = selected.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(releases.Length, releases.Distinct(StringComparer.Ordinal).Count());
        Assert.NotEmpty(releases);
        foreach (string release in releases)
        {
            Assert.Contains(release, new[] { "1.20.6", "1.21.1", "1.21.3", "1.21.4" });
            Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
            yield return [release, version.Version.Protocol];
        }
    }

    [NightlyTheory]
    [MemberData(nameof(AdventureComponentReleases))]
    public async Task AdventureComponentsDriveNativeMiningAndEquivalentWireProgress(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        string settingsFile = Environment.GetEnvironmentVariable("UMPK_ADVENTURE_GENERATOR_SETTINGS_FILE") ?? throw new InvalidOperationException("Supply the pinned native generator settings before launch.");
        byte[] settingsBytes = await File.ReadAllBytesAsync(settingsFile, ct);
        Assert.Equal("65c9e53a491c688c58eac7422eb4f66b8ca01cfb16be4d309fd3c2a31cb5a898", Convert.ToHexStringLower(SHA256.HashData(settingsBytes)));
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct, flatWorldSettings: System.Text.Encoding.UTF8.GetString(settingsBytes));
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        string sourceFile = Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.IntegrationTests", "VanillaInteractionAcceptanceLiveTests.Adventure.cs");
        await server.WriteAsync("adventure-source.json", new { sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(sourceFile, ct))) });
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        var tagGate = new Lock();
        var configurationTags = new List<AdventureTagFrame>();
        long tagBytes = 0;
        bool tagOverflow = false;
        void Observe(PacketObservation observation)
        {
            capture.Observe(observation);
            if (!AdventureConfigurationTagsMatch(observation.Phase, observation.Flow, observation.DecodedPacket))
                return;
            lock (tagGate)
            {
                tagBytes += observation.PayloadLength;
                if (tagBytes > 20 * 1024 * 1024)
                {
                    tagOverflow = true;
                    return;
                }
                configurationTags.Add(new(observation.Phase, observation.Flow, observation.WireId, observation.CopyPayload(), (ClientboundConfigUpdateTagsPacket)observation.DecodedPacket!));
            }
        }
        client.PacketFrameObserved += Observe;
        var scenarios = new List<object>();
        var observationMarkers = new List<string>();
        var gatingWitnesses = new List<object>();
        int expectedNativeDestroyDelay = 0;
        bool passed = false;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded && c.State.Self.OnGround, "native dry flat spawn", ct);
            Assert.True(client.State.Features.Physics);
            Vec3d initial = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(initial.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(initial.Z) >> 4) * 16 + 8;
            await VerifyAdventureFlatGenerator(server, client, x, z, ct);
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync("gamerule doMobSpawning false", ct);
            await server.ConsoleAsync("gamerule doTileDrops false", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {x + .5:R} 100 {z + .5:R}"), ct);
            await server.ConsoleAsync($"clear {Username}", ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.OnGround && Math.Abs(c.State.Self.Position.X - x - .5) < .01 && Math.Abs(c.State.Self.Position.Y - 100) < .01 && Math.Abs(c.State.Self.Position.Z - z - .5) < .01, "grounded dry interaction owner", ct);
            await WaitAsync(() => capture.Frames.Any(frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == x >> 4 && chunk.ChunkZ == z >> 4), "native full-column frame", ct);
            Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.Select(entity => entity.Id).ToArray(), ct));
            await Marker("execute unless entity @e[type=!minecraft:player]", "zero-native-peers");
            int stoneId = client.State.Registries!.Blocks[Identifier.Minecraft("stone")].NetworkId;
            AdventureTagFrame[] initialTags;
            lock (tagGate)
                initialTags = [.. configurationTags];
            Assert.NotEmpty(initialTags);
            TagRegistry blockTags = Assert.Single(initialTags[^1].Packet.Registries, registry => registry.Registry == RegistryIds.Block);
            TagEntry pickaxe = Assert.Single(blockTags.Tags, tag => tag.TagId == Identifier.Minecraft("mineable/pickaxe"));
            Assert.Contains(stoneId, pickaxe.Ids);
            await server.WriteAsync("adventure-tag-state.json", new { synchronizedBlockTag = "minecraft:mineable/pickaxe", stoneId, member = true, originalConfigurationTagPackets = configurationTags.Count });

            // Denied/unknown controls precede a successful STOP, which natively leaves five held destroy-delay calls for the next request.
            await Denied("absent", new(x + 2, 100, z + 1));
            await Denied("nonmatch", new(x + 2, 100, z + 2));
            await Unknown("state", new(x + 1, 100, z + 2), "oak_log");
            await Unknown("nbt", new(x, 100, z + 2), "chest");
            await Ordinary("survival-control", new(x + 2, 100, z - 2), GameMode.Survival, "absent");
            await Ordinary("adventure-direct", new(x + 2, 100, z - 1), GameMode.Adventure, "direct");
            await Ordinary("adventure-native-tag", new(x + 2, 100, z), GameMode.Adventure, "tag");
            await Identity(new(x - 1, 100, z + 2), changedTooltip: false);
            await Identity(new(x - 2, 100, z + 2), changedTooltip: true);

            int ordinaryReplay = capture.AssertRoundTrips(client.State.Registries!);
            AdventureTagFrame[] tags;
            lock (tagGate)
                tags = [.. configurationTags];
            Assert.False(tagOverflow);
            Assert.False(capture.Overflowed);
            Assert.True(capture.PayloadBytes + tagBytes <= 20 * 1024 * 1024, "The combined configuration-tag and Play capture exceeds the unchanged cap.");
            int dedicatedReplay = ReplayAdventureFrames(capture.Frames, tags, version, client.State.Registries!);
            var causalDispositions = new List<object>();
            int causalReplay = ReplayAdventureCausalFrames(capture.Frames, version, client.State.Registries!, observationMarkers, causalDispositions);
            await server.WriteAsync("adventure-causal-replay.json", new { expectedMarkers = observationMarkers, gatingWitnesses, byteIdenticalFrames = causalReplay, frames = causalDispositions, scope = "Every exact causal marker and game-mode/ability gate requires decoded-field assertions and byte identity. Ambient translated setup chat retains separate semantic and byte-difference dispositions." });
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "Adventure native peer disconnect", ct);
            await server.WriteAsync("adventure-acceptance.json", new { release, protocol, clientProfile = version.Version.Name, physics = true, scenarios, ordinaryReplay, dedicatedReplay, causalReplay, causalMarkerCount = observationMarkers.Count, gatingWitnesses = gatingWitnesses.Count, configurationTagPackets = tags.Length, totalPayloadBytes = capture.PayloadBytes + tagBytes, peerObservedDisconnect = true, originalMinecraftInputOrNetworkExecuted = false, nativeClaim = "UMPK public action packets and server outcomes, supported by separately retained original component methods and source ordering", customTagReloadClaim = false });
            passed = true;

            async Task Marker(string condition, string label)
            {
                string marker = "adventure-" + label + "-" + Guid.NewGuid().ToString("N");
                Assert.DoesNotContain(marker, observationMarkers);
                observationMarkers.Add(marker);
                await server.CommandAsync(condition + " run say " + marker, ct);
                await WaitAsync(() => server.ServerLog.Split('\n').Any(line => line.TrimEnd('\r').EndsWith("[Rcon] " + marker, StringComparison.Ordinal)) && capture.Frames.Any(frame => AdventureMarkerMatches(frame.Packet, marker)), "independent native predicate and exact ordered marker " + label, ct);
                await client.InvokeAsync(_ => true, ct);
            }

            async Task Prepare(BlockPos target, string block, GameMode mode)
            {
                Assert.False(await client.InvokeAsync(c => c.State.Inventory.HasOpenContainer, ct));
                Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.Select(entity => entity.Id).ToArray(), ct));
                int modeMark = capture.Mark();
                GameMode previousMode = await client.InvokeAsync(c => c.State.Self.GameMode, ct);
                await server.ConsoleAsync($"gamemode {mode.ToString().ToLowerInvariant()} {Username}", ct);
                await WaitAsync(client, c => c.State.Self.GameMode == mode && !c.State.Self.InstantBuild && c.State.Self.OnGround, "native game mode/ability", ct);
                await Marker($"execute if entity @a[name={Username},gamemode={mode.ToString().ToLowerInvariant()}]", "mode");
                CapturedFrame[] modeFrames = capture.Frames;
                int gameIndex = Array.FindLastIndex(modeFrames, frame => frame.Packet is ClientboundGameEventPacket { Event: 3 } game && game.Param == (float)mode);
                int abilitiesIndex = Array.FindLastIndex(modeFrames, frame => frame.Packet is ClientboundPlayerAbilitiesPacket);
                Assert.True(gameIndex >= 0 && abilitiesIndex >= 0);
                var abilities = Assert.IsType<ClientboundPlayerAbilitiesPacket>(modeFrames[abilitiesIndex].Packet);
                Assert.Equal(0, abilities.Flags & 8);
                if (previousMode != mode)
                {
                    Assert.True(gameIndex >= modeMark && abilitiesIndex >= modeMark, "A native mode transition must supply both permission-gating packets before its causal marker.");
                }
                gatingWitnesses.Add(new { mode, previousMode, gameIndex, abilitiesIndex, abilities.Flags, abilities.FlyingSpeed, abilities.WalkingSpeed });
                await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:air", ct);
                await WaitAsync(client, c => c.State.World.GetBlock(target).IsAir, "fresh air target transition", ct);
                await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:{block}", ct);
                await WaitAsync(client, c => c.State.World.GetBlock(target).Block.Id == Identifier.Minecraft(block) && c.State.World.GetColumn(target) is not null, "fresh native block target", ct);
                await AssertBlockAsync(server, protocol, target, block, ct);
                Assert.Equal((x >> 4, z >> 4), (target.X >> 4, target.Z >> 4));
                Assert.Equal(Identifier.Minecraft("stone"), await client.InvokeAsync(c => c.State.World.GetBlock(target.Below()).Block.Id, ct));
                await Marker($"execute if block {target.X} {target.Y} {target.Z} minecraft:{block}", "target");
            }

            async Task<AdventureHeldObservation> Hold(string item, string kind, int count = 1, bool tooltip = true)
            {
                int mark = capture.Mark();
                var applied = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable changed = client.Events.Subscribe<ContainerContentChanged>(evt =>
                {
                    if (evt.WindowId == 0 && client.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft(item) && client.State.Inventory.GetSlot(36).Count == count && AdventureWirePredicateMatches(client.State.Inventory.GetSlot(36), kind, tooltip, client.State.Registries!))
                        applied.TrySetResult(capture.Mark());
                });
                string component = AdventureComponentSyntax(kind, tooltip);
                await server.ConsoleAsync($"item replace entity {Username} weapon.mainhand with minecraft:{item}{(component.Length == 0 ? "" : "[minecraft:can_break=" + component + "]")} {count}", ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundContainerSetSlotPacket { ContainerId: 0, Slot: 36 } slot && slot.Item.Item.Id == Identifier.Minecraft(item) && slot.Item.Count == count && AdventureWirePredicateMatches(slot.Item, kind, tooltip, client.State.Registries!)), "actual native replacement slot packet", ct);
                int ownerMark = await applied.Task.WaitAsync(ct);
                await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft(item) && c.State.Inventory.GetSlot(36).Count == count && AdventureWirePredicateMatches(c.State.Inventory.GetSlot(36), kind, tooltip, c.State.Registries!), "replacement packet applied by inventory owner", ct);
                string predicate = $"minecraft:{item}[count={count}," + (kind == "absent" ? "!minecraft:can_break" : "minecraft:can_break=" + component) + "]";
                await Marker($"execute if items entity {Username} weapon.mainhand {predicate}", "held-item");
                AdventureModePredicateComponent? observed = await client.InvokeAsync(c => c.State.Inventory.GetSlot(36).Components.Get(DataComponents.CanBreak), ct);
                return new(observed, ownerMark);
            }

            async Task ObserveWindow(int mark, BlockPos target, string expected)
            {
                await Task.Delay(TimeSpan.FromSeconds(1.1), ct);
                await Marker($"execute if block {target.X} {target.Y} {target.Z} minecraft:{expected}", "post-action");
                CapturedFrame[] frames = capture.Since(mark);
                CapturedFrame[] actions = frames.Where(IsMiningOrAnimation).ToArray();
                if (actions.Length > 0)
                    Assert.True(frames[^1].Milliseconds - actions[^1].Milliseconds >= 900, "Native marker must extend the post-action capture window.");
            }

            async Task Ordinary(string label, BlockPos target, GameMode mode, string kind)
            {
                await Prepare(target, "stone", mode);
                await Hold("diamond_pickaxe", kind);
                if (kind == "tag")
                    await Marker($"execute if block {target.X} {target.Y} {target.Z} #minecraft:mineable/pickaxe", "native-block-tag");
                int mark = capture.Mark();
                await client.Actions.Interaction.DigBlockAsync(target, Direction.West, ct);
                await WaitAsync(client, c => c.State.World.GetBlock(target).IsAir, "native permitted stone destruction", ct);
                await AssertBlockAsync(server, protocol, target, "air", ct);
                await ObserveWindow(mark, target, "air");
                CapturedFrame[] frames = capture.Since(mark);
                CapturedFrame[] actions = frames.Where(IsMiningOrAnimation).ToArray();
                Assert.Equal(9 + expectedNativeDestroyDelay, actions.Length);
                Assert.Equal(7 + expectedNativeDestroyDelay, actions.Count(frame => frame.Packet is ServerboundSwingPacket));
                Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(actions[0].Packet).Action);
                Assert.Equal(2, Assert.IsType<ServerboundPlayerActionPacket>(actions[^2].Packet).Action);
                AssertAdventureSwing(actions[^1].Packet);
                Assert.All(actions.Skip(1).SkipLast(2), frame => AssertAdventureSwing(frame.Packet));
                Assert.True(actions[^2].Milliseconds - actions[0].Milliseconds >= 150);
                AssertAdventureActionFields(frames, target);
                scenarios.Add(new { label, mode, target, kind, nativeBlockAfter = "air", precedingNativeDestroyDelay = expectedNativeDestroyDelay, actionOrder = actions.Select(frame => AdventurePacketName(frame.Packet)).ToArray() });
                expectedNativeDestroyDelay = 5;
            }

            async Task Denied(string kind, BlockPos target)
            {
                await Prepare(target, "stone", GameMode.Adventure);
                await Hold("diamond_pickaxe", kind);
                int mark = capture.Mark();
                using var held = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task mining = client.Actions.Interaction.DigBlockAsync(target, Direction.West, held.Token);
                try
                {
                    await WaitAsync(() => capture.Since(mark).Any(IsMiningOrAnimation), "native initial denied attack swing", ct);
                    await ObserveWindow(mark, target, "stone");
                    Assert.False(mining.IsCompleted);
                    AssertAdventureSwing(Assert.Single(capture.Since(mark), IsMiningOrAnimation).Packet);
                }
                finally
                {
                    await held.CancelAsync();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
                }
                await ObserveWindow(mark, target, "stone");
                AssertAdventureSwing(Assert.Single(capture.Since(mark), IsMiningOrAnimation).Packet);
                scenarios.Add(new { label = "known-denial-" + kind, target, noStartOrAbort = true, initialMainHandSwing = 1, heldRequestRetained = true, nativeBlockAfter = "stone" });
            }

            async Task Unknown(string kind, BlockPos target, string block)
            {
                await Prepare(target, block, GameMode.Adventure);
                await Hold("diamond_pickaxe", kind);
                if (kind == "state")
                    await Marker($"execute if block {target.X} {target.Y} {target.Z} minecraft:oak_log[axis=y]", "native-state-context");
                else
                    await Marker($"execute if block {target.X} {target.Y} {target.Z} minecraft:chest{{id:\"minecraft:chest\"}}", "native-nbt-context");
                int mark = capture.Mark();
                InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => client.Actions.Interaction.DigBlockAsync(target, Direction.West, ct));
                Assert.Contains("requires unavailable", error.Message, StringComparison.Ordinal);
                await ObserveWindow(mark, target, block);
                Assert.DoesNotContain(capture.Since(mark), IsMiningOrAnimation);
                scenarios.Add(new { label = "explicit-unknown-" + kind, target, error = error.Message, nativeBlockAfter = block, nativePermissionNotClaimedExecuted = true });
            }

            async Task Identity(BlockPos target, bool changedTooltip)
            {
                await Prepare(target, "stone", GameMode.Adventure);
                AdventureModePredicateComponent original = (await Hold("stick", "direct")).Component!;
                int mark = capture.Mark();
                using var held = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task mining = client.Actions.Interaction.DigBlockAsync(target, Direction.West, held.Token);
                try
                {
                    // Native STOP retained five delay calls. Consume them through actual held input, then observe two progress phases before the replacement.
                    await WaitAsync(() => capture.Since(mark).Count(frame => IsMiningOrAnimation(frame) && frame.Packet is ServerboundSwingPacket) >= expectedNativeDestroyDelay + 3, "native delay consumed and progress-phase swings before replacement", ct);
                    Assert.False(mining.IsCompleted);
                    Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(capture.Since(mark), frame => frame.Packet is ServerboundPlayerActionPacket).Packet).Action);
                    int replacementMark = capture.Mark();
                    AdventureHeldObservation applied = await Hold("stick", "direct", 2, tooltip: !changedTooltip);
                    AdventureModePredicateComponent replacement = applied.Component!;
                    Assert.NotSame(original, replacement);
                    Assert.Equal(!changedTooltip, original.ShowInTooltip == replacement.ShowInTooltip);
                    await mining.WaitAsync(ct);
                    await WaitAsync(client, c => c.State.World.GetBlock(target).IsAir, "native identity-control stone destruction", ct);
                    await ObserveWindow(mark, target, "air");
                    CapturedFrame[] frames = capture.Since(mark);
                    AssertAdventureActionFields(frames, target);
                    var actions = frames.Where(frame => frame.Flow == PacketFlow.Serverbound && frame.Packet is ServerboundPlayerActionPacket).Select(frame => Assert.IsType<ServerboundPlayerActionPacket>(frame.Packet)).ToArray();
                    Assert.Equal(changedTooltip ? new[] { 0, 1, 0, 2 } : new[] { 0, 2 }, actions.Select(action => action.Action));
                    CapturedFrame[] afterReplacement = capture.Since(replacementMark);
                    int slotIndex = Array.FindIndex(afterReplacement, frame => frame.Packet is ClientboundContainerSetSlotPacket { ContainerId: 0, Slot: 36, Item.Count: 2 });
                    Assert.True(slotIndex >= 0);
                    int appliedIndex = applied.OwnerFrameMark - replacementMark;
                    Assert.InRange(appliedIndex, slotIndex + 1, afterReplacement.Length);
                    // Raw observations run on the read loop. The inventory event runs after application on the session owner, before the next mining tick.
                    var firstAfterSlot = afterReplacement.Skip(appliedIndex).Where(IsMiningOrAnimation).ToArray();
                    if (changedTooltip)
                    {
                        Assert.True(firstAfterSlot.Length >= 3);
                        Assert.Equal(1, Assert.IsType<ServerboundPlayerActionPacket>(firstAfterSlot[0].Packet).Action);
                        Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(firstAfterSlot[1].Packet).Action);
                        AssertAdventureSwing(firstAfterSlot[2].Packet);
                    }
                    else
                        AssertAdventureSwing(firstAfterSlot[0].Packet);
                    await Marker($"execute if items entity {Username} weapon.mainhand minecraft:stick[count=2,minecraft:can_break={AdventureComponentSyntax("direct", !changedTooltip)}]", "post-identity-item");
                    scenarios.Add(new { label = changedTooltip ? "tooltip-difference-restarts" : "equal-wire-predicate-retains-progress", target, precedingNativeDestroyDelay = expectedNativeDestroyDelay, progressPhasePreconditionSwings = expectedNativeDestroyDelay + 3, actualCountReplacement = "1-to-2", actualFreshComponent = true, nativeSlotFrameIndex = slotIndex, inventoryOwnerAppliedIndex = appliedIndex, nativeBlockAfter = "air", actionOrder = actions.Select(action => action.Action).ToArray(), packetOrder = frames.Where(IsMiningOrAnimation).Select(frame => AdventurePacketName(frame.Packet)).ToArray() });
                    expectedNativeDestroyDelay = 5;
                }
                finally
                {
                    await held.CancelAsync();
                    if (!mining.IsCompleted)
                        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
                }
            }
        }
        finally
        {
            capture.Stop();
            client.PacketFrameObserved -= Observe;
            await server.WriteAsync("adventure-capture.json", capture.Frames);
            await server.WriteAsync("adventure-causal-markers.json", observationMarkers);
            await server.WriteAsync("adventure-configuration-tags.json", configurationTags.Select(AdventureConfigurationTagEvidence).ToArray());
            await server.WriteAsync("adventure-result.json", new { passed, release, protocol, capture.PayloadBytes, capture.Overflowed, tagBytes, tagOverflow });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static string AdventureComponentSyntax(string kind, bool tooltip)
    {
        string predicate = kind switch
        {
            "absent" => "",
            "direct" => "{blocks:[\"minecraft:stone\"]}",
            "tag" => "{blocks:\"#minecraft:mineable/pickaxe\"}",
            "nonmatch" => "{blocks:[\"minecraft:dirt\"]}",
            "state" => "{blocks:[\"minecraft:oak_log\"],state:{axis:\"y\"}}",
            "nbt" => "{blocks:[\"minecraft:chest\"],nbt:{id:\"minecraft:chest\"}}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return kind == "absent" ? "" : "{predicates:[" + predicate + "],show_in_tooltip:" + (tooltip ? "true" : "false") + "}";
    }

    private static bool AdventureWirePredicateMatches(ItemStack item, string kind, bool tooltip, RegistryAccess registries)
    {
        AdventureModePredicateComponent? component = item.Components.Get(DataComponents.CanBreak);
        if (kind == "absent")
            return component is null;
        if (component is null || component.ShowInTooltip != tooltip || component.Predicates.Count != 1)
            return false;
        BlockPredicateEntry predicate = component.Predicates[0];
        string block = kind switch { "nonmatch" => "dirt", "state" => "oak_log", "nbt" => "chest", _ => "stone" };
        bool blocks = kind == "tag" ? predicate.Blocks is { Tag: { } tag, Ids.Count: 0 } && tag == Identifier.Minecraft("mineable/pickaxe")
            : predicate.Blocks is { Tag: null } set && set.Ids.SequenceEqual([registries.Blocks[Identifier.Minecraft(block)].NetworkId]);
        bool state = kind == "state" ? predicate.Properties is { Count: 1 } properties && properties[0] == new StatePropertyMatcher("axis", true, "y") : predicate.Properties is null;
        bool nbt = kind == "nbt" ? predicate.Nbt?.GetString("id") == "minecraft:chest" : predicate.Nbt is null;
        return blocks && state && nbt;
    }

    private static void AssertAdventureSwing(object? packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.Equal(0, swing.Hand);
        Assert.True(swing.HasHand);
    }

    private static void AssertAdventureActionFields(CapturedFrame[] frames, BlockPos target)
    {
        int priorSequence = -1;
        foreach (CapturedFrame frame in frames.Where(IsMiningOrAnimation))
        {
            if (frame.Packet is ServerboundPlayerActionPacket action)
            {
                Assert.Equal(target, action.Position);
                Assert.Equal((byte)Direction.West, action.Direction);
                if (action.Action == 1)
                    Assert.Equal(0, action.Sequence);
                else
                {
                    Assert.True(action.Sequence is > 0 && action.Sequence > priorSequence);
                    priorSequence = action.Sequence!.Value;
                }
            }
            else
                AssertAdventureSwing(frame.Packet);
        }
    }

    private static string AdventurePacketName(object? packet) => packet switch
    {
        ServerboundPlayerActionPacket action => $"action{action.Action}/sequence{action.Sequence}",
        ServerboundSwingPacket swing => $"swing/hand{swing.Hand}/hasHand{swing.HasHand}",
        _ => throw new InvalidOperationException("Unexpected mining packet."),
    };

    private sealed record AdventureTagFrame(ProtocolPhase Phase, PacketFlow Flow, int WireId, byte[] Payload, ClientboundConfigUpdateTagsPacket Packet);
    private sealed record AdventureHeldObservation(AdventureModePredicateComponent? Component, int OwnerFrameMark);

    private static int ReplayAdventureFrames(CapturedFrame[] frames, AdventureTagFrame[] tags, JavaVersion version, RegistryAccess registries)
    {
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        int count = ReplayAdventureConfigurationTags(tags, version, registries);
        foreach (CapturedFrame frame in frames)
        {
            if (frame.Packet is not (ServerboundPlayerActionPacket or ServerboundSwingPacket or ClientboundContainerSetSlotPacket or ClientboundContainerSetContentPacket or ClientboundBlockUpdatePacket or ClientboundBlockChangedAckPacket or ClientboundUpdateTagsPacket))
                continue;
            PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, frame.Flow);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, context);
            if (frame.Packet is ClientboundContainerSetSlotPacket slot)
            {
                var actual = Assert.IsType<ClientboundContainerSetSlotPacket>(decoded);
                Assert.Equal((slot.ContainerId, slot.StateId, slot.Slot), (actual.ContainerId, actual.StateId, actual.Slot));
                Assert.Equal((slot.Item.Item.Id, slot.Item.Count, slot.Item.Damage), (actual.Item.Item.Id, actual.Item.Count, actual.Item.Damage));
                ComparePredicate(slot.Item.Components.Get(DataComponents.CanBreak), actual.Item.Components.Get(DataComponents.CanBreak));
            }
            else if (frame.Packet is ClientboundContainerSetContentPacket content)
            {
                var actual = Assert.IsType<ClientboundContainerSetContentPacket>(decoded);
                Assert.Equal((content.ContainerId, content.StateId, content.Items.Count), (actual.ContainerId, actual.StateId, actual.Items.Count));
                for (int index = 0; index < content.Items.Count; index++)
                {
                    Assert.Equal((content.Items[index].Item.Id, content.Items[index].Count), (actual.Items[index].Item.Id, actual.Items[index].Count));
                    ComparePredicate(content.Items[index].Components.Get(DataComponents.CanBreak), actual.Items[index].Components.Get(DataComponents.CanBreak));
                }
                Assert.Equal((content.CarriedItem.Item.Id, content.CarriedItem.Count, content.CarriedItem.Damage), (actual.CarriedItem.Item.Id, actual.CarriedItem.Count, actual.CarriedItem.Damage));
                ComparePredicate(content.CarriedItem.Components.Get(DataComponents.CanBreak), actual.CarriedItem.Components.Get(DataComponents.CanBreak));
            }
            else if (frame.Packet is ClientboundUpdateTagsPacket tagPacket)
                CompareAdventureTagRegistries(tagPacket.Registries, Assert.IsType<ClientboundUpdateTagsPacket>(decoded).Registries);
            else
                Assert.Equal(frame.Packet, decoded);
            RoundTrip(codec, frame.Payload, decoded);
        }
        Assert.True(count >= 20);
        return count;

        void RoundTrip(BoundPacketCodec codec, byte[] payload, object decoded)
        {
            var bytes = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(bytes);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(payload, bytes.WrittenSpan.ToArray());
            count++;
        }

        static void ComparePredicate(AdventureModePredicateComponent? expected, AdventureModePredicateComponent? actual)
        {
            if (expected is null)
            {
                Assert.Null(actual);
                return;
            }
            Assert.NotNull(actual);
            Assert.Equal(expected.ShowInTooltip, actual.ShowInTooltip);
            Assert.Equal(expected.Predicates.Count, actual.Predicates.Count);
            for (int index = 0; index < expected.Predicates.Count; index++)
            {
                BlockPredicateEntry first = expected.Predicates[index], second = actual.Predicates[index];
                Assert.Equal(first.Blocks?.Tag, second.Blocks?.Tag);
                Assert.Equal(first.Blocks?.Ids, second.Blocks?.Ids);
                Assert.Equal(first.Properties, second.Properties);
                Assert.Equal(first.Nbt, second.Nbt);
            }
        }
    }

    private static object AdventureConfigurationTagEvidence(AdventureTagFrame tag)
        => new { tag.Phase, tag.Flow, tag.WireId, tag.Payload, Packet = new { tag.Packet.Registries } };

    private static bool AdventureConfigurationTagsMatch(ProtocolPhase phase, PacketFlow flow, object? packet)
        => phase == ProtocolPhase.Configuration && flow == PacketFlow.Clientbound && packet is ClientboundConfigUpdateTagsPacket;

    private static int ReplayAdventureConfigurationTags(AdventureTagFrame[] tags, JavaVersion version, RegistryAccess registries)
    {
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        foreach (AdventureTagFrame tag in tags)
        {
            Assert.Equal(ProtocolPhase.Configuration, tag.Phase);
            Assert.Equal(PacketFlow.Clientbound, tag.Flow);
            PhaseRegistry registry = version.Protocol.GetRegistry(tag.Phase, tag.Flow);
            Assert.True(registry.TryGetInbound(tag.WireId, out BoundPacketCodec codec));
            var decoded = Assert.IsType<ClientboundConfigUpdateTagsPacket>(codec.Decode(tag.Payload, context));
            CompareAdventureTagRegistries(tag.Packet.Registries, decoded.Registries);
            var bytes = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(bytes);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(tag.Payload, bytes.WrittenSpan.ToArray());
        }
        return tags.Length;
    }

    private static void CompareAdventureTagRegistries(IReadOnlyList<TagRegistry> expected, IReadOnlyList<TagRegistry> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int registry = 0; registry < expected.Count; registry++)
        {
            TagRegistry first = expected[registry], second = actual[registry];
            Assert.Equal(first.Registry, second.Registry);
            Assert.Equal(first.Tags.Count, second.Tags.Count);
            for (int tag = 0; tag < first.Tags.Count; tag++)
            {
                Assert.Equal(first.Tags[tag].TagId, second.Tags[tag].TagId);
                Assert.Equal(first.Tags[tag].Ids, second.Tags[tag].Ids);
            }
        }
    }

    private static bool AdventureMarkerMatches(object? packet, string marker)
        => packet is ClientboundDisguisedChatPacket chat && chat.Message.ToPlainText().Equals(marker, StringComparison.Ordinal);

    private static int ReplayAdventureCausalFrames(CapturedFrame[] frames, JavaVersion version, RegistryAccess registries, IReadOnlyCollection<string> observationMarkers, List<object> dispositions)
    {
        Assert.Equal(observationMarkers.Count, observationMarkers.Distinct(StringComparer.Ordinal).Count());
        var seenMarkers = new HashSet<string>(StringComparer.Ordinal);
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        int count = 0;
        for (int index = 0; index < frames.Length; index++)
        {
            CapturedFrame frame = frames[index];
            if (frame.Packet is not (ClientboundDisguisedChatPacket or ClientboundSystemChatPacket or ClientboundLegacyChatPacket or ClientboundGameEventPacket or ClientboundPlayerAbilitiesPacket))
                continue;
            Assert.Equal(PacketFlow.Clientbound, frame.Flow);
            PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, frame.Flow);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, context);
            bool chat = frame.Packet is ClientboundDisguisedChatPacket or ClientboundSystemChatPacket or ClientboundLegacyChatPacket;
            string? marker = chat ? observationMarkers.FirstOrDefault(value => AdventureMarkerMatches(frame.Packet, value)) : null;
            if (marker is not null)
                Assert.True(seenMarkers.Add(marker), "A causal marker appeared more than once.");
            switch (frame.Packet)
            {
                case ClientboundDisguisedChatPacket expected:
                    var actual = Assert.IsType<ClientboundDisguisedChatPacket>(decoded);
                    Assert.Equal(expected.Message, actual.Message);
                    Assert.Equal(expected.Message.ToPlainText(), actual.Message.ToPlainText());
                    Assert.Equal(expected.ChatTypeId, actual.ChatTypeId);
                    Assert.Equal(expected.SenderName, actual.SenderName);
                    Assert.Equal(expected.TargetName, actual.TargetName);
                    if (marker is not null)
                        Assert.Equal(marker, actual.Message.ToPlainText());
                    break;
                case ClientboundGameEventPacket expected:
                    var game = Assert.IsType<ClientboundGameEventPacket>(decoded);
                    Assert.Equal(expected.Event, game.Event);
                    Assert.Equal(expected.Param, game.Param);
                    break;
                case ClientboundPlayerAbilitiesPacket expected:
                    var abilities = Assert.IsType<ClientboundPlayerAbilitiesPacket>(decoded);
                    Assert.Equal(expected.Flags, abilities.Flags);
                    Assert.Equal(expected.FlyingSpeed, abilities.FlyingSpeed);
                    Assert.Equal(expected.WalkingSpeed, abilities.WalkingSpeed);
                    break;
                default:
                    Assert.Equal(frame.Packet, decoded);
                    break;
            }
            var bytes = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(bytes);
            codec.Encode(ref writer, decoded, context);
            bool identical = frame.Payload.AsSpan().SequenceEqual(bytes.WrittenSpan);
            bool ambientChat = chat && marker is null;
            dispositions.Add(new { frameIndex = index, frame.Milliseconds, frame.Flow, frame.WireId, packet = decoded.GetType().Name, codec.CodecIdentity, exactCausalMarker = marker, ambientChat, typedSemanticAssertionsPassed = true, byteIdentityRequired = !ambientChat, byteIdentical = identical, nativePayloadBytes = frame.Payload.Length, reencodedPayloadBytes = bytes.WrittenCount, nativePayloadDifference = ambientChat && !identical ? frame.Payload : null, reencodedPayloadDifference = ambientChat && !identical ? bytes.WrittenSpan.ToArray() : null });
            if (ambientChat && !identical)
                continue;
            Assert.Equal(frame.Payload, bytes.WrittenSpan.ToArray());
            count++;
        }
        Assert.Equal(observationMarkers.Order(StringComparer.Ordinal), seenMarkers.Order(StringComparer.Ordinal));
        return count;
    }

    private static async Task VerifyAdventureFlatGenerator(VanillaInteractionAcceptanceHarness server, UmpkClient client, int x, int z, CancellationToken ct)
    {
        string path = Environment.GetEnvironmentVariable("UMPK_ADVENTURE_GENERATOR_SETTINGS_FILE") ?? throw new InvalidOperationException("Supply the pinned native generator settings.");
        byte[] bytes = await File.ReadAllBytesAsync(path, ct);
        Assert.Equal("65c9e53a491c688c58eac7422eb4f66b8ca01cfb16be4d309fd3c2a31cb5a898", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument expected = JsonDocument.Parse(bytes);
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(server.DirectoryPath, "server.properties"), ct);
        var properties = lines.Where(line => line.StartsWith("level-type=", StringComparison.Ordinal) || line.StartsWith("generator-settings=", StringComparison.Ordinal))
            .ToDictionary(line => line[..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..].Replace("\\:", ":", StringComparison.Ordinal).Replace("\\=", "=", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal));
        Assert.Equal("minecraft:flat", properties["level-type"]);
        using JsonDocument actual = JsonDocument.Parse(properties["generator-settings"]);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.Equal(-64, await client.InvokeAsync(c => c.State.World.Dimension.MinY, ct));
        foreach (string condition in new[] { $"execute if biome {x} 100 {z} minecraft:the_void", $"execute if block {x} -64 {z} minecraft:stone", $"execute if block {x} 99 {z} minecraft:stone", $"execute if block {x} 100 {z} minecraft:air" })
            Assert.Equal("Test passed", await server.CommandAsync(condition, ct));
        await server.WriteAsync("adventure-generator-preconditions.json", new { properties, minY = -64, floorY = 99, preScenarioFill = true, settingsSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
    }
}
