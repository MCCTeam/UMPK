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
            await server.ConsoleAsync($"kill @e[type=!minecraft:player]", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} {self.Y:R} {self.Z:R}"), ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.OnGround && c.State.Self.Position.Subtract(self).LengthSqr() < .0001, "grounded stationary native player", ct);
            await WaitAsync(() => capture.Frames.Any(frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == x >> 4 && chunk.ChunkZ == z >> 4), "actual native full column", ct);
            Guid ownId = await client.InvokeAsync(c => c.State.Self.Uuid, ct);
            Assert.NotEqual(Guid.Empty, ownId);
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
                long settled = await client.InvokeAsync(c => c.State.SessionTick, ct);
                await WaitAsync(client, c => c.State.SessionTick >= settled + 10, "elapsed native destroy delay", ct);
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
                    AssertMining(window, protocol, target);
                foreach (ServerboundPlayerActionPacket packet in actions.Select(frame => frame.Packet).OfType<ServerboundPlayerActionPacket>())
                {
                    Assert.Equal(target, packet.Position);
                    Assert.Equal((byte)Direction.West, packet.Direction);
                    int sequence = Assert.IsType<int>(packet.Sequence);
                    Assert.True(sequence > previousSequence);
                    previousSequence = sequence;
                }
                Assert.True(window[^1].Milliseconds - actions[^1].Milliseconds >= 900);
                scenes.Add(new { name, sourceExpected = new { creative, permitted, expectedTool, toolAbsent, expectedBlock, creativePacketCount = creative ? permitted ? 2 : 3 : 9, minimumElapsedTicksBeforeAction = 10 }, observed = new { target, item = inventory.HeldItem.Item.Id.ToString(), abilities, packetKinds = actions.Select(frame => frame.Packet!.GetType().Name).ToArray(), successfulNativeBlockPredicate = expectedBlock, nativeCountResponse, publicActionCompleted = action.IsCompletedSuccessfully, elapsedSessionTicksBeforeAction = actionTick - settled } });
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
        string path = Environment.GetEnvironmentVariable("UMPK_CREATIVE_MINING_PREREQUISITE_RECEIPT") ?? throw new InvalidOperationException("Supply the reviewed combined35611/AOT producer and fixture receipt before native creative-mining execution.");
        byte[] bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_CREATIVE_MINING_PREREQUISITE_SHA256"), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement receipt = document.RootElement;
        Assert.Equal("ready for bounded native creative-mining acceptance", receipt.GetProperty("status").GetString());
        Assert.Equal("current-full35611-same-full-Cake111-v1", receipt.GetProperty("producer_proof_schema").GetString());
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_INTERACTION_PRODUCER"), receipt.GetProperty("producer_commit").GetString());
        Assert.False(receipt.GetProperty("offline_compatibility_only").GetBoolean());
        Assert.Equal("5591ade158bcdaca5245091bfcdbde6294646ca112c327f5997d682ec6c649c7", receipt.GetProperty("client_sha256").GetString());
        Assert.Equal("42492b4276fcf96508a1e42cf2d12e57db6fdd6d8a65c3b35e24629a8087ffdd", receipt.GetProperty("matched_client_tests_sha256").GetString());
        byte[] matchedTests = await File.ReadAllBytesAsync(receipt.GetProperty("matched_client_tests").GetProperty("path").GetString()!);
        Assert.Equal(receipt.GetProperty("matched_client_tests_sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(matchedTests)));
        JsonElement aot = receipt.GetProperty("aot_binary");
        byte[] aotBytes = await File.ReadAllBytesAsync(aot.GetProperty("path").GetString()!);
        Assert.Equal(aot.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(aotBytes)));
        Assert.Equal(receipt.GetProperty("aot_bytes").GetInt32(), aotBytes.Length);
        Assert.True(aotBytes.Length < 25_165_824);
        await AssertTrx("final_client_full_trx", 35611);
        await AssertTrx("final_aot_smoke_trx", 1);
        JsonElement sources = receipt.GetProperty("harness_sources");
        foreach (string required in new[] { "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.CreativeMining.cs", "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.cs", "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.Adventure.cs", "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceHarness.cs", "tests/Umpk.IntegrationTests/NightlyTheoryAttribute.cs" })
            Assert.True(sources.TryGetProperty(required, out _), "Required fixture source pin absent: " + required);
        foreach (JsonProperty source in sources.EnumerateObject())
            Assert.Equal(source.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(FixturePaths.RepoRoot(), source.Name)))));
        var binaries = Directory.GetFiles(AppContext.BaseDirectory, "Umpk.*").Where(file => Path.GetExtension(file) is ".dll" or ".pdb").ToDictionary(file => Path.GetFileName(file), file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        Assert.Equal(receipt.GetProperty("client_sha256").GetString(), binaries["Umpk.Client.dll"]);
        JsonElement expected = receipt.GetProperty("integration_binaries");
        Assert.Equal(binaries.Keys.Order(StringComparer.Ordinal), expected.EnumerateObject().Select(binary => binary.Name).Order(StringComparer.Ordinal));
        foreach (var binary in binaries)
            Assert.Equal(binary.Value, expected.GetProperty(binary.Key!).GetString());

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
}
