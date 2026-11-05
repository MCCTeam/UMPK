using System.Buffers;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    public static IEnumerable<object[]> QuartzReleases()
    {
        (string Release, int Protocol)[] rows = [("1.8.9", 47), ("1.12.2", 340)];
        string? selected = Environment.GetEnvironmentVariable("UMPK_QUARTZ_RELEASE");
        Assert.True(selected is null || rows.Any(row => row.Release == selected), "UMPK_QUARTZ_RELEASE must name a bound quartz release.");
        foreach ((string release, int protocol) in rows)
            if (selected is null || selected == release)
                yield return [release, protocol];
    }

    [NightlyTheory]
    [MemberData(nameof(QuartzReleases))]
    public async Task LegacyQuartzPlacementMatchesNativeVariantsAndAdventureOrder(string release, int protocol)
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
        bool passed = false;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "quartz native terrain", ct);
            Assert.True(client.State.Features.Physics && client.State.Features.Terrain && client.State.Features.Entities);
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(spawn.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(spawn.Z) >> 4) * 16 + 8;
            Vec3d self = new(x + .5, 100, z + .5);
            var destination = new BlockPos(x + 2, 101, z);
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync("gamerule doMobSpawning false", ct);
            await server.ConsoleAsync("gamerule doMobLoot false", ct);
            await server.ConsoleAsync("gamerule doTileDrops false", ct);
            await server.ConsoleAsync("gamerule doEntityDrops false", ct);
            Assert.Equal("doEntityDrops = false", await server.CommandAsync("gamerule doEntityDrops", ct));
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} {self.Y:R} {self.Z:R} 0 0"), ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.OnGround && PillarNear(c.State.Self.Position, self), "quartz grounded physics", ct);
            Assert.Contains(capture.Frames, frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == (x >> 4) && chunk.ChunkZ == (z >> 4)
                || frame.Packet is ClientboundMapChunkBulkPacket bulk && bulk.Columns.Any(column => column.Position.X == (x >> 4) && column.Position.Z == (z >> 4)));
            var scenes = new List<QuartzLiveScene>();
            foreach (Direction face in new[] { Direction.Down, Direction.Up, Direction.North, Direction.South, Direction.West, Direction.East })
                scenes.Add(new("pillar-" + face, 2, face, GameMode.Survival, false, face is Direction.Down or Direction.Up ? 2 : face is Direction.North or Direction.South ? 4 : 3));
            foreach (int metadata in new[] { 0, 1, 3, 4 })
                scenes.Add(new("variant-" + metadata, metadata, Direction.Up, GameMode.Survival, false, metadata == 1 ? 1 : 0));
            scenes.Add(new("adventure-clear", 2, Direction.Up, GameMode.Adventure, false, 0));
            scenes.Add(new("adventure-blocked", 2, Direction.Up, GameMode.Adventure, true, 0));
            Assert.Equal(12, scenes.Count);
            foreach (QuartzLiveScene scene in scenes)
                await Place(scene);
            int actionReplay = capture.AssertRoundTrips(client.State.Registries!);
            int inventoryReplay = ReplayAdventureFrames(capture.Frames, [], version, client.State.Registries!);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => QuartzNativeDisconnected(server.ServerLog, protocol), "quartz native peer EOF", ct);
            Assert.Equal(ClientStatus.Disconnected, client.Status);
            await server.WriteAsync("quartz-acceptance.json", new { release, protocol, observations, physics = true, explicitCoordinateCaller = true, actionReplay, inventoryReplay, peerObservedDisconnect = true, capture.PayloadBytes, recordingCap = 20 * 1024 * 1024, postActionTailSeconds = 2.1, protocol47Profile = protocol == 47 ? "1.8.9" : null, originalGraphicalClientExecuted = false, localWorldOrStackMutationClaim = false });
            passed = true;

            async Task AssertNativeCount(int metadata, int expected)
            {
                string response = await server.CommandAsync($"clear {Username} minecraft:quartz_block {metadata} 0", ct);
                Assert.Equal($"{Username} has {expected} items that match the criteria", response);
            }

            async Task Place(QuartzLiveScene scene)
            {
                await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);
                BlockPos clicked = destination.Offset(scene.Face.Opposite());
                await server.ConsoleAsync($"setblock {clicked.X} {clicked.Y} {clicked.Z} minecraft:stone", ct);
                if (scene.Blocked)
                    await server.ConsoleAsync($"setblock {destination.X} {destination.Y} {destination.Z} minecraft:stone", ct);
                await server.ConsoleAsync($"gamemode {(scene.Mode == GameMode.Survival ? "survival" : "adventure")} {Username}", ct);
                string kill = protocol == 47 ? "kill @e[type=!Player]" : "kill @e[type=!minecraft:player]";
                string killItems = protocol == 47 ? "kill @e[type=Item]" : "kill @e[type=minecraft:item]";
                int removalMark = capture.Mark();
                object[]? lastEntities = null;
                await server.ConsoleAsync(kill, ct);
                await server.ConsoleAsync(killItems, ct);
                try
                {
                    await WaitAsync(client, c =>
                    {
                        lastEntities = SnapshotPillarEntities(c);
                        return lastEntities.Length == 0;
                    }, "quartz received native removals", ct);
                    Assert.Empty(await client.InvokeAsync(SnapshotPillarEntities, ct));
                }
                finally
                {
                    await server.WriteAsync("quartz-" + scene.Name + "-clearance.json", new
                    {
                        kill,
                        killItems,
                        lastEntities,
                        removalFrames = capture.Since(removalMark).Where(frame => frame.Packet is ClientboundRemoveEntitiesPacket or ClientboundEntityEventPacket)
                            .Select(frame => new { frame.Milliseconds, frame.WireId, packet = frame.Packet!.GetType().Name, frame.Payload }).ToArray(),
                        spawnFrames = capture.Since(removalMark).Where(frame => frame.Packet is ClientboundAddEntityPacket or ClientboundAddMobPacket)
                            .Select(frame => new { frame.Milliseconds, frame.WireId, packet = frame.Packet!.GetType().Name, frame.Payload }).ToArray(),
                    });
                }
                await server.ConsoleAsync($"clear {Username}", ct);
                await server.ConsoleAsync($"give {Username} minecraft:quartz_block 32 {scene.Metadata}", ct);
                await WaitAsync(client, c => c.State.Self.GameMode == scene.Mode && !c.State.Self.InstantBuild && c.State.Self.OnGround && PillarNear(c.State.Self.Position, self)
                    && c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("quartz_block") && c.State.Inventory.GetSlot(36).Count == 32
                    && c.State.Inventory.GetSlot(36).Damage == scene.Metadata && c.State.Inventory.GetSlot(36).Components.Get(DataComponents.LegacyNbt) is null
                    && c.State.World.GetBlock(clicked).Block.Id == Identifier.Minecraft("stone")
                    && (scene.Blocked ? c.State.World.GetBlock(destination).Block.Id == Identifier.Minecraft("stone") : c.State.World.GetBlock(destination).IsAir), "quartz native mode, plain stack and terrain", ct);
                Assert.Equal("Found " + Username, await server.CommandAsync($"testfor @a[name={Username},m={(scene.Mode == GameMode.Survival ? 0 : 2)}] {{OnGround:1b}}", ct));
                await AssertNativeCount(scene.Metadata, 32);
                await AssertBlockAsync(server, protocol, destination, scene.Blocked ? "stone" : "air", ct, 0);
                Vec3d cursor = scene.Face switch
                {
                    Direction.Down => new(.5, 0, .5),
                    Direction.Up => new(.5, 1, .5),
                    Direction.North => new(.5, .5, 0),
                    Direction.South => new(.5, .5, 1),
                    Direction.West => new(0, .5, .5),
                    _ => new(1, .5, .5),
                };
                Vec3d hit = new(clicked.X + cursor.X, clicked.Y + cursor.Y, clicked.Z + cursor.Z);
                await client.Actions.Movement.LookAtAsync(hit, ct);
                Assert.True((await client.Snapshots.SelfAsync(ct)).EyePosition.Subtract(hit).LengthSqr() < 4.5 * 4.5);
                Assert.False((await client.InvokeAsync(ReadSlabPhysics, ct)).Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)));
                var inputs = await client.InvokeAsync(c => new { before = ReadPillarOwnedInputs(c, protocol, clicked, destination), prediction = ReadSlabPrediction(c, clicked, scene.Face, cursor), after = ReadPillarOwnedInputs(c, protocol, clicked, destination) }, ct);
                await server.WriteAsync("quartz-" + scene.Name + "-inputs.json", inputs);
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(clicked, scene.Face, cursor, Hand.Main, insideBlock: false, ct: ct);
                bool success = scene.Mode == GameMode.Survival;
                int expectedCount = success ? 31 : 32;
                await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Count == expectedCount && c.State.Inventory.GetSlot(36).Damage == scene.Metadata
                    && (success ? c.State.World.GetBlock(destination).Block.Id == Identifier.Minecraft("quartz_block") && c.State.World.GetBlock(destination).LegacyMeta == scene.PlacedMetadata
                        : scene.Blocked ? c.State.World.GetBlock(destination).Block.Id == Identifier.Minecraft("stone") : c.State.World.GetBlock(destination).IsAir), "quartz native block and stack effects", ct);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                await AssertBlockAsync(server, protocol, destination, success ? "quartz_block" : scene.Blocked ? "stone" : "air", ct, success ? scene.PlacedMetadata : 0);
                await AssertNativeCount(scene.Metadata, expectedCount);
                CapturedFrame[] segment = capture.Since(mark);
                CapturedFrame[] actions = segment.Where(IsUseOrAnimation).ToArray();
                int expectedPackets = scene.Blocked ? 0 : success ? 2 : 1;
                int replayedActions = ReplayQuartzActions(actions, version, client.State.Registries!);
                var observation = new { scene.Name, scene.Metadata, scene.Face, scene.Mode, scene.Blocked, scene.PlacedMetadata, clicked, destination, cursor, localPrediction = inputs.prediction, nativeCount = expectedCount, nativeResultMeaning = success ? "native block and stack changed" : scene.Blocked ? "native world precheck rejects before USE" : protocol == 47 ? "source-backed controller Boolean false after USE; not an enum" : "source-backed controller FAIL after USE", expectedPackets, observedPackets = actions.Length, replayedActions, actions = actions.Select(frame => new { frame.Flow, frame.WireId, packet = frame.Packet!.GetType().Name, frame.Payload }).ToArray() };
                observations.Add(observation);
                await server.WriteAsync("quartz-scenes.json", observations);
                // Persist native effects and actual frames before this assertion so an old-producer missing swing is a reviewable failed observation.
                Assert.Equal(expectedPackets, actions.Length);
                Assert.Equal(!scene.Blocked, inputs.prediction.SendPacket);
                Assert.Equal(success ? "Success" : "Fail", inputs.prediction.Result);
                Assert.Equal(success ? "Packet" : "None", inputs.prediction.Swing);
                if (!scene.Blocked)
                {
                    if (protocol == 47)
                    {
                        var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(actions[0].Packet);
                        Assert.Equal((clicked, (int)scene.Face), (use.Position, use.Face));
                        Assert.Equal((Identifier.Minecraft("quartz_block"), 32, scene.Metadata), (use.HeldItem.Item.Id, use.HeldItem.Count, use.HeldItem.Damage));
                        Assert.Equal(((byte)(cursor.X * 16), (byte)(cursor.Y * 16), (byte)(cursor.Z * 16)), (use.CursorX, use.CursorY, use.CursorZ));
                    }
                    else
                    {
                        var use = Assert.IsType<ServerboundUseItemOnPacket>(actions[0].Packet);
                        Assert.Equal((clicked, (int)scene.Face, 0), (use.Position, use.Face, use.Hand));
                        Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
                        Assert.False(use.Inside || use.WorldBorderHit);
                    }
                    if (success)
                        Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                    Assert.True(segment[^1].Milliseconds - actions[0].Milliseconds >= 900);
                }
                else
                    Assert.True(segment.Length >= 2 && segment[^1].Milliseconds - segment[0].Milliseconds >= 900);
                Assert.False(capture.Overflowed);
            }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "legacy quartz variants and Adventure order", completedCases = observations.Count, capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static int ReplayQuartzActions(CapturedFrame[] frames, JavaVersion version, Umpk.Game.Registries.RegistryAccess registries)
    {
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        foreach (CapturedFrame frame in frames)
        {
            Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, frame.Flow).TryGetInbound(frame.WireId, out BoundPacketCodec codec));
            object decoded = codec.Decode(frame.Payload, context);
            object? expected = frame.Packet;
            if (expected is ServerboundSwingPacket swing)
                expected = swing with { HasHand = version.Version.Protocol >= 107 };
            if (expected is ServerboundUseItemOnPacket use)
                expected = use with { Inside = false, WorldBorderHit = false, Sequence = 0 };
            Assert.Equal(expected, decoded);
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(frame.Payload, buffer.WrittenSpan.ToArray());
        }
        return frames.Length;
    }

    private static bool QuartzNativeDisconnected(string log, int protocol)
    {
        string marker = Username + " lost connection: ";
        return log.Split('\n').Any(line =>
        {
            int index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
                return false;
            string reason = line[(index + marker.Length)..].TrimEnd('\r');
            return protocol == 47
                ? reason.StartsWith("TextComponent{text='Disconnected', siblings=[], style=", StringComparison.Ordinal) && reason.EndsWith('}')
                : reason is "Disconnected" or "End of stream";
        });
    }

    private sealed record QuartzLiveScene(string Name, int Metadata, Direction Face, GameMode Mode, bool Blocked, int PlacedMetadata);
}
