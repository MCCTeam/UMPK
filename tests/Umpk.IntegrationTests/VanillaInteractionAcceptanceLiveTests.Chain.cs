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
    public static IEnumerable<object[]> ChainReleases()
    {
        (string Release, int Protocol)[] rows = [("1.16", 735), ("1.16.2", 751), ("1.21.10", 773), ("26.3", 777)];
        string? selected = Environment.GetEnvironmentVariable("UMPK_CHAIN_RELEASE");
        Assert.True(selected is null || rows.Any(row => row.Release == selected), "UMPK_CHAIN_RELEASE must name a bound chain release.");
        foreach ((string release, int protocol) in rows)
            if (selected is null || selected == release)
                yield return [release, protocol];
    }

    [NightlyTheory]
    [MemberData(nameof(ChainReleases))]
    public async Task ChainPlacementMatchesNativeAxesFluidRodsAndAdventureOrder(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        Assert.Equal(protocol, version.Version.Protocol);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct);
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        client.PacketFrameObserved += capture.Observe;
        var observations = new List<object>();
        var markers = new List<string>();
        string actorTag = "umpk_chain_" + Guid.NewGuid().ToString("N");
        int? actorId = null;
        bool passed = false;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "chain native terrain", ct);
            Assert.True(client.State.Features.Physics && client.State.Features.Terrain && client.State.Features.Entities);
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(spawn.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(spawn.Z) >> 4) * 16 + 8;
            Vec3d self = new(x + .5, 100, z + .5);
            var destination = new BlockPos(x + 2, 100, z + 1);
            await server.ConsoleAsync("difficulty peaceful", ct);
            foreach ((string oldName, string newName, string value) in new[]
            {
                ("doMobSpawning", "spawn_mobs", "false"),
                ("doMobLoot", "mob_drops", "false"),
                ("doTileDrops", "block_drops", "false"),
                ("doEntityDrops", "entity_drops", "false"),
                ("randomTickSpeed", "random_tick_speed", "0"),
            })
            {
                string rule = protocol < 774 ? oldName : newName;
                await server.ConsoleAsync($"gamerule {rule} {value}", ct);
                string expected = protocol == 777 ? $"Game rule {rule} is currently set to {value}" : $"Gamerule {rule} is currently set to: {value}";
                Assert.Equal(expected, await server.CommandAsync($"gamerule {rule}", ct));
            }
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await ClearCells();
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} {self.Y:R} {self.Z:R} 0 0"), ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.OnGround && PillarNear(c.State.Self.Position, self), "chain grounded physics", ct);
            Assert.Contains(capture.Frames, frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == (x >> 4) && chunk.ChunkZ == (z >> 4));
            string item = protocol < 773 ? "chain" : "copper_chain";
            var scenes = new List<ChainLiveScene>();
            foreach (Direction face in new[] { Direction.Down, Direction.Up, Direction.North, Direction.South, Direction.West, Direction.East })
                scenes.Add(new("dry-" + face, item, face));
            scenes.Add(new("source-water", item, Direction.Up, Water: true));
            Direction bodyFace = protocol == 735 ? Direction.Up : Direction.East;
            scenes.Add(new("rod-center", item, bodyFace, Body: "center"));
            scenes.Add(new("unit-cell-corner", item, bodyFace, Body: "corner"));
            scenes.Add(new("adventure-clear", item, Direction.Up, Adventure: true));
            scenes.Add(new("adventure-occupied", item, Direction.Up, Adventure: true, Occupied: true));
            if (protocol >= 773)
                scenes.Add(new("renamed-iron-chain", "iron_chain", Direction.Up));
            foreach (ChainLiveScene scene in scenes)
                await Place(scene);
            Assert.Equal(protocol < 773 ? 11 : 12, observations.Count);
            await RemoveActor();
            Assert.Equal("Test passed", await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct));
            Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.ToArray(), ct));
            RegistryAccess registries = client.State.Registries!;
            int actionsReplay = capture.AssertRoundTrips(registries);
            int inventoryReplay = ReplayAdventureFrames(capture.Frames, [], version, registries);
            int contextReplay = AssertMotionRoundTrips(version, capture.Frames, registries);
            int supplementalReplay = ReplaySlabSupplement(capture.Frames, markers, version, registries);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => QuartzNativeDisconnected(server.ServerLog, protocol), "chain exact native peer EOF", ct);
            Assert.Equal(ClientStatus.Disconnected, client.Status);
            Assert.False(capture.Overflowed);
            await server.WriteAsync("chain-acceptance.json", new { release, protocol, observations, markers, actionsReplay, inventoryReplay, contextReplay, supplementalReplay, physics = true, explicitCoordinateCaller = true, peerObservedDisconnect = true, capture.PayloadBytes, recordingCap = 20 * 1024 * 1024, postActionTailSeconds = 2.1, protocol777LocalSwing = "Observed owner Local intent and one actual USE/no animation packet; no graphical arm claim.", originalGraphicalClientExecuted = false, localBlockOrStackMutationClaim = false });
            passed = true;

            async Task ClearCells()
                => await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);

            async Task Marker(string condition, string label)
            {
                string marker = "chain-" + label + "-" + Guid.NewGuid().ToString("N");
                markers.Add(marker);
                await server.ConsoleAsync(condition + " run tellraw " + Username + " {\"text\":\"" + marker + "\"}", ct);
                await WaitAsync(() => capture.Frames.Any(frame => SlabMarkerMatches(frame.Packet, marker)), "native chain causal marker " + label, ct);
                await client.InvokeAsync(_ => true, ct);
            }

            async Task NativeEntity(string condition)
                => Assert.Equal(protocol < 773 ? "Test passed, count: 1" : "Test passed. Count: 1", await server.CommandAsync(condition, ct));

            async Task NativeCount(string name, int count)
                => Assert.Equal(protocol < 773 ? $"Found {count} matching items on player {Username}" : $"Found {count} matching item(s) on player {Username}", await server.CommandAsync($"clear {Username} minecraft:{name} 0", ct));

            async Task RemoveActor()
            {
                if (actorId is not int id)
                    return;
                int mark = capture.Mark();
                await server.ConsoleAsync($"kill @e[tag={actorTag}]", ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(id)), "chain actual native villager removal frame", ct);
                await WaitAsync(client, c => c.State.Entities.Get(id) is null, "chain native removal applied", ct);
                actorId = null;
            }

            async Task SpawnBody(string scene)
            {
                Vec3d position = new(destination.X + (scene == "corner" ? .95 : .5), destination.Y, destination.Z + (scene == "corner" ? .95 : .5));
                int mark = capture.Mark();
                await server.ConsoleAsync(FormattableString.Invariant($"summon minecraft:villager {position.X:R} {position.Y:R} {position.Z:R} {{Tags:[\"{actorTag}\"],Age:-1000000,NoAI:1b,NoGravity:1b,Silent:1b,PersistenceRequired:1b}}"), ct);
                await WaitAsync(client, c => c.State.Entities.All.Count() == 1 && c.State.Entities.All.Single().Type.Id == Identifier.Minecraft("villager"), "chain native villager spawn", ct);
                Entity actor = await client.InvokeAsync(c => Assert.Single(c.State.Entities.All), ct);
                actorId = actor.Id;
                Assert.True(JavaGameData.EntityGeometryData(protocol).TryGetRule(actor.Type.Id, out EntityGeometryRule rule));
                await WaitAsync(() => capture.Since(mark).Any(frame => SlabMetadata(frame.Packet, actor.Id, rule.MetadataIndex, true)), "chain original child metadata", ct);
                await server.ConsoleAsync($"data merge entity @e[tag={actorTag},limit=1] {{Age:0}}", ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => SlabMetadata(frame.Packet, actor.Id, rule.MetadataIndex, false)), "chain actual adult metadata transition", ct);
                await server.ConsoleAsync(FormattableString.Invariant($"tp @e[tag={actorTag},limit=1] {position.X:R} {position.Y:R} {position.Z:R}"), ct);
                await WaitAsync(client, c => c.State.Entities.Get(actor.Id)?.Position == position, "chain received body position", ct);
                await NativeEntity(FormattableString.Invariant($"execute if entity @e[type=minecraft:villager,tag={actorTag},nbt={{Pos:[{position.X:R}d,{position.Y:R}d,{position.Z:R}d],Age:0,NoAI:1b,NoGravity:1b}}]"));
                Assert.Equal("Test passed", await server.CommandAsync($"execute unless entity @e[type=!minecraft:player,tag=!{actorTag}]", ct));
                var body = await client.InvokeAsync(c => ReadSlabBody(c, actor.Id, protocol), ct);
                Assert.True(body.Member && body.Known && body.BlocksBuilding);
                Assert.True(body.Box.Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)));
                Axis axis = protocol == 735 ? Axis.Y : Axis.X;
                Aabb rod = ChainRod(destination, axis);
                Assert.Equal(scene == "center", body.Box.Intersects(rod));
                await server.WriteAsync("chain-body-" + scene + ".json", new { actor.Id, position, body, unitCellIntersection = true, rod, rodIntersection = scene == "center" });
            }

            async Task Place(ChainLiveScene scene)
            {
                await RemoveActor();
                await ClearCells();
                await server.ConsoleAsync("kill @e[type=!minecraft:player]", ct);
                await server.ConsoleAsync("kill @e[type=minecraft:item]", ct);
                try
                {
                    await WaitAsync(client, c => !c.State.Entities.All.Any(), "chain received native ambient removals", ct);
                }
                finally
                {
                    await server.WriteAsync("chain-" + scene.Name + "-precondition.json", await client.InvokeAsync(c => VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, destination.Offset(scene.Face.Opposite()), destination), ct));
                }
                Assert.Equal("Test passed", await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct));
                BlockPos clicked = destination.Offset(scene.Face.Opposite());
                await server.ConsoleAsync($"setblock {clicked.X} {clicked.Y} {clicked.Z} minecraft:stone", ct);
                if (scene.Water)
                {
                    foreach (Direction side in new[] { Direction.North, Direction.South, Direction.East, Direction.West })
                    {
                        BlockPos wall = destination.Offset(side);
                        await server.ConsoleAsync($"setblock {wall.X} {wall.Y} {wall.Z} minecraft:stone", ct);
                    }
                    await server.ConsoleAsync($"setblock {destination.X} {destination.Y} {destination.Z} minecraft:water[level=0]", ct);
                }
                if (scene.Occupied)
                    await server.ConsoleAsync($"setblock {destination.X} {destination.Y} {destination.Z} minecraft:stone", ct);
                string initialState = scene.Occupied ? "stone" : scene.Water ? "water[level=0]" : "air";
                await WaitAsync(client, c => c.State.World.GetBlock(clicked).Block.Id == Identifier.Minecraft("stone") && ChainInitial(c.State.World.GetBlock(destination), scene), "chain authoritative prepared cells", ct);
                await AssertBlockAsync(server, protocol, destination, initialState, ct);
                GameMode previousMode = await client.InvokeAsync(c => c.State.Self.GameMode, ct);
                int modeMark = capture.Mark();
                await server.ConsoleAsync($"gamemode {(scene.Adventure ? "adventure" : "survival")} {Username}", ct);
                await server.ConsoleAsync($"clear {Username}", ct);
                await server.ConsoleAsync($"give {Username} minecraft:{scene.Item} 32", ct);
                await WaitAsync(client, c => c.State.Self.GameMode == (scene.Adventure ? GameMode.Adventure : GameMode.Survival) && !c.State.Self.InstantBuild && c.State.Self.OnGround && PillarNear(c.State.Self.Position, self)
                    && c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft(scene.Item) && c.State.Inventory.GetSlot(36).Count == 32 && c.State.Inventory.GetSlot(36).Components.HasNoPatch, "chain native default main-hand stack and grounded mode", ct);
                await NativeCount(scene.Item, 32);
                await NativeEntity(FormattableString.Invariant($"execute if entity @a[name={Username},gamemode={(scene.Adventure ? "adventure" : "survival")},nbt={{Pos:[{self.X:R}d,{self.Y:R}d,{self.Z:R}d],OnGround:1b,SelectedItem:{{id:\"minecraft:{scene.Item}\"}}}}]"));
                await Marker($"execute if entity @a[name={Username},gamemode={(scene.Adventure ? "adventure" : "survival")}]", scene.Name + "-mode");
                CapturedFrame[] modeFrames = capture.Frames;
                int abilitiesIndex = Array.FindLastIndex(modeFrames, frame => frame.Packet is ClientboundPlayerAbilitiesPacket);
                Assert.True(abilitiesIndex >= 0);
                Assert.Equal(0, Assert.IsType<ClientboundPlayerAbilitiesPacket>(modeFrames[abilitiesIndex].Packet).Flags & 8);
                int modeIndex = Array.FindLastIndex(modeFrames, frame => frame.Packet is ClientboundGameEventPacket { Event: 3 } change && change.Param == (float)(scene.Adventure ? GameMode.Adventure : GameMode.Survival));
                if (previousMode != (scene.Adventure ? GameMode.Adventure : GameMode.Survival))
                    Assert.True(modeIndex >= modeMark && abilitiesIndex >= modeMark, "A native mode transition must provide both actual gating packets before its ordered marker.");
                if (scene.Body is not null)
                    await SpawnBody(scene.Body);
                Vec3d cursor = ChainCursor(scene.Face);
                Vec3d hit = new(clicked.X + cursor.X, clicked.Y + cursor.Y, clicked.Z + cursor.Z);
                await client.Actions.Movement.LookAtAsync(hit, ct);
                Assert.True((await client.Snapshots.SelfAsync(ct)).EyePosition.Subtract(hit).LengthSqr() < 4.5 * 4.5);
                Assert.False((await client.InvokeAsync(ReadSlabPhysics, ct)).Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)));
                var inputs = await client.InvokeAsync(c => new { before = VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, clicked, destination), prediction = ReadSlabPrediction(c, clicked, scene.Face, cursor), after = VanillaInteractionAcceptanceDiagnostics.Read(c, protocol, clicked, destination) }, ct);
                await server.WriteAsync("chain-" + scene.Name + "-inputs.json", inputs);
                bool success = !scene.Adventure && scene.Body != "center";
                string expectedResult = scene.Adventure ? "Pass" : success ? "Success" : "Fail";
                string expectedSwing = success ? protocol == 777 ? "Local" : "Packet" : "None";
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(clicked, scene.Face, cursor, Hand.Main, insideBlock: false, ct: ct);
                string axis = protocol == 735 ? "y" : scene.Face.GetAxis().ToString().ToLowerInvariant();
                int count = success ? 31 : 32;
                string resultState = success ? scene.Item + (protocol == 735 ? "[waterlogged=" : "[axis=" + axis + ",waterlogged=") + scene.Water.ToString().ToLowerInvariant() + "]" : initialState;
                await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Count == count && (success ? ChainState(c.State.World.GetBlock(destination), protocol, scene.Item, axis, scene.Water) : ChainInitial(c.State.World.GetBlock(destination), scene)), "chain independent authoritative block and stack result", ct);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                await AssertBlockAsync(server, protocol, destination, resultState, ct);
                await NativeCount(scene.Item, count);
                if (scene.Body is not null)
                    await NativeEntity($"execute if entity @e[type=minecraft:villager,tag={actorTag},nbt={{Age:0,NoAI:1b,NoGravity:1b}}]");
                else
                    Assert.Equal("Test passed", await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct));
                CapturedFrame[] segment = capture.Since(mark);
                CapturedFrame[] actions = segment.Where(IsUseOrAnimation).ToArray();
                int expectedPackets = success && protocol != 777 ? 2 : 1;
                observations.Add(new { scene.Name, scene.Item, scene.Face, scene.Water, scene.Adventure, scene.Occupied, scene.Body, clicked, destination, cursor, axis, resultState, nativeCount = count, previousMode, modeMark, modeIndex, abilitiesIndex, inputs.prediction, expectedResult, expectedSwing, expectedPackets, observedPackets = actions.Length, actions = actions.Select(frame => new { frame.Flow, frame.WireId, packet = frame.Packet!.GetType().Name, frame.Payload }).ToArray() });
                await server.WriteAsync("chain-scenes.json", observations);
                Assert.True(inputs.prediction.SendPacket);
                Assert.Equal(expectedResult, inputs.prediction.Result);
                Assert.Equal(expectedSwing, inputs.prediction.Swing);
                Assert.Equal(expectedPackets, actions.Length);
                var use = Assert.IsType<ServerboundUseItemOnPacket>(actions[0].Packet);
                Assert.Equal((clicked, (int)scene.Face, 0), (use.Position, use.Face, use.Hand));
                Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
                Assert.False(use.Inside || use.WorldBorderHit);
                if (success && protocol != 777)
                    Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                if (protocol >= 759)
                    Assert.Single(segment, frame => frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == use.Sequence);
                Assert.DoesNotContain(actions, frame => frame.Packet is ServerboundPunchPacket);
                Assert.True(segment[^1].Milliseconds - actions[0].Milliseconds >= 900);
                Assert.False(capture.Overflowed);
                await Marker($"execute if block {destination.X} {destination.Y} {destination.Z} minecraft:{resultState}", scene.Name + "-native-state");
            }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "chain native geometry and action results", completedCases = observations.Count, capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static Vec3d ChainCursor(Direction face) => face switch
    {
        Direction.Down => new(.5, 0, .5),
        Direction.Up => new(.5, 1, .5),
        Direction.North => new(.5, .5, 0),
        Direction.South => new(.5, .5, 1),
        Direction.West => new(0, .5, .5),
        _ => new(1, .5, .5),
    };

    private static Aabb ChainRod(BlockPos cell, Axis axis) => axis switch
    {
        Axis.X => new(cell.X, cell.Y + .40625, cell.Z + .40625, cell.X + 1, cell.Y + .59375, cell.Z + .59375),
        Axis.Y => new(cell.X + .40625, cell.Y, cell.Z + .40625, cell.X + .59375, cell.Y + 1, cell.Z + .59375),
        _ => new(cell.X + .40625, cell.Y + .40625, cell.Z, cell.X + .59375, cell.Y + .59375, cell.Z + 1),
    };

    private static bool ChainInitial(BlockState state, ChainLiveScene scene)
        => state.IsValid && (scene.Occupied ? state.Block.Id == Identifier.Minecraft("stone") : scene.Water ? state.Block.Id == Identifier.Minecraft("water") && state.TryGetProperty("level", out string level) && level == "0" : state.IsAir);

    private static bool ChainState(BlockState state, int protocol, string item, string axis, bool wet)
        => state.IsValid && state.Block.Id == Identifier.Minecraft(item) && state.TryGetProperty("waterlogged", out string water) && water == wet.ToString().ToLowerInvariant()
            && (protocol == 735 ? state.Block.Value.Properties.All(property => property.Name != "axis") : state.TryGetProperty("axis", out string actual) && actual == axis);

    private sealed record ChainLiveScene(string Name, string Item, Direction Face, bool Water = false, string? Body = null, bool Adventure = false, bool Occupied = false);
}
