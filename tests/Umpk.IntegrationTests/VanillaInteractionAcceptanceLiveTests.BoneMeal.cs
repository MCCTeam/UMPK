using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    [NightlyFact]
    public Task BoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth766() => RunBoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth("1.20.5", 766);

    [NightlyFact]
    public Task BoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth769() => RunBoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth("1.21.4", 769);

    [NightlyFact]
    public Task BoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth774() => RunBoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth("1.21.11", 774);

    [NightlyFact]
    public Task BoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth775() => RunBoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth("26.1", 775);

    [NightlyFact]
    public Task BoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth776() => RunBoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth("26.2", 776);

    [NightlyFact]
    public Task BoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth777() => RunBoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth("26.3", 777);

    private async Task RunBoneMealWheatMatchesNativeHandsModesAgesAndServerOwnedGrowth(string release, int protocol)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        Assert.Equal(protocol, version.Version.Protocol);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        const string flatWorldSettings = """{"layers":[{"height":164,"block":"minecraft:stone"}],"biome":"minecraft:the_void","features":false,"lakes":false,"structure_overrides":[]}""";
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct, flatWorldSettings: flatWorldSettings);
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version, includeBiomeRegistry: true);
        client.PacketFrameObserved += capture.Observe;
        var observations = new List<object>();
        var markers = new List<string>();
        bool passed = false;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "bone-meal native terrain", ct);
            Assert.True(client.State.Features.Physics && client.State.Features.Terrain && client.State.Features.Inventory && client.State.Features.Entities);
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(spawn.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(spawn.Z) >> 4) * 16 + 8;
            var target = new BlockPos(x + 2, 100, z + 1);
            Vec3d self = new(x + .5, 100, z + .5);
            string[] lines = await File.ReadAllLinesAsync(Path.Combine(server.DirectoryPath, "server.properties"), ct);
            var properties = lines.Where(line => line.StartsWith("level-type=", StringComparison.Ordinal) || line.StartsWith("generator-settings=", StringComparison.Ordinal))
                .ToDictionary(line => line[..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..].Replace("\\:", ":", StringComparison.Ordinal).Replace("\\=", "=", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal));
            Assert.Equal("flat", properties["level-type"]);
            using (JsonDocument expected = JsonDocument.Parse(flatWorldSettings))
            using (JsonDocument actual = JsonDocument.Parse(properties["generator-settings"]))
                Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
            Assert.Equal(-64, await client.InvokeAsync(c => c.State.World.Dimension.MinY, ct));
            ModernFlatBiomeWitness biomes = AssertReceivedModernFlatNativeBiomes(capture, version, client.State.Registries!, protocol, x >> 4, z >> 4);
            Assert.Equal("Test passed", await server.CommandAsync($"execute if biome {x} 100 {z} minecraft:the_void", ct));
            await AssertBlockAsync(server, protocol, new BlockPos(x, -64, z), "stone", ct);
            await AssertBlockAsync(server, protocol, new BlockPos(x, 99, z), "stone", ct);
            await AssertBlockAsync(server, protocol, new BlockPos(x, 100, z), "air", ct);
            await AssertEmptyWorld();
            await server.ConsoleAsync("difficulty peaceful", ct);
            foreach ((string oldName, string newName) in new[] { ("doMobSpawning", "spawn_mobs"), ("doMobLoot", "mob_drops"), ("doTileDrops", "block_drops"), ("doEntityDrops", "entity_drops") })
            {
                string rule = protocol >= 774 ? newName : oldName;
                await server.ConsoleAsync($"gamerule {rule} false", ct);
                string expected = protocol == 777 ? $"Game rule {rule} is currently set to false" : $"Gamerule {rule} is currently set to: false";
                Assert.Equal(expected, await server.CommandAsync($"gamerule {rule}", ct));
            }
            string randomTicks = protocol >= 774 ? "random_tick_speed" : "randomTickSpeed";
            await server.ConsoleAsync($"gamerule {randomTicks} 0", ct);
            string randomTicksExpected = protocol == 777 ? $"Game rule {randomTicks} is currently set to 0" : $"Gamerule {randomTicks} is currently set to: 0";
            Assert.Equal(randomTicksExpected, await server.CommandAsync($"gamerule {randomTicks}", ct));
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} {self.Y:R} {self.Z:R} 0 0"), ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.OnGround && PillarNear(c.State.Self.Position, self), "bone-meal grounded physics", ct);
            const string relativeSource = "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.BoneMeal.cs";
            string sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(FixturePaths.RepoRoot(), relativeSource), ct)));
            await server.WriteAsync("bone-meal-generator-preconditions.json", new { release, protocol, properties, biomes, minY = -64, floorY = 99, cropY = 100, randomTicks = 0, receivedFullColumn = true, initialNativeAndClientEntitiesEmpty = true, gameplayBiomeProjectionKnown = false, sourceSha256 });

            foreach (Hand hand in new[] { Hand.Main, Hand.Off })
                foreach (GameMode mode in new[] { GameMode.Survival, GameMode.Creative })
                    foreach (int age in new[] { 0, 7 })
                        foreach (Direction face in new[] { Direction.Up, Direction.East })
                            await Use(new BoneMealLiveScene(hand + "-" + mode + "-age" + age + "-" + face, hand, mode, age, face));
            Assert.Equal(16, observations.Count);
            await AssertEmptyWorld();
            int actionsReplay = capture.AssertRoundTrips(client.State.Registries!);
            int inventoryReplay = ReplayAdventureFrames(capture.Frames, [], version, client.State.Registries!);
            int contextReplay = AssertMotionRoundTrips(version, capture.Frames.Where(frame => frame.Packet is not (ServerboundUseItemOnPacket or ClientboundDisguisedChatPacket)).ToArray(), client.State.Registries!);
            int supplementalReplay = ReplaySlabSupplement(capture.Frames, markers, version, client.State.Registries!);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => QuartzNativeDisconnected(server.ServerLog, protocol), "bone-meal exact native peer EOF", ct);
            Assert.Equal(ClientStatus.Disconnected, client.Status);
            Assert.False(capture.Overflowed);
            await server.WriteAsync("bone-meal-acceptance.json", new { release, protocol, observations, markers, actionsReplay, inventoryReplay, contextReplay, supplementalReplay, sourceSha256, peerObservedDisconnect = true, capture.PayloadBytes, recordingCap = 20 * 1024 * 1024, postActionTailSeconds = 2.1, originalGraphicalClientExecuted = false, predictionOnlyMutatesNothing = true, noClientGrowthShrinkClaim = "Pure previews are unchanged and the producer's framed before-write tests remain a prerequisite; live growth and count changes are server observations.", protocol775 = "Client PASS/no swing remains distinct from original server SUCCESS_SERVER and crop growth.", protocol777 = "Local swing intent with no animation packet; no graphical arm observation." });
            passed = true;

            async Task AssertEmptyWorld()
            {
                Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.ToArray(), ct));
                Assert.Equal("Test passed", await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct));
            }

            async Task NativeCount(int count) => Assert.Equal($"Found {count} matching item(s) on player {Username}", await server.CommandAsync($"clear {Username} minecraft:bone_meal 0", ct));

            async Task Marker(string condition, string label)
            {
                string marker = "bone-meal-" + label + "-" + Guid.NewGuid().ToString("N");
                markers.Add(marker);
                await server.ConsoleAsync(condition + " run tellraw " + Username + " {\"text\":\"" + marker + "\"}", ct);
                await WaitAsync(() => capture.Frames.Any(frame => SlabMarkerMatches(frame.Packet, marker)), "native bone-meal marker " + label, ct);
                await client.InvokeAsync(_ => true, ct);
            }

            async Task Use(BoneMealLiveScene scene)
            {
                await AssertEmptyWorld();
                await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:air", ct);
                await server.ConsoleAsync($"setblock {target.X} 99 {target.Z} minecraft:farmland[moisture=7]", ct);
                await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:wheat[age={scene.Age}]", ct);
                string nativeMode = scene.Mode == GameMode.Creative ? "creative" : "survival";
                await server.ConsoleAsync($"gamemode {nativeMode} {Username}", ct);
                await server.ConsoleAsync($"clear {Username}", ct);
                if (scene.Hand == Hand.Main)
                    await server.ConsoleAsync($"give {Username} minecraft:bone_meal 32", ct);
                else
                    await server.ConsoleAsync($"item replace entity {Username} weapon.offhand with minecraft:bone_meal 32", ct);
                int slot = scene.Hand == Hand.Main ? 36 : 45;
                int otherSlot = scene.Hand == Hand.Main ? 45 : 36;
                await WaitAsync(client, c => c.State.Self.GameMode == scene.Mode && c.State.Self.InstantBuild == (scene.Mode == GameMode.Creative) && !c.State.Self.Sneaking && c.State.Self.OnGround && PillarNear(c.State.Self.Position, self)
                    && c.State.World.GetColumn(target) is not null && BoneMealWheatAge(c.State.World.GetBlock(target)) == scene.Age
                    && c.State.World.GetBlock(target.Below()).Block.Id == Identifier.Minecraft("farmland")
                    && c.State.Inventory.GetSlot(slot).Item.Id == Identifier.Minecraft("bone_meal") && c.State.Inventory.GetSlot(slot).Count == 32 && c.State.Inventory.GetSlot(slot).Components.HasNoPatch && c.State.Inventory.GetSlot(otherSlot).IsEmpty, "bone-meal received terrain, mode, canonical held input and grounded player", ct);
                await AssertBlockAsync(server, protocol, target, $"wheat[age={scene.Age}]", ct);
                await AssertBlockAsync(server, protocol, target.Below(), "farmland[moisture=7]", ct);
                foreach (BlockPos air in new[] { target.Above(), target.Offset(Direction.East) })
                {
                    await AssertBlockAsync(server, protocol, air, "air", ct);
                    Assert.True(await client.InvokeAsync(c => c.State.World.GetBlock(air).IsAir, ct));
                }
                await NativeCount(32);
                string handPredicate = scene.Hand == Hand.Main ? "SelectedItem:{id:\"minecraft:bone_meal\"}" : protocol == 777 ? "equipment:{offhand:{id:\"minecraft:bone_meal\"}}" : "Inventory:[{Slot:-106b,id:\"minecraft:bone_meal\"}]";
                string countOne = protocol >= 774 ? "Test passed. Count: 1" : "Test passed, count: 1";
                Assert.Equal(countOne, await server.CommandAsync(FormattableString.Invariant($"execute if entity @a[name={Username},gamemode={nativeMode},nbt={{Pos:[{self.X:R}d,{self.Y:R}d,{self.Z:R}d],OnGround:1b,{handPredicate}}}]"), ct));
                await Marker($"execute if block {target.X} {target.Y} {target.Z} minecraft:wheat[age={scene.Age}]", scene.Name + "-input");
                Vec3d cursor = scene.Face == Direction.Up ? new(.5, 1, .5) : new(1, .5, .5);
                Vec3d hit = new(target.X + cursor.X, target.Y + cursor.Y, target.Z + cursor.Z);
                await client.Actions.Movement.LookAtAsync(hit, ct);
                Assert.True((await client.Snapshots.SelfAsync(ct)).EyePosition.Subtract(hit).LengthSqr() < 4.5 * 4.5);
                var inputs = await client.InvokeAsync(c => new { before = ReadBoneMealLiveState(c, protocol, target, scene.Hand), prediction = ReadLadderPrediction(c, target, scene.Face, cursor, scene.Hand), after = ReadBoneMealLiveState(c, protocol, target, scene.Hand) }, ct);
                await server.WriteAsync("bone-meal-" + scene.Name + "-inputs.json", inputs);
                Assert.Equal(inputs.before, inputs.after);
                Assert.Equal((scene.Age, 32, 0), (inputs.before.Age, inputs.before.Count, inputs.before.OtherCount));
                Assert.True(inputs.before.CanonicalItem && inputs.before.CanonicalWheat && inputs.before.NoPatch && inputs.before.NoCanPlaceOn && inputs.before.EmptyCollision);
                bool clientSuccess = scene.Age == 0 && protocol != 775;
                bool packetSwing = clientSuccess && protocol != 777;
                string expectedResult = clientSuccess ? "Success" : "Pass";
                string expectedSwing = packetSwing ? "Packet" : clientSuccess ? "Local" : "None";
                Assert.True(inputs.prediction.SendPacket);
                Assert.Equal(expectedResult, inputs.prediction.Result);
                Assert.Equal(expectedSwing, inputs.prediction.Swing);
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(target, scene.Face, cursor, scene.Hand, insideBlock: false, ct: ct);
                bool serverGrowth = scene.Age == 0;
                int nativeCount = serverGrowth && scene.Mode == GameMode.Survival ? 31 : 32;
                await WaitAsync(client, c => c.State.Inventory.GetSlot(slot).Count == nativeCount && (serverGrowth ? BoneMealWheatAge(c.State.World.GetBlock(target)) is >= 2 and <= 5 : BoneMealWheatAge(c.State.World.GetBlock(target)) == 7), "bone-meal authoritative crop and selected inventory result", ct);
                int finalAge = await client.InvokeAsync(c => BoneMealWheatAge(c.State.World.GetBlock(target)), ct);
                if (serverGrowth)
                    Assert.InRange(finalAge, 2, 5);
                else
                    Assert.Equal(7, finalAge);
                await AssertBlockAsync(server, protocol, target, $"wheat[age={finalAge}]", ct);
                await AssertBlockAsync(server, protocol, target.Below(), "farmland[moisture=7]", ct);
                await NativeCount(nativeCount);
                Assert.True(await client.InvokeAsync(c => c.State.Inventory.GetSlot(otherSlot).IsEmpty, ct));
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                CapturedFrame[] segment = capture.Since(mark);
                CapturedFrame[] actions = segment.Where(IsUseOrAnimation).ToArray();
                observations.Add(new { scene.Name, scene.Hand, scene.Mode, initialAge = scene.Age, finalAge, scene.Face, cursor, slot, inputs.prediction, expectedResult, expectedSwing, expectedPackets = packetSwing ? 2 : 1, observedPackets = actions.Length, nativeCount, serverGrowth, explicitCoordinateCaller = true, actions = actions.Select(frame => new { frame.Flow, frame.WireId, packet = frame.Packet!.GetType().Name, payload = Convert.ToHexStringLower(frame.Payload), frame.Milliseconds }).ToArray() });
                await server.WriteAsync("bone-meal-scenes.json", observations);
                await server.WriteAsync("capture.json", capture.Frames);
                Assert.Equal(packetSwing ? 2 : 1, actions.Length);
                var use = Assert.IsType<ServerboundUseItemOnPacket>(actions[0].Packet);
                Assert.Equal((target, (int)scene.Face, (int)scene.Hand), (use.Position, use.Face, use.Hand));
                Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
                Assert.False(use.Inside || use.WorldBorderHit);
                if (packetSwing)
                    Assert.Equal((int)scene.Hand, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                Assert.Single(segment, frame => frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == use.Sequence);
                if (serverGrowth)
                    Assert.Contains(segment, frame => frame.Flow == PacketFlow.Clientbound && frame.Packet is ClientboundBlockUpdatePacket update && update.Position == target && update.BlockStateId != inputs.before.StateId);
                Assert.True(segment[^1].Milliseconds - actions[0].Milliseconds >= 900);
                Assert.False(capture.Overflowed);
                await Marker($"execute if block {target.X} {target.Y} {target.Z} minecraft:wheat[age={finalAge}]", scene.Name + "-result");
                await AssertEmptyWorld();
            }
        }
        finally
        {
            capture.Stop();
            client.PacketFrameObserved -= capture.Observe;
            await server.WriteAsync("bone-meal-biome-registry-frames.json", capture.BiomeRegistryFrames);
            await server.WriteAsync("bone-meal-scenes.json", observations);
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "canonical wheat bone meal with independent server growth and inventory", completedCases = observations.Count, capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static int BoneMealWheatAge(BlockState state) => state.IsValid && state.Block.Id == Identifier.Minecraft("wheat") && state.TryGetProperty("age", out string age) && int.TryParse(age, NumberStyles.None, CultureInfo.InvariantCulture, out int result) ? result : -1;

    private static BoneMealLiveState ReadBoneMealLiveState(UmpkClient client, int protocol, BlockPos target, Hand hand)
    {
        BlockState crop = client.State.World.GetBlock(target);
        int slot = hand == Hand.Main ? 36 : 45;
        int otherSlot = hand == Hand.Main ? 45 : 36;
        var held = client.State.Inventory.GetSlot(slot);
        var canonical = JavaGameData.Registries(protocol);
        return new(crop.StateId, BoneMealWheatAge(crop), held.Count, client.State.Inventory.GetSlot(otherSlot).Count,
            !held.IsEmpty && ReferenceEquals(held.Item.Value, canonical.Items[Identifier.Minecraft("bone_meal")].Value),
            crop.IsValid && ReferenceEquals(crop.Block.Value, canonical.Blocks[Identifier.Minecraft("wheat")].Value),
            held.Components.HasNoPatch, held.Components.Get(DataComponents.CanPlaceOn) is null,
            JavaGameData.BlockShapes(protocol).GetCollisionShapes(crop).ToArray().Length == 0);
    }

    private sealed record BoneMealLiveState(int StateId, int Age, int Count, int OtherCount, bool CanonicalItem, bool CanonicalWheat, bool NoPatch, bool NoCanPlaceOn, bool EmptyCollision);

    private sealed record BoneMealLiveScene(string Name, Hand Hand, GameMode Mode, int Age, Direction Face);
}
