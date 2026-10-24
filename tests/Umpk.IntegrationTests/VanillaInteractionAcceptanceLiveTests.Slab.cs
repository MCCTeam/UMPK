using System.Buffers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    public static IEnumerable<object[]> SlabReleases()
    {
        string[] names = (Environment.GetEnvironmentVariable("UMPK_SLAB_RELEASES") ?? "1.13,1.14,1.17,1.20.6,26.3").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        foreach (string name in names)
        {
            Assert.Contains(name, new[] { "1.13", "1.14", "1.17", "1.20.6", "26.3" });
            Assert.True(JavaVersions.TryGetByName(name, out JavaVersion version));
            yield return [name, version.Version.Protocol];
        }
    }

    [NightlyTheory]
    [MemberData(nameof(SlabReleases))]
    public async Task ModernSlabPlacementMatchesNativeStatesStacksAndSwing(string release, int protocol)
    {
        (string? levelDataSeed, string? levelDataSeedSha256) = await VerifySlabPrerequisites(protocol);
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        string? flatWorldSettings = protocol switch
        {
            < 766 => null,
            _ => """{"layers":[{"height":164,"block":"minecraft:stone"}],"biome":"minecraft:the_void","features":false,"lakes":false,"structure_overrides":[]}""",
        };
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct, flatWorldSettings: flatWorldSettings, levelDataSeed: levelDataSeed, levelDataSeedSha256: levelDataSeedSha256);
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        client.PacketFrameObserved += capture.Observe;
        var markers = new List<string>();
        var scenarios = new List<object>();
        string actorTag = "umpk_slab_" + Guid.NewGuid().ToString("N");
        int? actorId = null;
        bool passed = false;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "slab native terrain", ct);
            Assert.True(client.State.Features.Physics && client.State.Features.Terrain && client.State.Features.Entities);
            Vec3d initial = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(initial.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(initial.Z) >> 4) * 16 + 8;
            Vec3d self = new(x + .5, 100, z + .5);
            var destination = new BlockPos(x + 2, 100, z + 1);
            BlockPos anchor = destination.Offset(Direction.East);
            var staging = new Vec3d(x + .5, 100, z - 2.5);
            await server.ConsoleAsync("difficulty peaceful", ct);
            string spawnRule = protocol < 774 ? "doMobSpawning" : "minecraft:spawn_mobs";
            await server.ConsoleAsync($"gamerule {spawnRule} false", ct);
            string spawnRuleResponse = await server.CommandAsync($"gamerule {spawnRule}", ct);
            Assert.EndsWith("false", spawnRuleResponse, StringComparison.Ordinal);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobLoot false" : "gamerule minecraft:mob_drops false", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doTileDrops false" : "gamerule minecraft:block_drops false", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await ClearCells();
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} {self.Y:R} {self.Z:R} 0 0"), ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && !c.State.Self.InstantBuild && c.State.Self.OnGround && Near(c.State.Self.Position, self), "native grounded Survival slab owner", ct);
            Assert.Contains(capture.Frames, frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == (x >> 4) && chunk.ChunkZ == (z >> 4));
            await AssertBlockAsync(server, protocol, destination.Below(), "stone", ct);
            await Marker(FormattableString.Invariant($"execute if entity @a[name={Username},gamemode=survival,nbt={{Pos:[{self.X:R}d,{self.Y:R}d,{self.Z:R}d],OnGround:1b}}]"), "grounded-native-position");
            int[] initialIds = await client.InvokeAsync(c => c.State.Entities.All.Select(entity => entity.Id).ToArray(), ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp @e[type=!minecraft:player] {staging.X:R} {staging.Y:R} {staging.Z:R}"), ct);
            await server.ConsoleAsync("kill @e[type=!minecraft:player]", ct);
            await server.ConsoleAsync("kill @e[type=minecraft:item]", ct);
            await WaitAsync(client, c => !c.State.Entities.All.Any(), "staged ambient actors actually removed", ct);
            int cleanupMarker = await Marker("execute unless entity @e[type=!minecraft:player]", "zero-ambient-native");
            CapturedFrame[] setupFrames = capture.Frames.Take(cleanupMarker).ToArray();
            int[] observedIds = initialIds.Concat(setupFrames.Select(frame => SlabSpawnId(frame.Packet)).OfType<int>()).Distinct().Order().ToArray();
            foreach (int id in observedIds)
                Assert.Contains(setupFrames, frame => frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(id));
            await server.WriteAsync("slab-controlled-setup.json", new { initialIds, observedIds, staging, self, destination, naturalSpawningDisabled = true, ambientActorsMovedIntoReceivedStagingColumnBeforeKill = true, nativeAndClientZeroActors = true, spawnRule, spawnRuleResponse, limits = "Controlled peaceful no-spawn placement fixture; no general natural mob movement or AI claim." });

            await Use("ordinary-bottom", "bottom", success: true);
            await Use("ordinary-top", "top", success: true);
            await Use("matching-bottom-double", "double", success: true);
            await Use("matching-top-double", "double", success: true);
            await Use("adjacent-bottom-double", "double", success: true);
            await Use("adjacent-top-double", "double", success: true);
            await Use("occupied-destination", "stone", success: false);
            await Use("waterlogged-destination", "bottom", success: true, waterlogged: true);
            await Use("upper-body-bottom-clear", "bottom", success: true, body: true);
            await Use("upper-body-top-blocked", "air", success: false, body: true);
            await Use("upper-body-full-merge-blocked", "bottom", success: false, body: true);
            await RemoveActor();
            await Marker("execute unless entity @e[type=!minecraft:player]", "final-zero-actors");
            Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.ToArray(), ct));
            Assert.Equal(11, scenarios.Count);
            Assert.False(capture.Overflowed);
            Assert.True(capture.PayloadBytes <= 20 * 1024 * 1024);
            RegistryAccess registries = client.State.Registries!;
            int actions = capture.AssertRoundTrips(registries);
            int inventory = ReplayAdventureFrames(capture.Frames, [], version, registries);
            // The maintained motion helper expects sequence-bearing use packets. Capture.AssertRoundTrips above owns all era-normalized uses instead.
            int context = AssertMotionRoundTrips(version, capture.Frames.Where(frame => frame.Packet is not (ServerboundUseItemOnPacket or ClientboundDisguisedChatPacket)).ToArray(), registries);
            int supplemental = ReplaySlabSupplement(capture.Frames, markers, version, registries);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection: Disconnected", StringComparison.Ordinal), "native peer EOF after client transport disconnect", ct);
            Assert.Equal(ClientStatus.Disconnected, client.Status);
            await server.WriteAsync("slab-acceptance.json", new { release, protocol, scenarios, actions, inventory, context, supplemental, markers, physics = true, defaultComponents = true, exactNativeStatesItemsAndPositions = true, peerObservedEof = true, peerEofEvidence = "Native lost connection: Disconnected after local transport close; no serverbound disconnect packet exists.", recordingCap = 20 * 1024 * 1024, postActionTailSeconds = 2.1, capture.PayloadBytes, nativeMinecraftClientInputOrRenderingExecuted = false, protocol777LocalSwing = "Actual owner Local intent plus one use/no animation and independent server result; no graphical arm-animation claim." });
            passed = true;

            async Task ClearCells()
                => await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);

            async Task<int> Marker(string condition, string label)
            {
                string marker = "slab-" + label + "-" + Guid.NewGuid().ToString("N");
                markers.Add(marker);
                await server.ConsoleAsync(condition + " run tellraw " + Username + " {\"text\":\"" + marker + "\"}", ct);
                await WaitAsync(() => capture.Frames.Any(frame => SlabMarkerMatches(frame.Packet, marker)), "exact native causal slab marker " + label, ct);
                await client.InvokeAsync(_ => true, ct);
                return Array.FindIndex(capture.Frames, frame => SlabMarkerMatches(frame.Packet, marker));
            }

            async Task RemoveActor()
            {
                if (actorId is not int id)
                    return;
                int mark = capture.Mark();
                await server.ConsoleAsync($"kill @e[tag={actorTag}]", ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(id)), "owned villager native removal frame", ct);
                await WaitAsync(client, c => c.State.Entities.Get(id) is null, "owned villager removal applied", ct);
                actorId = null;
            }

            async Task SpawnUpperBody()
            {
                int mark = capture.Mark();
                var position = new Vec3d(destination.X + .5, destination.Y + .75, destination.Z + .5);
                await server.ConsoleAsync(FormattableString.Invariant($"summon minecraft:villager {position.X:R} {position.Y:R} {position.Z:R} {{Tags:[\"{actorTag}\"],Age:-1000000,NoAI:1b,NoGravity:1b,Silent:1b,PersistenceRequired:1b}}"), ct);
                await WaitAsync(client, c => c.State.Entities.All.Count() == 1 && c.State.Entities.All.Single().Type.Id == Identifier.Minecraft("villager"), "exact owned native villager", ct);
                Entity actor = await client.InvokeAsync(c => Assert.Single(c.State.Entities.All), ct);
                actorId = actor.Id;
                Assert.True(JavaGameData.EntityGeometryData(protocol).TryGetRule(actor.Type.Id, out EntityGeometryRule rule));
                await WaitAsync(() => capture.Since(mark).Any(frame => SlabMetadata(frame.Packet, actor.Id, rule.MetadataIndex, true)), "native child metadata before adult transition", ct);
                await server.ConsoleAsync($"data merge entity @e[tag={actorTag},limit=1] {{Age:0}}", ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => SlabMetadata(frame.Packet, actor.Id, rule.MetadataIndex, false)), "actual dirty adult metadata", ct);
                await server.ConsoleAsync(FormattableString.Invariant($"tp @e[tag={actorTag},limit=1] {position.X:R} {position.Y:R} {position.Z:R}"), ct);
                await WaitAsync(client, c => c.State.Entities.Get(actor.Id)?.Position == position, "native body position applied", ct);
                await Marker(FormattableString.Invariant($"execute if entity @e[type=minecraft:villager,tag={actorTag},nbt={{Pos:[{position.X:R}d,{position.Y:R}d,{position.Z:R}d],Age:0,NoAI:1b,NoGravity:1b}}] unless entity @e[type=!minecraft:player,tag=!{actorTag}]"), "exact-adult-body");
                var body = await client.InvokeAsync(c => ReadSlabBody(c, actor.Id, protocol), ct);
                Assert.True(body.Member && body.Known && body.BlocksBuilding);
                Assert.Equal(position, actor.Position);
                Assert.Equal(destination.Y + .75, body.Box.MinY);
                Assert.True(body.Box.MinY > destination.Y + .5 && body.Box.MinY < destination.Y + 1);
                Assert.True(body.Box.MinX < destination.X + 1 && body.Box.MaxX > destination.X);
            }

            async Task Set(BlockPos cell, string state, Func<BlockState, bool> matches)
            {
                await server.ConsoleAsync($"setblock {cell.X} {cell.Y} {cell.Z} minecraft:{state}", ct);
                await WaitAsync(client, c => matches(c.State.World.GetBlock(cell)), "native slab setup " + state, ct);
                await AssertBlockAsync(server, protocol, cell, state, ct);
            }

            async Task Use(string label, string result, bool success, bool waterlogged = false, bool body = false)
            {
                await RemoveActor();
                await ClearCells();
                await WaitAsync(client, c => c.State.World.GetBlock(destination).IsAir && c.State.World.GetBlock(anchor).IsAir, "native cleared slab cells", ct);
                Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.ToArray(), ct));
                await Set(anchor, "stone", block => block.IsValid && block.Block.Id == Identifier.Minecraft("stone"));
                BlockPos clicked = anchor;
                Direction face = Direction.West;
                Vec3d cursor = new(0, label is "ordinary-top" or "upper-body-top-blocked" ? .75 : .25, .5);
                string? initialHalf = label switch
                {
                    "matching-bottom-double" or "adjacent-bottom-double" or "upper-body-full-merge-blocked" => "bottom",
                    "matching-top-double" or "adjacent-top-double" => "top",
                    _ => null,
                };
                if (initialHalf is not null)
                    await Set(destination, $"oak_slab[type={initialHalf},waterlogged=false]", block => SlabState(block, initialHalf, false));
                if (label is "matching-bottom-double" or "matching-top-double" or "upper-body-full-merge-blocked")
                {
                    clicked = destination;
                    face = label == "matching-top-double" ? Direction.Down : Direction.Up;
                    cursor = new(.5, .5, .5);
                }
                if (label == "occupied-destination")
                    await Set(destination, "stone", block => block.IsValid && block.Block.Id == Identifier.Minecraft("stone"));
                if (waterlogged)
                {
                    foreach (Direction side in new[] { Direction.North, Direction.South, Direction.East, Direction.West })
                        await Set(destination.Offset(side), "stone", block => block.IsValid && block.Block.Id == Identifier.Minecraft("stone"));
                    await Set(destination, "water[level=0]", block => block.IsValid && block.Block.Id == Identifier.Minecraft("water") && block.TryGetProperty("level", out string level) && level == "0");
                    clicked = destination.Below();
                    face = Direction.Up;
                    cursor = new(.5, 1, .5);
                }
                if (body)
                    await SpawnUpperBody();
                await server.ConsoleAsync($"clear {Username}", ct);
                await server.ConsoleAsync($"give {Username} minecraft:oak_slab 32", ct);
                await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("oak_slab") && c.State.Inventory.GetSlot(36).Count == 32 && c.State.Inventory.GetSlot(36).Components.HasNoPatch, "native default slab stack", ct);
                await AssertSlabCount(server, 32, ct);
                await Marker($"execute if entity @a[name={Username},gamemode=survival,nbt={{SelectedItem:{{id:\"minecraft:oak_slab\"}}}}]", "native-main-hand");
                await WaitAsync(client, c => c.State.Self.OnGround && Near(c.State.Self.Position, self), "retained real physics position", ct);
                Vec3d hit = new(clicked.X + cursor.X, clicked.Y + cursor.Y, clicked.Z + cursor.Z);
                await client.Actions.Movement.LookAtAsync(hit, ct);
                var snapshot = await client.Snapshots.SelfAsync(ct);
                Assert.True(snapshot.EyePosition.Subtract(hit).LengthSqr() < 4.5 * 4.5, "The explicit coordinate action must remain within native reach.");
                Aabb selfBox = await client.InvokeAsync(ReadSlabPhysics, ct);
                Assert.False(selfBox.Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)), "The fixture must not obtain a rejection from its own body.");
                await server.WriteAsync(label + "-inputs.json", await client.InvokeAsync(c => VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, clicked, destination), ct));
                var prediction = await client.InvokeAsync(c => ReadSlabPrediction(c, clicked, face, cursor), ct);
                Assert.True(prediction.SendPacket);
                Assert.Equal(success ? "Success" : "Fail", prediction.Result);
                Assert.Equal(success ? protocol == 777 ? "Local" : "Packet" : "None", prediction.Swing);
                int beforeState = await client.InvokeAsync(c => c.State.World.GetBlock(destination).StateId, ct);
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(clicked, face, cursor, Hand.Main, insideBlock: false, ct: ct);
                int count = success ? 31 : 32;
                await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Count == count && (result is "stone" or "air" ? c.State.World.GetBlock(destination).Block.Id == Identifier.Minecraft(result) : SlabState(c.State.World.GetBlock(destination), result, waterlogged)), "native slab result and authoritative stack", ct);
                string nativeResult = result is "stone" or "air" ? result : $"oak_slab[type={result},waterlogged={waterlogged.ToString().ToLowerInvariant()}]";
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                await AssertBlockAsync(server, protocol, destination, nativeResult, ct);
                await AssertSlabCount(server, count, ct);
                await Marker($"execute if block {destination.X} {destination.Y} {destination.Z} minecraft:{nativeResult}", label + "-native-result");
                Assert.Equal(count, await client.InvokeAsync(c => c.State.Inventory.GetSlot(36).Count, ct));
                CapturedFrame[] segment = capture.Since(mark);
                CapturedFrame[] actions = segment.Where(IsUseOrAnimation).ToArray();
                Assert.Equal(success && protocol != 777 ? 2 : 1, actions.Length);
                var use = Assert.IsType<ServerboundUseItemOnPacket>(actions[0].Packet);
                Assert.Equal((clicked, (int)face, 0), (use.Position, use.Face, use.Hand));
                Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
                Assert.False(use.Inside || use.WorldBorderHit);
                if (success && protocol != 777)
                    Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                Assert.DoesNotContain(actions, frame => frame.Packet is ServerboundPunchPacket);
                Assert.True(segment[^1].Milliseconds - actions[0].Milliseconds >= 900, "Retain the unchanged post-action capture tail.");
                if (protocol >= 759)
                    Assert.Contains(segment, frame => frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == use.Sequence);
                if (!success)
                    Assert.Equal(beforeState, await client.InvokeAsync(c => c.State.World.GetBlock(destination).StateId, ct));
                if (body)
                    Assert.Equal(actorId, await client.InvokeAsync(c => Assert.Single(c.State.Entities.All).Id, ct));
                Assert.False(capture.Overflowed);
                scenarios.Add(new { label, clicked, face, cursor, destination, nativeResult, count, prediction, actorId, selfBox, explicitCoordinateCaller = true, actionPackets = actions.Select(frame => new { frame.Flow, frame.WireId, packet = frame.Packet!.GetType().Name, frame.Payload }).ToArray() });
                await server.WriteAsync("slab-scenarios.json", scenarios);
            }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "modern default slab placement", capture.PayloadBytes, capture.Overflowed, completedCases = scenarios.Count });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static async Task<(string? Path, string? Sha256)> VerifySlabPrerequisites(int protocol)
    {
        string prerequisite = Environment.GetEnvironmentVariable("UMPK_SLAB_PREREQUISITE_RECEIPT") ?? throw new InvalidOperationException("Supply the reviewed merged-producer slab prerequisite receipt before native execution.");
        byte[] bytes = await File.ReadAllBytesAsync(prerequisite);
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_SLAB_PREREQUISITE_SHA256"), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument receipt = JsonDocument.Parse(bytes);
        JsonElement ready = receipt.RootElement;
        Assert.True(ready.GetProperty("native_ready").GetBoolean());
        Assert.False(ready.GetProperty("offline_compatibility_only").GetBoolean());
        string producer = Environment.GetEnvironmentVariable("UMPK_INTERACTION_PRODUCER") ?? throw new InvalidOperationException("Supply the exact reviewed producer commit.");
        Assert.False(string.IsNullOrWhiteSpace(producer));
        Assert.Equal(producer, ready.GetProperty("producer_commit").GetString());
        Assert.Equal(25, ready.GetProperty("passed_repository_gates").GetInt32());
        Assert.Equal(2344, ready.GetProperty("executed_slab_cases").GetInt32());
        Assert.True(ready.GetProperty("consumer_packages_verified").GetBoolean());
        Assert.Equal(1, ready.GetProperty("executed_passing_AOT_smokes").GetInt32());
        Assert.True(ready.GetProperty("aot_bytes").GetInt32() < 25_165_824);
        string fixture = Path.Combine(FixturePaths.RepoRoot(), "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.Slab.cs");
        Assert.Equal(ready.GetProperty("fixture_source_sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(fixture))));
        string[] requiredSources = ["VanillaInteractionAcceptanceHarness.cs", "VanillaInteractionAcceptanceLiveTests.cs", "VanillaInteractionAcceptanceLiveTests.Adventure.cs", "VanillaInteractionAcceptanceLiveTests.Motion.cs", "VanillaInteractionAcceptanceDiagnostics.cs"];
        foreach (string source in requiredSources)
            Assert.Equal(ready.GetProperty("harness_sources").GetProperty(source).GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(FixturePaths.RepoRoot(), "tests/Umpk.IntegrationTests", source)))));
        var actual = Directory.GetFiles(AppContext.BaseDirectory, "Umpk.*").Where(path => Path.GetExtension(path) is ".dll" or ".pdb").ToDictionary(path => Path.GetFileName(path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        JsonElement binaries = ready.GetProperty("integration_binaries");
        Assert.Equal(actual.Keys.Order(StringComparer.Ordinal), binaries.EnumerateObject().Select(binary => binary.Name).Order(StringComparer.Ordinal));
        foreach (var binary in actual)
            Assert.Equal(binary.Value, binaries.GetProperty(binary.Key!).GetString());
        if (protocol != 755)
            return (null, null);
        JsonElement seed = ready.GetProperty("level_data_seed_755");
        Assert.Equal("1.17", seed.GetProperty("release").GetString());
        Assert.Equal(755, seed.GetProperty("protocol").GetInt32());
        string path = seed.GetProperty("path").GetString()!;
        string generationReceipt = seed.GetProperty("generation_receipt").GetString()!;
        Assert.True(Path.IsPathFullyQualified(path) && Path.IsPathFullyQualified(generationReceipt));
        Assert.Equal("level.dat", Path.GetFileName(path));
        Assert.Equal(seed.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))));
        Assert.Equal(seed.GetProperty("generation_receipt_sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(generationReceipt))));
        return (path, seed.GetProperty("sha256").GetString());
    }

    private static bool SlabState(BlockState state, string half, bool waterlogged)
        => state.IsValid && state.Block.Id == Identifier.Minecraft("oak_slab") && state.TryGetProperty("type", out string type) && type == half && state.TryGetProperty("waterlogged", out string water) && water == (waterlogged ? "true" : "false");

    private static async Task AssertSlabCount(VanillaInteractionAcceptanceHarness server, int count, CancellationToken ct)
    {
        string response = await server.CommandAsync($"clear {Username} minecraft:oak_slab 0", ct);
        Assert.Contains(response, new[] { $"Found {count} matching items on player {Username}", $"Found {count} matching item(s) on player {Username}" });
    }

    private static bool SlabMarkerMatches(object? packet, string marker)
        => packet switch
        {
            ClientboundLegacyChatPacket chat => chat.Message.ToPlainText() == marker,
            ClientboundSystemChatPacket chat => chat.Content.ToPlainText() == marker,
            _ => false,
        };

    private static bool SlabMetadata(object? packet, int id, int index, bool baby)
        => packet switch
        {
            ClientboundAddMobPacket spawn when spawn.EntityId == id => spawn.Metadata.Entries.Any(entry => entry.Index == index && entry.Value.Equals(MetadataValue.Boolean(baby))),
            ClientboundSetEntityDataPacket data when data.EntityId == id => data.Metadata.Entries.Any(entry => entry.Index == index && entry.Value.Equals(MetadataValue.Boolean(baby))),
            _ => false,
        };

    private static int? SlabSpawnId(object? packet) => packet switch
    {
        ClientboundAddMobPacket actor => actor.EntityId,
        ClientboundAddEntityPacket actor => actor.EntityId,
        ClientboundAddPaintingPacket actor => actor.EntityId,
        ClientboundAddExperienceOrbPacket actor => actor.EntityId,
        _ => null,
    };

    private static Aabb ReadSlabPhysics(UmpkClient client)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object owner = typeof(UmpkClient).GetField("_physics", fields)!.GetValue(client)!;
        return ((PhysicsState)owner.GetType().GetProperty("EngineState", fields)!.GetValue(owner)!).BoundingBox;
    }

    // This invokes the existing complete predictor on Client.InvokeAsync. It adds no alternate controller or production test state.
    private static SlabPrediction ReadSlabPrediction(UmpkClient client, BlockPos clicked, Direction face, Vec3d cursor)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object owner = client.Actions.Interaction;
        object prediction = owner.GetType().GetMethod("PredictBlockUse", fields)!.Invoke(owner, [clicked, face, cursor, Hand.Main])!;
        return new((bool)prediction.GetType().GetProperty("SendPacket")!.GetValue(prediction)!, prediction.GetType().GetProperty("Result")!.GetValue(prediction)!.ToString()!, prediction.GetType().GetProperty("Swing")!.GetValue(prediction)!.ToString()!);
    }

    private static SlabBody ReadSlabBody(UmpkClient client, int id, int protocol)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Entity actor = client.State.Entities.Get(id)!;
        object membership = typeof(ClientState).GetProperty("EntityMembership", fields)!.GetValue(client.State)!;
        bool member = (bool?)membership.GetType().GetMethod("IsWorldQueryMember")!.Invoke(membership, [actor]) == true;
        object geometry = typeof(ClientState).GetProperty("EntityGeometry", fields)!.GetValue(client.State)!;
        object?[] arguments = [actor, JavaGameData.EntityGeometryData(protocol), default(Aabb), false, protocol];
        bool known = (bool)geometry.GetType().GetMethod("TryGet")!.Invoke(geometry, arguments)!;
        return new(member, known, (Aabb)arguments[2]!, (bool)arguments[3]!);
    }

    private sealed record SlabPrediction(bool SendPacket, string Result, string Swing);
    private sealed record SlabBody(bool Member, bool Known, Aabb Box, bool BlocksBuilding);

    private static int ReplaySlabSupplement(CapturedFrame[] frames, IReadOnlyCollection<string> markers, JavaVersion version, RegistryAccess registries)
    {
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;
        foreach (CapturedFrame frame in frames)
        {
            string? marker = markers.SingleOrDefault(value => SlabMarkerMatches(frame.Packet, value));
            if (marker is null && frame.Packet is not (ClientboundAddMobPacket or ClientboundSectionBlocksUpdatePacket or ClientboundPlayerPositionPacket or ClientboundPlayerAbilitiesPacket or ClientboundGameEventPacket))
                continue;
            Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, frame.Flow).TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, context);
            if (frame.Packet is ClientboundAddMobPacket spawn)
            {
                var read = Assert.IsType<ClientboundAddMobPacket>(decoded);
                Assert.Equal((spawn.EntityId, spawn.Uuid, spawn.TypeId, spawn.X, spawn.Y, spawn.Z, spawn.VelocityX, spawn.VelocityY, spawn.VelocityZ), (read.EntityId, read.Uuid, read.TypeId, read.X, read.Y, read.Z, read.VelocityX, read.VelocityY, read.VelocityZ));
                Assert.Equal(spawn.Metadata.Entries, read.Metadata.Entries);
                Assert.Equal(spawn.Metadata.RawTail, read.Metadata.RawTail);
            }
            else if (frame.Packet is ClientboundSectionBlocksUpdatePacket changes)
            {
                var read = Assert.IsType<ClientboundSectionBlocksUpdatePacket>(decoded);
                Assert.Equal((changes.SectionPos, changes.LegacyChunkX, changes.LegacyChunkZ, changes.IsLegacy), (read.SectionPos, read.LegacyChunkX, read.LegacyChunkZ, read.IsLegacy));
                Assert.Equal(changes.Changes, read.Changes);
            }
            else if (marker is not null)
            {
                Assert.True(seen.Add(marker));
                Assert.True(SlabMarkerMatches(decoded, marker));
            }
            else
                Assert.Equal(frame.Packet, decoded);
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(frame.Payload, buffer.WrittenSpan.ToArray());
            count++;
        }
        Assert.Equal(markers.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        return count;
    }
}
