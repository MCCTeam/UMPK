using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    public static IEnumerable<object[]> CreativeMiningRestrictionReleases()
    {
        string[] releases = (Environment.GetEnvironmentVariable("UMPK_CREATIVE_MINING_RELEASES") ?? "1.21.4,1.21.5,26.3").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(releases);
        Assert.Equal(releases.Length, releases.Distinct(StringComparer.Ordinal).Count());
        foreach (string release in releases)
        {
            Assert.Contains(release, new[] { "1.21.4", "1.21.5", "26.3" });
            Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
            yield return [release, version.Version.Protocol];
        }
    }

    [NightlyTheory]
    [MemberData(nameof(CreativeMiningRestrictionReleases))]
    public async Task CreativeMiningRestrictionsHaveNativePacketsAndIndependentServerOutcomes(string release, int protocol)
    {
        await VerifyCreativeMiningPrerequisites();
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct, viewDistance: 2);
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        var appliedTags = new TaskCompletionSource<ClientboundConfigUpdateTagsPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable tagApplied = client.Events.Subscribe<PacketReceived>(message =>
        {
            if (message.Phase == ProtocolPhase.Configuration && message.Packet is ClientboundConfigUpdateTagsPacket packet)
                appliedTags.TrySetResult(packet);
        });
        var tagGate = new Lock();
        var tags = new List<AdventureTagFrame>();
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
                tags.Add(new(observation.Phase, observation.Flow, observation.WireId, observation.CopyPayload(), (ClientboundConfigUpdateTagsPacket)observation.DecodedPacket!));
            }
        }
        client.PacketFrameObserved += Observe;
        var scenes = new List<object>();
        var markers = new List<string>();
        var replayDispositions = new List<object>();
        int previousSequence = 0;
        bool passed = false;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded && c.State.Features.Physics, "physics-enabled native terrain", ct);
            Vec3d initial = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(initial.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(initial.Z) >> 4) * 16 + 8;
            Vec3d self = new(x + .5, 100, z + .5);
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync($"effect clear {Username}", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobSpawning false" : "gamerule minecraft:spawn_mobs false", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doTileDrops false" : "gamerule minecraft:block_drops false", ct);
            if (protocol == 769)
                await server.ConsoleAsync("gamerule doMobLoot false", ct);
            await server.ConsoleAsync($"kill @e[type=!minecraft:player]", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} {self.Y:R} {self.Z:R}"), ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.OnGround && c.State.Self.Position.Subtract(self).LengthSqr() < .0001, "grounded stationary native player", ct);
            await WaitAsync(() => capture.Frames.Any(frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == x >> 4 && chunk.ChunkZ == z >> 4), "actual native full column", ct);
            Guid ownId = await client.InvokeAsync(c => c.State.Self.Uuid, ct);
            Assert.NotEqual(Guid.Empty, ownId);
            // Clear late chunk-generation creatures and initial kill drops only in this owned native run.
            if (protocol != 769)
                await server.CommandAsync(protocol < 774 ? "gamerule doMobLoot false" : "gamerule minecraft:mob_drops false", ct);
            await SettleCreativeMiningNativeEntitiesAsync(server.CommandAsync, polls => server.WriteAsync("creative-mining-owned-setup.json", new { release, protocol, maximumPolls = 24, minimumNativeEmptyTicks = 20, budgetSeconds = 30, pollIntervalMilliseconds = 500, polls }), ct, protocol: protocol);
            await Marker("execute unless entity @e[type=!minecraft:player]", "no-peers");
            AdventureTagFrame[] nativeTags;
            lock (tagGate)
                nativeTags = [.. tags];
            Assert.NotEmpty(nativeTags);
            TagRegistry blockTags = Assert.Single(nativeTags[^1].Packet.Registries, registry => registry.Registry == RegistryIds.Block);
            TagEntry pickaxeTag = Assert.Single(blockTags.Tags, tag => tag.TagId == Identifier.Minecraft("mineable/pickaxe"));
            Assert.Contains(client.State.Registries!.Blocks[Identifier.Minecraft("stone")].NetworkId, pickaxeTag.Ids);
            ClientboundConfigUpdateTagsPacket installedTags = await appliedTags.Task.WaitAsync(ct);
            TagRegistry installedBlockTags = Assert.Single(installedTags.Registries, registry => registry.Registry == RegistryIds.Block);
            TagEntry installedPickaxeTag = Assert.Single(installedBlockTags.Tags, tag => tag.TagId == Identifier.Minecraft("mineable/pickaxe"));
            Assert.Contains(client.State.Registries!.Blocks[Identifier.Minecraft("stone")].NetworkId, installedPickaxeTag.Ids);

            await Scene("default-sword-denied", "diamond_sword", "minecraft:diamond_sword", creative: true, permitted: false, expectedTool: protocol >= 770 ? false : null);
            await Scene("default-pickaxe-permitted", "diamond_pickaxe", "minecraft:diamond_pickaxe", creative: true, permitted: true, expectedTool: protocol >= 770 ? true : null);
            if (protocol >= 770)
            {
                await Scene("removed-tool-sword-permitted", "diamond_sword", "minecraft:diamond_sword[!minecraft:tool]", creative: true, permitted: true, expectedTool: null, toolAbsent: true);
                await Scene("false-tool-stick-denied", "stick", "minecraft:stick[minecraft:tool={rules:[],can_destroy_blocks_in_creative:false}]", creative: true, permitted: false, expectedTool: false);
            }
            await Scene("survival-pickaxe-control", "diamond_pickaxe", "minecraft:diamond_pickaxe", creative: false, permitted: true, expectedTool: protocol >= 770 ? true : null);
            Assert.Equal(protocol == 769 ? 3 : 5, scenes.Count);
            Assert.False(capture.Overflowed);
            lock (tagGate)
            {
                Assert.False(tagOverflow);
                Assert.True(capture.PayloadBytes + tagBytes <= 20 * 1024 * 1024);
                nativeTags = [.. tags];
            }
            int actionReplay = capture.AssertRoundTrips(client.State.Registries!);
            int tagReplay = ReplayAdventureConfigurationTags(nativeTags, version, client.State.Registries!);
            int causalReplay = ReplayAdventureCausalFrames(capture.Frames, version, client.State.Registries!, markers, replayDispositions);
            int inputReplay = ReplayCreativeMiningInputs(capture.Frames, version, client.State.Registries!);
            Assert.True(actionReplay >= scenes.Count * 2 && inputReplay > 0);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "native peer-observed disconnect", ct);
            await server.WriteAsync("creative-mining-acceptance.json", new { release, protocol, ownId, physics = true, publicCaller = "DigBlockAsync explicit-coordinate held attack", sourceExpectedContract = "creative restriction initial+held group", observedScenes = scenes, actionReplay, tagReplay, causalReplay, inputReplay, originalClientExecutedInThisLeg = false, recordingCap = 20 * 1024 * 1024, recordedBytes = capture.PayloadBytes + tagBytes, peerObservedDisconnect = true });
            passed = true;

            async Task Scene(string name, string itemName, string nativeItem, bool creative, bool permitted, bool? expectedTool, bool toolAbsent = false)
            {
                var target = new BlockPos(x + 2, 100, z + scenes.Count - 2);
                GameMode mode = creative ? GameMode.Creative : GameMode.Survival;
                int setupMark = capture.Mark();
                await server.ConsoleAsync($"gamemode {(creative ? "creative" : "survival")} {Username}", ct);
                await server.ConsoleAsync($"clear {Username}", ct);
                await server.ConsoleAsync($"give {Username} {nativeItem} 1", ct);
                await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:stone", ct);
                await WaitAsync(client, c => c.State.Self.GameMode == mode && c.State.Self.InstantBuild == creative && c.State.Self.Invulnerable == creative && !c.State.Self.Flying
                    && c.State.Self.Uuid == ownId && c.State.TabList.TryGet(ownId, out var info) && info.GameMode == mode
                    && c.State.Self.ActiveEffects.Count == 0
                    && c.State.Self.HeldSlot == 0 && c.State.Inventory.PlayerSlots[36].Item.Id == Identifier.Minecraft(itemName) && c.State.Inventory.PlayerSlots[36].Count == 1 && c.State.Inventory.PlayerSlots[45].IsEmpty
                    && c.State.World.GetBlock(target).Block.Id == Identifier.Minecraft("stone") && c.State.Self.OnGround && c.State.Self.Position.Subtract(self).LengthSqr() < .0001, "native scene inputs " + name, ct);
                var inventory = await client.Snapshots.PlayerInventoryAsync(ct);
                var abilities = await client.Snapshots.AbilitiesAsync(ct);
                Assert.Equal(creative, abilities.InstantBuild);
                ToolComponent? tool = inventory.HeldItem.Components.Get(DataComponents.Tool);
                if (toolAbsent)
                    Assert.Null(tool);
                else if (expectedTool is bool flag)
                    Assert.Equal(flag, Assert.IsType<ToolComponent>(tool).CanDestroyBlocksInCreative);
                Assert.Contains(capture.Frames, frame => frame.Packet is ClientboundPlayerInfoUpdatePacket info && info.Actions.HasFlag(PlayerInfoActions.UpdateGameMode) && info.Entries.Any(entry => entry.ProfileId == ownId && entry.GameMode == mode));
                Assert.Contains(capture.Frames, frame => frame.Packet is ClientboundPlayerAbilitiesPacket ability && ((ability.Flags & 8) != 0) == creative);
                Assert.Contains(capture.Since(setupMark), frame => frame.Packet is ClientboundContainerSetSlotPacket slot && slot.Slot == 36 && slot.Item.Item.Id == Identifier.Minecraft(itemName) && slot.Item.Count == 1
                    || frame.Packet is ClientboundContainerSetContentPacket content && content.Items.Count > 36 && content.Items[36].Item.Id == Identifier.Minecraft(itemName) && content.Items[36].Count == 1);
                await Marker($"execute if entity @a[name={Username},gamemode={(creative ? "creative" : "survival")},nbt={{abilities:{{instabuild:{(creative ? 1 : 0)}b}}}}]", name + "-mode");
                string componentPredicate = toolAbsent ? "[!minecraft:tool]" : name == "false-tool-stick-denied" ? "[minecraft:tool={rules:[],can_destroy_blocks_in_creative:false}]" : protocol >= 770 ? "[minecraft:tool]" : "";
                await Marker($"execute if items entity {Username} weapon.mainhand minecraft:{itemName}{componentPredicate}", name + "-item");
                await NativeCount(itemName);
                await AssertBlockAsync(server, protocol, target, "stone", ct);
                int expectedInitialDestroyDelay = !creative && protocol == 769 ? 5 : !creative && protocol is 770 or 777 ? 4 : 0;
                long settled = await client.InvokeAsync(c => c.State.SessionTick, ct);
                await WaitAsync(client, c => c.State.SessionTick >= settled + 10, "settled session ticks before action", ct);
                await client.Actions.Movement.LookAtAsync(new(target.X + .5, target.Y + .5, target.Z + .5), ct);
                var position = await client.Snapshots.SelfAsync(ct);
                Assert.True(position.EyePosition.Subtract(new(target.X + .5, target.Y + .5, target.Z + .5)).LengthSqr() < 4.5 * 4.5);
                long actionTick = await client.InvokeAsync(c => c.State.SessionTick, ct);
                int mark = capture.Mark();
                using var actionCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task action = client.Actions.Interaction.DigBlockAsync(target, Direction.West, actionCancellation.Token);
                try
                {
                    await action.WaitAsync(TimeSpan.FromSeconds(10), ct);
                }
                finally
                {
                    if (!action.IsCompleted)
                    {
                        await actionCancellation.CancelAsync();
                        try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
                        catch (OperationCanceledException) { }
                    }
                }
                string expectedBlock = permitted ? "air" : "stone";
                await AssertBlockAsync(server, protocol, target, expectedBlock, ct);
                await WaitAsync(client, c => c.State.World.GetBlock(target).Block.Id == Identifier.Minecraft(expectedBlock), "native block result " + name, ct);
                string nativeCountResponse = await NativeCount(itemName);
                Assert.Equal(1, (await client.Snapshots.PlayerInventoryAsync(ct)).HeldItem.Count);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                await Marker("execute if entity @a[name=" + Username + "]", name + "-after-window");
                CapturedFrame[] window = capture.Since(mark);
                CapturedFrame[] actions = window.Where(IsMiningOrAnimation).ToArray();
                if (creative)
                {
                    Assert.Equal(permitted ? 2 : 3, actions.Length);
                    var start = Assert.IsType<ServerboundPlayerActionPacket>(actions[0].Packet);
                    Assert.Equal(0, start.Action);
                    foreach (CapturedFrame animation in actions.Skip(1))
                        AssertCreativeMiningAnimation(animation.Packet, protocol);
                }
                else
                    AssertMining(window, protocol, target, expectedInitialDestroyDelay);
                foreach (ServerboundPlayerActionPacket packet in actions.Select(frame => frame.Packet).OfType<ServerboundPlayerActionPacket>())
                {
                    Assert.Equal(target, packet.Position);
                    Assert.Equal((byte)Direction.West, packet.Direction);
                    int sequence = Assert.IsType<int>(packet.Sequence);
                    Assert.True(sequence > previousSequence);
                    previousSequence = sequence;
                }
                Assert.True(window[^1].Milliseconds - actions[^1].Milliseconds >= 900);
                scenes.Add(new { name, sourceExpected = new { creative, permitted, expectedTool, toolAbsent, expectedBlock, creativePacketCount = creative ? permitted ? 2 : 3 : 9 + expectedInitialDestroyDelay, minimumElapsedTicksBeforeAction = 10 }, observed = new { target, item = inventory.HeldItem.Item.Id.ToString(), abilities, packetKinds = actions.Select(frame => frame.Packet!.GetType().Name).ToArray(), successfulNativeBlockPredicate = expectedBlock, nativeCountResponse, publicActionCompleted = action.IsCompletedSuccessfully, elapsedSessionTicksBeforeAction = actionTick - settled } });
            }

            async Task Marker(string predicate, string label)
            {
                string marker = "creative-mining-" + label + "-" + Guid.NewGuid().ToString("N");
                markers.Add(marker);
                await server.CommandAsync(predicate + " run say " + marker, ct);
                await WaitAsync(() => capture.Frames.Any(frame => AdventureMarkerMatches(frame.Packet, marker)), "native predicate marker " + label, ct);
            }

            async Task<string> NativeCount(string itemName)
            {
                string response = await server.CommandAsync($"clear {Username} minecraft:{itemName} 0", ct);
                Assert.Contains(response, new[] { $"Found 1 matching item on player {Username}", $"Found 1 matching item(s) on player {Username}", $"Found 1 matching items on player {Username}" });
                return response;
            }
        }
        finally
        {
            client.PacketFrameObserved -= Observe;
            capture.Stop();
            await server.WriteAsync("creative-mining-capture.json", capture.Frames);
            AdventureTagFrame[] finalTags;
            lock (tagGate)
                finalTags = [.. tags];
            await server.WriteAsync("creative-mining-configuration-tags.json", finalTags.Select(AdventureConfigurationTagEvidence).ToArray());
            await server.WriteAsync("creative-mining-replay-dispositions.json", replayDispositions);
            await server.WriteAsync("creative-mining-result.json", new { passed, release, protocol, completedScenes = scenes.Count, scenes, capture.PayloadBytes, tagBytes, capture.Overflowed, tagOverflow, originalClientExecutedInThisLeg = false });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnect = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnect.Token);
            }
        }
    }

    private static void AssertCreativeMiningAnimation(object? packet, int protocol)
    {
        if (protocol == 777)
            Assert.IsType<ServerboundPunchPacket>(packet);
        else
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packet).Hand);
    }

    private static int ReplayCreativeMiningInputs(CapturedFrame[] frames, JavaVersion version, RegistryAccess registries)
    {
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        int count = 0;
        foreach (CapturedFrame frame in frames)
        {
            if (frame.Packet is not (ClientboundContainerSetSlotPacket or ClientboundContainerSetContentPacket or ClientboundPlayerInfoUpdatePacket or ClientboundBlockChangedAckPacket))
                continue;
            PhaseRegistry registry = version.Protocol.GetRegistry(frame.Phase, frame.Flow);
            Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, context);
            switch (frame.Packet)
            {
                case ClientboundContainerSetSlotPacket slot:
                    var actualSlot = Assert.IsType<ClientboundContainerSetSlotPacket>(decoded);
                    Assert.Equal((slot.ContainerId, slot.StateId, slot.Slot), (actualSlot.ContainerId, actualSlot.StateId, actualSlot.Slot));
                    CompareItem(slot.Item, actualSlot.Item);
                    break;
                case ClientboundContainerSetContentPacket content:
                    var actualContent = Assert.IsType<ClientboundContainerSetContentPacket>(decoded);
                    Assert.Equal((content.ContainerId, content.StateId, content.Items.Count), (actualContent.ContainerId, actualContent.StateId, actualContent.Items.Count));
                    for (int index = 0; index < content.Items.Count; index++)
                        CompareItem(content.Items[index], actualContent.Items[index]);
                    CompareItem(content.CarriedItem, actualContent.CarriedItem);
                    break;
                case ClientboundPlayerInfoUpdatePacket info:
                    var actualInfo = Assert.IsType<ClientboundPlayerInfoUpdatePacket>(decoded);
                    Assert.Equal(info.Actions, actualInfo.Actions);
                    Assert.Equal(info.Entries.Select(entry => (entry.ProfileId, entry.Name, entry.GameMode, entry.Listed, entry.Latency)), actualInfo.Entries.Select(entry => (entry.ProfileId, entry.Name, entry.GameMode, entry.Listed, entry.Latency)));
                    break;
                default:
                    Assert.Equal(frame.Packet, decoded);
                    break;
            }
            var bytes = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(bytes);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(frame.Payload, bytes.WrittenSpan.ToArray());
            count++;
        }
        return count;

        static void CompareItem(ItemStack expected, ItemStack actual)
        {
            Assert.Equal((expected.Item.Id, expected.Count, expected.Damage), (actual.Item.Id, actual.Count, actual.Damage));
            ToolComponent? first = expected.Components.Get(DataComponents.Tool), second = actual.Components.Get(DataComponents.Tool);
            if (first is null)
                Assert.Null(second);
            else
            {
                Assert.NotNull(second);
                Assert.Equal((first.DefaultMiningSpeed, first.DamagePerBlock, first.CanDestroyBlocksInCreative), (second.DefaultMiningSpeed, second.DamagePerBlock, second.CanDestroyBlocksInCreative));
                Assert.Equal(first.Rules, second.Rules);
            }
        }
    }

    private static async Task VerifyCreativeMiningPrerequisites()
    {
        string path = Environment.GetEnvironmentVariable("UMPK_CREATIVE_MINING_PREREQUISITE_RECEIPT") ?? throw new InvalidOperationException("Supply a reviewed full/AOT producer and fixture receipt before native creative-mining execution.");
        byte[] bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_CREATIVE_MINING_PREREQUISITE_SHA256"), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement receipt = document.RootElement;
        Assert.Equal("ready for bounded native creative-mining acceptance", receipt.GetProperty("status").GetString());
        string? schema = receipt.GetProperty("producer_proof_schema").GetString();
        if (schema == "current-full35780-two-local-roots-v1")
        {
            await AssertCurrent35780AuditedProducer();
            return;
        }
        bool current35709 = schema == "current-full35709-same-full-Cake111-v1";
        if (!current35709)
            Assert.Equal("current-full35611-same-full-Cake111-v1", schema);
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_INTERACTION_PRODUCER"), receipt.GetProperty("producer_commit").GetString());
        Assert.False(receipt.GetProperty("offline_compatibility_only").GetBoolean());
        Assert.Equal(current35709 ? "4c6cddd899273d72d1fda72721ff5c5cd6ca4b8f692c24bb801219d596083095" : "5591ade158bcdaca5245091bfcdbde6294646ca112c327f5997d682ec6c649c7", receipt.GetProperty("client_sha256").GetString());
        Assert.Equal(current35709 ? "0b412226030f8e2152d84bb818684d1336acede063a0aaf8123a745bde51bb18" : "42492b4276fcf96508a1e42cf2d12e57db6fdd6d8a65c3b35e24629a8087ffdd", receipt.GetProperty("matched_client_tests_sha256").GetString());
        if (current35709)
            await AssertCurrentAuditedProducer();
        byte[] matchedTests = await File.ReadAllBytesAsync(receipt.GetProperty("matched_client_tests").GetProperty("path").GetString()!);
        Assert.Equal(receipt.GetProperty("matched_client_tests_sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(matchedTests)));
        JsonElement aot = receipt.GetProperty("aot_binary");
        byte[] aotBytes = await File.ReadAllBytesAsync(aot.GetProperty("path").GetString()!);
        Assert.Equal(aot.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(aotBytes)));
        Assert.Equal(receipt.GetProperty("aot_bytes").GetInt32(), aotBytes.Length);
        Assert.True(aotBytes.Length < 25_165_824);
        await AssertTrx("final_client_full_trx", current35709 ? 35709 : 35611);
        await AssertTrx("final_aot_smoke_trx", 1);
        JsonElement sources = receipt.GetProperty("harness_sources");
        foreach (string required in new[] { "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.CreativeMining.cs", "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.cs", "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.Adventure.cs", "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceHarness.cs", "tests/Umpk.IntegrationTests/NightlyTheoryAttribute.cs" })
            Assert.True(sources.TryGetProperty(required, out _), "Required fixture source pin absent: " + required);
        if (current35709)
            Assert.True(sources.TryGetProperty("tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.CreativeMiningSetupTests.cs", out _), "Required owned-setup fixture source pin absent.");
        foreach (JsonProperty source in sources.EnumerateObject())
            Assert.Equal(source.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(FixturePaths.RepoRoot(), source.Name)))));
        var binaries = Directory.GetFiles(AppContext.BaseDirectory, "Umpk.*").Where(file => Path.GetExtension(file) is ".dll" or ".pdb").ToDictionary(file => Path.GetFileName(file), file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        Assert.Equal(receipt.GetProperty("client_sha256").GetString(), binaries["Umpk.Client.dll"]);
        if (current35709)
            Assert.Equal("ede2c3bdab05ffe33ad72fbbd874f1b1dec60434f1385e13f40a1f2f1152c2f0", binaries["Umpk.Protocol.Java.dll"]);
        JsonElement expected = receipt.GetProperty("integration_binaries");
        Assert.Equal(binaries.Keys.Order(StringComparer.Ordinal), expected.EnumerateObject().Select(binary => binary.Name).Order(StringComparer.Ordinal));
        foreach (var binary in binaries)
            Assert.Equal(binary.Value, expected.GetProperty(binary.Key!).GetString());

        async Task AssertCurrentAuditedProducer()
        {
            JsonElement rootPin = receipt.GetProperty("current_root_audit");
            Assert.Equal("bf249c02cf91e75ab716add469005ebb1893106dde135fc090f3e1e01a80437f", rootPin.GetProperty("sha256").GetString());
            byte[] raw = await File.ReadAllBytesAsync(rootPin.GetProperty("path").GetString()!);
            Assert.Equal(rootPin.GetProperty("bytes").GetInt64(), raw.LongLength);
            Assert.Equal(rootPin.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(raw)));
            using JsonDocument acceptedDocument = JsonDocument.Parse(raw);
            JsonElement accepted = acceptedDocument.RootElement;
            Assert.Equal("accepted exact fullClient35709/35585keys and strictAOT/exact1+CIguard with owned cleanup; full17/consumer/native-server gates separate", accepted.GetProperty("status").GetString());
            Assert.Equal(35709, accepted.GetProperty("passed").GetInt32());
            Assert.Equal(0, accepted.GetProperty("failed").GetInt32());
            Assert.Equal(0, accepted.GetProperty("skipped").GetInt32());
            Assert.Equal(35585, accepted.GetProperty("uniqueDisplayKeys").GetInt32());
            Assert.True(accepted.GetProperty("exactlyOneExecutedPassedSmoke").GetBoolean());
            Assert.True(accepted.GetProperty("actualCiXmlGuardPassed").GetBoolean());
            Assert.True(accepted.GetProperty("allProducerAndTestPairsUnchanged").GetBoolean());
            Assert.True(accepted.GetProperty("all29ObservedPidsAbsent").GetBoolean());
            Assert.Equal("4c6cddd899273d72d1fda72721ff5c5cd6ca4b8f692c24bb801219d596083095", accepted.GetProperty("clientPair").GetProperty("dll").GetProperty("sha256").GetString());
            AssertPin(receipt.GetProperty("matched_client_tests"), accepted.GetProperty("testPair").GetProperty("dll"));
            AssertPin(receipt.GetProperty("final_client_full_trx"), accepted.GetProperty("fullTrx"));
            AssertPin(receipt.GetProperty("final_aot_smoke_trx"), accepted.GetProperty("nativeSmokeTrx"));
            AssertPin(receipt.GetProperty("aot_binary"), accepted.GetProperty("binary"));
            Assert.Equal(24_712_880, receipt.GetProperty("aot_bytes").GetInt32());
        }

        async Task AssertCurrent35780AuditedProducer()
        {
            Assert.Equal(Environment.GetEnvironmentVariable("UMPK_INTERACTION_PRODUCER"), receipt.GetProperty("producer_commit").GetString());
            Assert.False(receipt.GetProperty("offline_compatibility_only").GetBoolean());
            Assert.Equal("eeeaaefce47801b47f89d07f9f9baf6579adc0b2d817f5061c6339436862a4d0", receipt.GetProperty("client_sha256").GetString());
            Assert.Equal("9f183fe3e2fdd9fbc67f95ec27820a5991716a9040bf157b5ac0377844ceff44", receipt.GetProperty("matched_client_tests_sha256").GetString());
            JsonElement fullPin = receipt.GetProperty("current_root_audit");
            Assert.Equal("ef38ee8e294616c4131e70c32cb15372739b88ac8a144c88976174c662e76017", fullPin.GetProperty("sha256").GetString());
            using JsonDocument fullDocument = await ReadPinnedJson(fullPin);
            JsonElement full = fullDocument.RootElement;
            Assert.Equal("accepted exact current-source full Client35780 without AOT", full.GetProperty("status").GetString());
            Assert.True(full.GetProperty("passed").GetBoolean());
            Assert.Equal(35780, full.GetProperty("rows").GetInt32());
            Assert.Equal(35656, full.GetProperty("distinctDisplayKeys").GetInt32());
            Assert.Equal("352 authored + SDK1 + generated3; PDB356; references198/11; Csc/PE54", full.GetProperty("sourceClosure").GetString());
            Assert.True(full.GetProperty("sourceClosureRechecked").GetBoolean());
            Assert.Equal(126, full.GetProperty("runtimeFilesVerifiedUnchanged").GetInt32());
            Assert.True(full.GetProperty("all10ObservedPidsIndependentlyAbsent").GetBoolean());
            foreach (JsonProperty counter in full.GetProperty("rawCounters").EnumerateObject())
                Assert.Equal(counter.Name is "total" or "executed" or "passed" ? "35780" : "0", counter.Value.GetString());
            Assert.Equal(receipt.GetProperty("client_sha256").GetString(), full.GetProperty("producerDllSha256").GetString());
            Assert.Equal("90990c78abae3fb7323f7c2ab5a87b7439d28ed5cf15250c02ac58e78bc7ae07", full.GetProperty("producerPdbSha256").GetString());
            AssertPin(receipt.GetProperty("matched_client_tests"), full.GetProperty("testPair").GetProperty("dll"));
            AssertPin(receipt.GetProperty("matched_client_test_pdb"), full.GetProperty("testPair").GetProperty("pdb"));
            AssertPin(receipt.GetProperty("final_client_full_trx"), full.GetProperty("rawTrx"));
            await AssertPinnedFile(receipt.GetProperty("matched_client_tests"));
            await AssertPinnedFile(receipt.GetProperty("matched_client_test_pdb"));
            await AssertTrx("final_client_full_trx", 35780);
            await AssertOwnedCleanup(full.GetProperty("cleanup"), 10);

            JsonElement aotPin = receipt.GetProperty("current_aot_root_audit");
            Assert.Equal("0b835ab6820d0b020efa0abe6d5f3c00e82cb3ccb31ae3294adf0ee693b662f0", aotPin.GetProperty("sha256").GetString());
            using JsonDocument aotDocument = await ReadPinnedJson(aotPin);
            JsonElement aot = aotDocument.RootElement;
            Assert.Equal("accepted currenteee localAOT strict/exact1+actualCIguard; matched full35780 retained unchanged", aot.GetProperty("status").GetString());
            AssertPin(fullPin, aot.GetProperty("fullClientRoot"));
            AssertPin(receipt.GetProperty("final_client_full_trx"), aot.GetProperty("fullTrx"));
            AssertPin(receipt.GetProperty("matched_client_tests"), aot.GetProperty("testPair").GetProperty("dll"));
            AssertPin(receipt.GetProperty("matched_client_test_pdb"), aot.GetProperty("testPair").GetProperty("pdb"));
            Assert.Equal(35780, aot.GetProperty("passed").GetInt32());
            Assert.Equal(0, aot.GetProperty("failed").GetInt32());
            Assert.Equal(0, aot.GetProperty("skipped").GetInt32());
            Assert.Equal(35656, aot.GetProperty("uniqueDisplayKeys").GetInt32());
            Assert.True(aot.GetProperty("exactlyOneExecutedPassedSmoke").GetBoolean());
            Assert.True(aot.GetProperty("actualCiXmlGuardPassed").GetBoolean());
            Assert.True(aot.GetProperty("allProducerAndTestPairsUnchanged").GetBoolean());
            Assert.Equal(28, aot.GetProperty("protectedOriginalArtifacts").GetInt32());
            Assert.Equal(11, aot.GetProperty("actualIlcReferenceCount").GetInt32());
            Assert.True(aot.GetProperty("all14ObservedPidsAbsent").GetBoolean());
            Assert.Equal(25165824, aot.GetProperty("strictLimit").GetInt32());
            JsonElement binary = receipt.GetProperty("aot_binary");
            AssertPin(binary, aot.GetProperty("binary"));
            Assert.Equal("5979caf8c8b3d1f530d811f1ceb8df50c92c04b6cac52a9e4f5729973d3c80a2", binary.GetProperty("sha256").GetString());
            Assert.Equal(24729600, binary.GetProperty("bytes").GetInt32());
            Assert.Equal(24729600, receipt.GetProperty("aot_bytes").GetInt32());
            Assert.True(receipt.GetProperty("aot_bytes").GetInt32() < 25_165_824);
            await AssertPinnedFile(binary);
            AssertPin(receipt.GetProperty("final_aot_smoke_trx"), aot.GetProperty("nativeSmokeTrx"));
            await AssertTrx("final_aot_smoke_trx", 1);
            await AssertOwnedCleanup(aot.GetProperty("cleanup"), 14);
            using JsonDocument aotBindingDocument = await ReadPinnedJson(aot.GetProperty("binding"));
            JsonElement aotBinding = aotBindingDocument.RootElement;
            Assert.True(aotBinding.GetProperty("all28OriginalArtifactsInvariantBeforeAfter").GetBoolean());
            Assert.True(aotBinding.GetProperty("allObservedPidsIndependentlyAbsent").GetBoolean());
            Assert.True(aotBinding.GetProperty("actualCiXmlGuardPassed").GetBoolean());
            Assert.Equal(28, aotBinding.GetProperty("originalArtifactPins").EnumerateObject().Count());
            foreach (JsonProperty artifact in aotBinding.GetProperty("originalArtifactPins").EnumerateObject())
                await AssertPinnedFile(artifact.Value);
            using JsonDocument xmlGuardDocument = await ReadPinnedJson(aotBinding.GetProperty("actualCiXmlGuardCommand"));
            JsonElement xmlGuard = xmlGuardDocument.RootElement;
            Assert.Equal(0, xmlGuard.GetProperty("exitCode").GetInt32());
            Assert.Contains("aot-smoke-trx-guard.py", xmlGuard.GetProperty("argv")[1].GetString()!);
            Assert.True(xmlGuard.GetProperty("reaped").GetBoolean());
            Assert.Equal("/usr/bin/python3", xmlGuard.GetProperty("argv")[0].GetString());
            using JsonDocument aotInputsDocument = await ReadPinnedJson(aotBinding.GetProperty("inputManifest"));
            JsonElement xmlSource = receipt.GetProperty("aot_xml_guard_source");
            AssertPin(xmlSource, aotInputsDocument.RootElement.GetProperty(xmlGuard.GetProperty("argv")[1].GetString()!));
            await AssertPinnedFile(xmlSource);

            JsonElement currentSources = receipt.GetProperty("harness_sources");
            foreach (string required in new[] { "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.CreativeMining.cs", "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.cs", "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.Adventure.cs", "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceHarness.cs", "tests/Umpk.IntegrationTests/NightlyTheoryAttribute.cs", "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.CreativeMiningSetupTests.cs" })
                Assert.True(currentSources.TryGetProperty(required, out _), "Required current fixture source pin absent: " + required);
            foreach (JsonProperty source in currentSources.EnumerateObject())
                Assert.Equal(source.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(FixturePaths.RepoRoot(), source.Name)))));
            var currentBinaries = Directory.GetFiles(AppContext.BaseDirectory, "Umpk.*").Where(file => Path.GetExtension(file) is ".dll" or ".pdb").ToDictionary(file => Path.GetFileName(file), file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
            Assert.Equal(receipt.GetProperty("client_sha256").GetString(), currentBinaries["Umpk.Client.dll"]);
            Assert.Equal("90990c78abae3fb7323f7c2ab5a87b7439d28ed5cf15250c02ac58e78bc7ae07", currentBinaries["Umpk.Client.pdb"]);
            Assert.Equal("ede2c3bdab05ffe33ad72fbbd874f1b1dec60434f1385e13f40a1f2f1152c2f0", currentBinaries["Umpk.Protocol.Java.dll"]);
            JsonElement expectedCurrentBinaries = receipt.GetProperty("integration_binaries");
            Assert.Equal(currentBinaries.Keys.Order(StringComparer.Ordinal), expectedCurrentBinaries.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
            foreach (var artifact in currentBinaries)
                Assert.Equal(artifact.Value, expectedCurrentBinaries.GetProperty(artifact.Key!).GetString());
            JsonElement ilc = aotBinding.GetProperty("actualIlcReferences");
            Assert.Equal(11, ilc.GetArrayLength());
            foreach (JsonElement reference in ilc.EnumerateArray())
            {
                await AssertPinnedFile(reference);
                Assert.Equal(currentBinaries[Path.GetFileName(reference.GetProperty("path").GetString()!)], reference.GetProperty("sha256").GetString());
            }
            Assert.Equal(currentBinaries["Umpk.Client.dll"], aot.GetProperty("clientPair").GetProperty("dll").GetProperty("sha256").GetString());
            Assert.Equal(currentBinaries["Umpk.Client.pdb"], aot.GetProperty("clientPair").GetProperty("pdb").GetProperty("sha256").GetString());

            static async Task AssertPinnedFile(JsonElement pin)
            {
                byte[] raw = await File.ReadAllBytesAsync(pin.GetProperty("path").GetString()!);
                Assert.Equal(pin.GetProperty("bytes").GetInt64(), raw.LongLength);
                Assert.Equal(pin.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(raw)));
            }

            static async Task<JsonDocument> ReadPinnedJson(JsonElement pin)
            {
                await AssertPinnedFile(pin);
                return JsonDocument.Parse(await File.ReadAllBytesAsync(pin.GetProperty("path").GetString()!));
            }

            static async Task AssertOwnedCleanup(JsonElement pin, int expectedPids)
            {
                using JsonDocument document = await ReadPinnedJson(pin);
                JsonElement cleanup = document.RootElement;
                Assert.True(cleanup.GetProperty("slotReleased").GetBoolean());
                foreach (string field in new[] { "allObservedPidsAbsentBeforeStop", "allObservedPidsAbsentAfterStop" })
                {
                    JsonProperty[] pids = cleanup.GetProperty(field).EnumerateObject().ToArray();
                    Assert.Equal(expectedPids, pids.Length);
                    Assert.All(pids, pid => Assert.True(pid.Value.GetBoolean()));
                }
            }
        }

        static void AssertPin(JsonElement actual, JsonElement accepted)
        {
            Assert.Equal(accepted.GetProperty("path").GetString(), actual.GetProperty("path").GetString());
            Assert.Equal(accepted.GetProperty("sha256").GetString(), actual.GetProperty("sha256").GetString());
            Assert.Equal(accepted.GetProperty("bytes").GetInt64(), actual.GetProperty("bytes").GetInt64());
        }

        async Task AssertTrx(string key, int total)
        {
            JsonElement pin = receipt.GetProperty(key);
            byte[] raw = await File.ReadAllBytesAsync(pin.GetProperty("path").GetString()!);
            Assert.Equal(pin.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(raw)));
            using var stream = new MemoryStream(raw, writable: false);
            XDocument result = XDocument.Load(stream);
            XElement counters = result.Descendants(result.Root!.Name.Namespace + "Counters").Single();
            Assert.Equal(total, (int)counters.Attribute("total")!);
            Assert.Equal(total, (int)counters.Attribute("executed")!);
            Assert.Equal(total, (int)counters.Attribute("passed")!);
            Assert.Equal(0, (int)counters.Attribute("failed")!);
            Assert.Equal(0, (int)counters.Attribute("notExecuted")!);
            XElement[] rows = result.Descendants(result.Root.Name.Namespace + "UnitTestResult").ToArray();
            Assert.Equal(total, rows.Length);
            Assert.All(rows, row => Assert.Equal("Passed", (string?)row.Attribute("outcome")));
        }
    }

    private sealed record CreativeMiningSetupPoll(int Attempt, string? TimeBeforeResponse = null, int? NativeTickBefore = null, string? PresenceResponse = null, int? NativeEntityCount = null, string? ActorDataResponse = null, string? CleanupResponse = null, string? TimeAfterResponse = null, int? NativeTickAfter = null, long? EmptyTicks = null, bool Ready = false);

    private static async Task SettleCreativeMiningNativeEntitiesAsync(Func<string, CancellationToken, Task<string>> command, Func<IReadOnlyList<CreativeMiningSetupPoll>, Task> writeEvidence, CancellationToken ct, int protocol = 769, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        Assert.Contains(protocol, new[] { 769, 770, 777 });
        delay ??= Task.Delay;
        const int maximumPolls = 24;
        const int minimumEmptyTicks = 20;
        const string presenceCommand = "execute unless entity @e[type=!minecraft:player]";
        const string actorCommand = "data get entity @e[type=!minecraft:player,limit=1]";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        List<CreativeMiningSetupPoll> polls = [];
        int? emptySinceAfterProbe = null;
        try
        {
            for (int attempt = 0; attempt < maximumPolls; attempt++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var poll = new CreativeMiningSetupPoll(attempt);
                polls.Add(poll);
                string beforeResponse = await command("time query gametime", deadline.Token);
                polls[^1] = poll = poll with { TimeBeforeResponse = beforeResponse };
                int beforeTick = ParseCreativeMiningNativeTick(beforeResponse, protocol);
                polls[^1] = poll = poll with { NativeTickBefore = beforeTick };
                string presence = await command(presenceCommand, deadline.Token);
                polls[^1] = poll = poll with { PresenceResponse = presence };
                if (presence == "Test passed")
                {
                    string afterResponse = await command("time query gametime", deadline.Token);
                    polls[^1] = poll = poll with { TimeAfterResponse = afterResponse, NativeEntityCount = 0 };
                    int afterTick = ParseCreativeMiningNativeTick(afterResponse, protocol);
                    long elapsed = 0;
                    if (emptySinceAfterProbe is int firstAfterTick)
                        elapsed = beforeTick >= firstAfterTick ? beforeTick - firstAfterTick : (long)beforeTick + int.MaxValue - firstAfterTick;
                    else
                        emptySinceAfterProbe = afterTick;
                    bool ready = elapsed >= minimumEmptyTicks;
                    polls[^1] = poll with { NativeTickAfter = afterTick, EmptyTicks = elapsed, Ready = ready };
                    if (ready)
                        return;
                }
                else
                {
                    string failedPrefix = protocol == 777 ? "Test failed. Count: " : "Test failed, count: ";
                    Assert.StartsWith(failedPrefix, presence, StringComparison.Ordinal);
                    Assert.True(int.TryParse(presence.AsSpan(failedPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int count) && count > 0, "Native entity-count query must have an exact positive count.");
                    polls[^1] = poll = poll with { NativeEntityCount = count };
                    emptySinceAfterProbe = null;
                    string actorData = await command(actorCommand, deadline.Token);
                    polls[^1] = poll = poll with { ActorDataResponse = actorData };
                    Assert.True(actorData == "No entity was found" || actorData.Contains(" has the following entity data: ", StringComparison.Ordinal), "The native actor query must retain exact entity NBT or its disappearance response.");
                    string removed = await command("kill @e[type=!minecraft:player]", deadline.Token);
                    polls[^1] = poll with { CleanupResponse = removed };
                    Assert.True(removed == "No entity was found" || removed.StartsWith("Killed ", StringComparison.Ordinal), "The owned global cleanup command must have a native result.");
                }
                await delay(TimeSpan.FromMilliseconds(500), deadline.Token);
            }
            throw new InvalidOperationException("Native global no-peer precondition did not settle within 24 probes.");
        }
        finally
        {
            await writeEvidence(polls.ToArray());
        }
    }

    private static int ParseCreativeMiningNativeTick(string response, int protocol)
    {
        Assert.Contains(protocol, new[] { 769, 770, 777 });
        string prefix = protocol == 777 ? "The game time is " : "The time is ";
        string suffix = protocol == 777 ? " tick(s)" : string.Empty;
        Assert.StartsWith(prefix, response, StringComparison.Ordinal);
        Assert.EndsWith(suffix, response, StringComparison.Ordinal);
        ReadOnlySpan<char> digits = response.AsSpan(prefix.Length, response.Length - prefix.Length - suffix.Length);
        bool parsed = long.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long tick);
        Assert.True(parsed && tick >= 0 && (protocol == 777 || tick < int.MaxValue), "Native gametime must match the release's nonnegative TimeCommand numeric range.");
        Assert.True(digits.SequenceEqual(tick.ToString(System.Globalization.CultureInfo.InvariantCulture)), "Native gametime must use the exact decimal TimeCommand response.");
        return (int)(tick % int.MaxValue);
    }
}
