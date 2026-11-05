using System.Reflection;
using System.Text.Json;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    public static IEnumerable<object[]> LadderReleases()
    {
        (string Release, int Protocol)[] rows = [("1.16", 735), ("1.17", 755), ("1.20.5", 766), ("26.3", 777)];
        string? selected = Environment.GetEnvironmentVariable("UMPK_LADDER_RELEASE");
        Assert.True(selected is null || rows.Any(row => row.Release == selected), "UMPK_LADDER_RELEASE must name a bound ladder release.");
        foreach ((string release, int protocol) in rows)
            if (selected is null || selected == release)
                yield return [release, protocol];
    }

    [NightlyTheory]
    [MemberData(nameof(LadderReleases))]
    public async Task LadderPlacementMatchesNativeStoneFacesHandsAndThinObstruction(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        Assert.Equal(protocol, version.Version.Protocol);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        SpecializedFlatSeed? levelSeed = await ReadSpecializedFlatSeedAsync(release, protocol, ct);
        string flatWorldSettings = protocol < 766
            ? """{"structures":{"structures":{}},"layers":[{"height":100,"block":"minecraft:stone"}],"biome":"minecraft:the_void","features":false,"lakes":false}"""
            : """{"layers":[{"height":164,"block":"minecraft:stone"}],"biome":"minecraft:the_void","features":false,"lakes":false,"structure_overrides":[]}""";
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct, flatWorldSettings: levelSeed is null ? flatWorldSettings : null, levelDataSeed: levelSeed?.Path, levelDataSeedSha256: levelSeed?.Sha256);
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version, includeBiomeRegistry: protocol >= 766);
        client.PacketFrameObserved += capture.Observe;
        var observations = new List<object>();
        var markers = new List<string>();
        string actorTag = "umpk_ladder_" + Guid.NewGuid().ToString("N");
        int? actorId = null;
        bool passed = false;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "ladder native terrain", ct);
            Assert.True(client.State.Features.Physics && client.State.Features.Terrain && client.State.Features.Entities);
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(spawn.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(spawn.Z) >> 4) * 16 + 8;
            {
                string[] lines = await File.ReadAllLinesAsync(Path.Combine(server.DirectoryPath, "server.properties"), ct);
                var properties = lines.Where(line => line.StartsWith("level-type=", StringComparison.Ordinal) || line.StartsWith("generator-settings=", StringComparison.Ordinal))
                    .ToDictionary(line => line[..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..].Replace("\\:", ":", StringComparison.Ordinal).Replace("\\=", "=", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal));
                if (levelSeed is null)
                {
                    Assert.Equal("flat", properties["level-type"]);
                    using JsonDocument expected = JsonDocument.Parse(flatWorldSettings);
                    using JsonDocument actual = JsonDocument.Parse(properties["generator-settings"]);
                    Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
                }
                else
                    await AssertSpecializedFlatSeedCopyAsync(server, levelSeed, release, ct);
                int minY = protocol < 766 ? 0 : -64;
                Assert.Equal(minY, await client.InvokeAsync(c => c.State.World.Dimension.MinY, ct));
                Assert.Contains(capture.Frames, frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == (x >> 4) && chunk.ChunkZ == (z >> 4));
                int nativeBiomeFrames;
                ModernFlatBiomeWitness? modernNativeBiomes = null;
                if (protocol < 766)
                    nativeBiomeFrames = AssertPreservedFlatNativeBiomes(capture, version, client.State.Registries!, protocol, x >> 4, z >> 4);
                else
                {
                    modernNativeBiomes = AssertReceivedModernFlatNativeBiomes(capture, version, client.State.Registries!, protocol, x >> 4, z >> 4);
                    nativeBiomeFrames = modernNativeBiomes.ChunkFrames;
                    Assert.Equal("Test passed", await server.CommandAsync($"execute if biome {x} 100 {z} minecraft:the_void", ct));
                }
                foreach (string condition in new[] { $"execute if block {x} {minY} {z} minecraft:stone", $"execute if block {x} 99 {z} minecraft:stone", $"execute if block {x} 100 {z} minecraft:air" })
                    Assert.Equal("Test passed", await server.CommandAsync(condition, ct));
                Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.ToArray(), ct));
                Assert.Equal("Test passed", await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct));
                await server.WriteAsync("ladder-generator-preconditions.json", new { release, protocol, properties, levelSeed, worldInputKind = levelSeed is null ? "server-properties-json" : "same-version-level.dat", minY, floorY = 99, preScenarioFill = true, receivedVoidBiome = true, gameplayBiomeProjectionKnown = false, nativeBiomeFrames, modernNativeBiomes, receivedFullColumn = true, initialNativeAndClientEntitiesEmpty = true });
            }
            Vec3d self = new(x + .5, 100, z + .5);
            var destination = new BlockPos(x + 2, 100, z + 1);
            await server.ConsoleAsync("difficulty peaceful", ct);
            foreach ((string oldName, string newName) in new[] { ("doMobSpawning", "spawn_mobs"), ("doMobLoot", "mob_drops"), ("doTileDrops", "block_drops"), ("doEntityDrops", "entity_drops") })
            {
                string rule = protocol == 777 ? newName : oldName;
                await server.ConsoleAsync($"gamerule {rule} false", ct);
                string expected = protocol == 777 ? $"Game rule {rule} is currently set to false" : $"Gamerule {rule} is currently set to: false";
                Assert.Equal(expected, await server.CommandAsync($"gamerule {rule}", ct));
            }
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await ClearCells();
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} {self.Y:R} {self.Z:R} 0 0"), ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.OnGround && PillarNear(c.State.Self.Position, self), "ladder grounded physics", ct);
            Assert.Contains(capture.Frames, frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == (x >> 4) && chunk.ChunkZ == (z >> 4));
            var scenes = new List<LadderLiveScene>();
            foreach (Direction face in new[] { Direction.North, Direction.South, Direction.West, Direction.East })
                foreach (Hand hand in new[] { Hand.Main, Hand.Off })
                    scenes.Add(new("dry-" + face + "-" + hand, face, hand));
            scenes.Add(new("plate-overlap", Direction.North, Hand.Main, "plate-overlap"));
            scenes.Add(new("unused-unit-cell", Direction.North, Hand.Main, "unused-unit-cell"));
            foreach (LadderLiveScene scene in scenes)
                await Place(scene);
            Assert.Equal(10, observations.Count);
            await RemoveActor();
            Assert.Equal("Test passed", await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct));
            Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.ToArray(), ct));
            RegistryAccess registries = client.State.Registries!;
            int actionsReplay = capture.AssertRoundTrips(registries);
            int inventoryReplay = ReplayAdventureFrames(capture.Frames, [], version, registries);
            // Capture owns era-normalized use replay; the motion helper expects sequence-bearing use packets.
            int contextReplay = AssertMotionRoundTrips(version, capture.Frames.Where(frame => frame.Packet is not (ServerboundUseItemOnPacket or ClientboundDisguisedChatPacket)).ToArray(), registries);
            int supplementalReplay = ReplaySlabSupplement(capture.Frames, markers, version, registries);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => QuartzNativeDisconnected(server.ServerLog, protocol), "ladder exact native peer EOF", ct);
            Assert.Equal(ClientStatus.Disconnected, client.Status);
            Assert.False(capture.Overflowed);
            await server.WriteAsync("ladder-acceptance.json", new { release, protocol, observations, markers, actionsReplay, inventoryReplay, contextReplay, supplementalReplay, physics = true, explicitCoordinateCaller = true, bothHands = true, peerObservedDisconnect = true, capture.PayloadBytes, recordingCap = 20 * 1024 * 1024, postActionTailSeconds = 2.1, protocol777LocalSwing = "Owner Local intent and one actual USE/no animation packet; no graphical arm claim.", originalGraphicalClientExecuted = false, localBlockOrStackMutationClaim = false });
            passed = true;

            async Task ClearCells() => await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);

            async Task NativeEntity(string condition) => Assert.Equal(protocol == 777 ? "Test passed. Count: 1" : "Test passed, count: 1", await server.CommandAsync(condition, ct));

            async Task NativeCount(int count) => Assert.Equal(protocol < 766 ? $"Found {count} matching items on player {Username}" : $"Found {count} matching item(s) on player {Username}", await server.CommandAsync($"clear {Username} minecraft:ladder 0", ct));

            async Task Marker(string condition, string label)
            {
                string marker = "ladder-" + label + "-" + Guid.NewGuid().ToString("N");
                markers.Add(marker);
                await server.ConsoleAsync(condition + " run tellraw " + Username + " {\"text\":\"" + marker + "\"}", ct);
                await WaitAsync(() => capture.Frames.Any(frame => SlabMarkerMatches(frame.Packet, marker)), "native ladder marker " + label, ct);
                await client.InvokeAsync(_ => true, ct);
            }

            async Task RemoveActor()
            {
                if (actorId is not int id)
                    return;
                int mark = capture.Mark();
                await server.ConsoleAsync($"kill @e[tag={actorTag}]", ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(id)), "ladder actual native removal frame", ct);
                await WaitAsync(client, c => c.State.Entities.Get(id) is null, "ladder native removal applied", ct);
                actorId = null;
            }

            async Task SpawnBody(string scene)
            {
                Vec3d position = new(destination.X + .5, destination.Y, destination.Z + (scene == "plate-overlap" ? .65 : .2));
                int mark = capture.Mark();
                await server.ConsoleAsync(FormattableString.Invariant($"summon minecraft:villager {position.X:R} {position.Y:R} {position.Z:R} {{Tags:[\"{actorTag}\"],Age:-1000000,NoAI:1b,NoGravity:1b,Silent:1b,PersistenceRequired:1b}}"), ct);
                await WaitAsync(client, c => c.State.Entities.All.Count() == 1 && c.State.Entities.All.Single().Type.Id == Identifier.Minecraft("villager"), "ladder native villager spawn", ct);
                Entity actor = await client.InvokeAsync(c => Assert.Single(c.State.Entities.All), ct);
                actorId = actor.Id;
                Assert.True(JavaGameData.EntityGeometryData(protocol).TryGetRule(actor.Type.Id, out EntityGeometryRule rule));
                await WaitAsync(() => capture.Since(mark).Any(frame => SlabMetadata(frame.Packet, actor.Id, rule.MetadataIndex, true)), "ladder original child metadata", ct);
                await server.ConsoleAsync($"data merge entity @e[tag={actorTag},limit=1] {{Age:0}}", ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => SlabMetadata(frame.Packet, actor.Id, rule.MetadataIndex, false)), "ladder actual adult metadata", ct);
                await server.ConsoleAsync(FormattableString.Invariant($"tp @e[tag={actorTag},limit=1] {position.X:R} {position.Y:R} {position.Z:R}"), ct);
                await WaitAsync(client, c => c.State.Entities.Get(actor.Id)?.Position == position, "ladder received body position", ct);
                await NativeEntity(FormattableString.Invariant($"execute if entity @e[type=minecraft:villager,tag={actorTag},nbt={{Pos:[{position.X:R}d,{position.Y:R}d,{position.Z:R}d],Age:0,NoAI:1b,NoGravity:1b}}]"));
                var body = await client.InvokeAsync(c => ReadSlabBody(c, actor.Id, protocol), ct);
                Assert.True(body.Member && body.Known && body.BlocksBuilding);
                Assert.True(body.Box.Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)));
                Aabb plate = new(destination.X, destination.Y, destination.Z + .8125, destination.X + 1, destination.Y + 1, destination.Z + 1);
                Assert.Equal(scene == "plate-overlap", body.Box.Intersects(plate));
                Assert.True(body.Box.MaxZ < destination.Z + 1, "The actor must not intersect the backing STONE.");
                await server.WriteAsync("ladder-body-" + scene + ".json", new { actor.Id, position, body, plate, unitCellIntersection = true, plateIntersection = scene == "plate-overlap" });
            }

            async Task Place(LadderLiveScene scene)
            {
                await RemoveActor();
                await ClearCells();
                await ClearNativeNonPlayerContextAsync(server, client, ct);
                await WaitAsync(client, c => !c.State.Entities.All.Any(), "ladder received ambient removals", ct);
                Assert.Equal("Test passed", await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct));
                BlockPos clicked = destination.Offset(scene.Face.Opposite());
                await server.ConsoleAsync($"setblock {clicked.X} {clicked.Y} {clicked.Z} minecraft:stone", ct);
                await WaitAsync(client, c => c.State.World.GetBlock(clicked).Block.Id == Identifier.Minecraft("stone") && c.State.World.GetBlock(destination).IsAir, "ladder authoritative prepared cells", ct);
                await AssertBlockAsync(server, protocol, clicked, "stone", ct);
                await AssertBlockAsync(server, protocol, destination, "air", ct);
                await server.ConsoleAsync($"gamemode survival {Username}", ct);
                await server.ConsoleAsync($"clear {Username}", ct);
                if (scene.Hand == Hand.Main)
                    await server.ConsoleAsync($"give {Username} minecraft:ladder 32", ct);
                else
                    await server.ConsoleAsync(protocol == 735 ? $"replaceitem entity {Username} weapon.offhand minecraft:ladder 32" : $"item replace entity {Username} weapon.offhand with minecraft:ladder 32", ct);
                int slot = scene.Hand == Hand.Main ? 36 : 45;
                int otherSlot = scene.Hand == Hand.Main ? 45 : 36;
                await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && !c.State.Self.InstantBuild && c.State.Self.OnGround && PillarNear(c.State.Self.Position, self) && c.State.Inventory.GetSlot(slot).Item.Id == Identifier.Minecraft("ladder") && c.State.Inventory.GetSlot(slot).Count == 32 && c.State.Inventory.GetSlot(slot).Components.HasNoPatch && c.State.Inventory.GetSlot(otherSlot).IsEmpty, "ladder native selected hand and grounded mode", ct);
                await NativeCount(32);
                string handPredicate = scene.Hand == Hand.Main ? "SelectedItem:{id:\"minecraft:ladder\"}" : protocol == 777 ? "equipment:{offhand:{id:\"minecraft:ladder\"}}" : "Inventory:[{Slot:-106b,id:\"minecraft:ladder\"}]";
                await NativeEntity(FormattableString.Invariant($"execute if entity @a[name={Username},gamemode=survival,nbt={{Pos:[{self.X:R}d,{self.Y:R}d,{self.Z:R}d],OnGround:1b,{handPredicate}}}]"));
                await Marker("execute if entity @a[name=" + Username + ",gamemode=survival]", scene.Name + "-input");
                if (scene.Body is not null)
                    await SpawnBody(scene.Body);
                Vec3d cursor = scene.Face switch { Direction.North => new(.5, .5, 0), Direction.South => new(.5, .5, 1), Direction.West => new(0, .5, .5), _ => new(1, .5, .5) };
                Vec3d hit = new(clicked.X + cursor.X, clicked.Y + cursor.Y, clicked.Z + cursor.Z);
                await client.Actions.Movement.LookAtAsync(hit, ct);
                Assert.True((await client.Snapshots.SelfAsync(ct)).EyePosition.Subtract(hit).LengthSqr() < 4.5 * 4.5);
                Assert.False((await client.InvokeAsync(ReadSlabPhysics, ct)).Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)));
                var inputs = await client.InvokeAsync(c => new { before = VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, clicked, destination), prediction = ReadLadderPrediction(c, clicked, scene.Face, cursor, scene.Hand), after = VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, clicked, destination) }, ct);
                await server.WriteAsync("ladder-" + scene.Name + "-inputs.json", inputs);
                bool success = scene.Body != "plate-overlap";
                string expectedResult = success ? "Success" : "Fail";
                string expectedSwing = success ? protocol == 777 ? "Local" : "Packet" : "None";
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(clicked, scene.Face, cursor, scene.Hand, insideBlock: false, ct: ct);
                int count = success ? 31 : 32;
                string facing = scene.Face.ToString().ToLowerInvariant();
                string resultState = success ? "ladder[facing=" + facing + ",waterlogged=false]" : "air";
                await WaitAsync(client, c => c.State.Inventory.GetSlot(slot).Count == count && (success ? LadderState(c.State.World.GetBlock(destination), facing) : c.State.World.GetBlock(destination).IsAir), "ladder authoritative block and selected stack result", ct);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                await AssertBlockAsync(server, protocol, destination, resultState, ct);
                await NativeCount(count);
                if (scene.Body is not null)
                    await NativeEntity($"execute if entity @e[type=minecraft:villager,tag={actorTag},nbt={{Age:0,NoAI:1b,NoGravity:1b}}]");
                CapturedFrame[] segment = capture.Since(mark);
                CapturedFrame[] actions = segment.Where(IsUseOrAnimation).ToArray();
                int expectedPackets = success && protocol != 777 ? 2 : 1;
                observations.Add(new { scene.Name, scene.Face, scene.Hand, scene.Body, clicked, destination, cursor, slot, facing, resultState, nativeCount = count, inputs.prediction, expectedResult, expectedSwing, expectedPackets, observedPackets = actions.Length, actions = actions.Select(frame => new { frame.Flow, frame.WireId, packet = frame.Packet!.GetType().Name, payload = Convert.ToHexStringLower(frame.Payload), frame.Milliseconds }).ToArray() });
                await server.WriteAsync("ladder-scenes.json", observations);
                await server.WriteAsync("capture.json", capture.Frames);
                JsonSerializerOptions diagnosticOptions = new() { IncludeFields = true };
                Assert.Equal(JsonSerializer.Serialize(inputs.before, diagnosticOptions), JsonSerializer.Serialize(inputs.after, diagnosticOptions));
                Assert.True(inputs.prediction.SendPacket);
                Assert.Equal(expectedResult, inputs.prediction.Result);
                Assert.Equal(expectedSwing, inputs.prediction.Swing);
                Assert.Equal(expectedPackets, actions.Length);
                var use = Assert.IsType<ServerboundUseItemOnPacket>(actions[0].Packet);
                Assert.Equal((clicked, (int)scene.Face, (int)scene.Hand), (use.Position, use.Face, use.Hand));
                Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
                Assert.False(use.Inside || use.WorldBorderHit);
                if (success && protocol != 777)
                    Assert.Equal((int)scene.Hand, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                if (protocol >= 759)
                    Assert.Single(segment, frame => frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == use.Sequence);
                Assert.True(segment[^1].Milliseconds - actions[0].Milliseconds >= 900);
                Assert.False(capture.Overflowed);
                await Marker($"execute if block {destination.X} {destination.Y} {destination.Z} minecraft:{resultState}", scene.Name + "-result");
            }
        }
        finally
        {
            capture.Stop();
            if (protocol >= 766)
                await server.WriteAsync("ladder-biome-registry-frames.json", capture.BiomeRegistryFrames);
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "horizontal STONE ladder placement and thin obstruction", completedCases = observations.Count, capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static bool LadderState(BlockState state, string facing) => state.IsValid && state.Block.Id == Identifier.Minecraft("ladder") && state.TryGetProperty("facing", out string actual) && actual == facing && state.TryGetProperty("waterlogged", out string wet) && wet == "false";

    private static SlabPrediction ReadLadderPrediction(UmpkClient client, BlockPos clicked, Direction face, Vec3d cursor, Hand hand)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object owner = client.Actions.Interaction;
        object prediction = owner.GetType().GetMethod("PredictBlockUse", fields)!.Invoke(owner, [clicked, face, cursor, hand])!;
        return new((bool)prediction.GetType().GetProperty("SendPacket")!.GetValue(prediction)!, prediction.GetType().GetProperty("Result")!.GetValue(prediction)!.ToString()!, prediction.GetType().GetProperty("Swing")!.GetValue(prediction)!.ToString()!);
    }

    private sealed record LadderLiveScene(string Name, Direction Face, Hand Hand, string? Body = null);
}
