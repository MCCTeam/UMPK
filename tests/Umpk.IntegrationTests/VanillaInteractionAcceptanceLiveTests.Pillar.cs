using System.Reflection;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    public static IEnumerable<object[]> PillarReleases()
    {
        (string Release, int Protocol)[] rows = [("1.8.9", 47), ("1.13", 393), ("26.3", 777)];
        string? requested = Environment.GetEnvironmentVariable("UMPK_PILLAR_RELEASE");
        Assert.True(requested is null || rows.Any(row => row.Release == requested), "UMPK_PILLAR_RELEASE must name one of the three bound native releases.");
        foreach ((string release, int protocol) in rows)
            if (requested is null || requested == release)
                yield return [release, protocol];
    }

    [NightlyTheory]
    [MemberData(nameof(PillarReleases))]
    public async Task DefaultLogPlacementMatchesNativeAxesStacksAndPacketOrder(string release, int protocol)
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
        var scenes = new List<object>();
        bool passed = false;
        string item = protocol == 47 ? "log" : "oak_log";
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "native pillar terrain", ct);
            Assert.True(client.State.Features.Physics && client.State.Features.Terrain && client.State.Features.Entities);
            Vec3d spawn = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(spawn.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(spawn.Z) >> 4) * 16 + 8;
            Vec3d self = new(x + .5, 100, z + .5);
            var destination = new BlockPos(x + 2, 101, z);
            string mobLootRule = protocol < 774 ? "doMobLoot" : "minecraft:mob_drops";
            await server.ConsoleAsync($"gamerule {mobLootRule} false", ct);
            string entityDropsRule = protocol == 777 ? "minecraft:entity_drops" : "doEntityDrops";
            await server.ConsoleAsync($"gamerule {entityDropsRule} false", ct);
            string entityDropsQuery = await server.CommandAsync($"gamerule {entityDropsRule}", ct);
            Assert.Equal(protocol switch
            {
                47 => "doEntityDrops = false",
                393 => "Gamerule doEntityDrops is currently set to: false",
                777 => "Game rule entity_drops is currently set to false",
                _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
            }, entityDropsQuery);
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobSpawning false" : "gamerule minecraft:spawn_mobs false", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doTileDrops false" : "gamerule minecraft:block_drops false", ct);
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await ClearCells();
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} {self.Y:R} {self.Z:R} 0 0"), ct);
            var entitiesBeforeCleanup = await client.InvokeAsync(c => c.State.Entities.All.Select(entity => new { entity.Id, Type = entity.Type.Id.ToString(), entity.Position }).ToArray(), ct);
            int entityCleanupMark = capture.Mark();
            string entityCleanupCommand = protocol == 47 ? "kill @e[type=!Player]" : "kill @e[type=!minecraft:player]";
            string itemCleanupCommand = protocol == 47 ? "kill @e[type=Item]" : "kill @e[type=minecraft:item]";
            await server.WriteAsync("pillar-entity-clearance-before.json", new { mobLootRule, entityDropsRule, entityDropsQuery, entityCleanupCommand, itemCleanupCommand, entityCleanupMark, entitiesBeforeCleanup });
            await server.ConsoleAsync(entityCleanupCommand, ct);
            await server.ConsoleAsync(itemCleanupCommand, ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && !c.State.Self.InstantBuild && c.State.Self.OnGround && PillarNear(c.State.Self.Position, self)
                && c.State.World.GetColumn(destination) is not null && c.State.World.GetBlock(new BlockPos(x, 99, z)).Block.Id == Identifier.Minecraft("stone"), "real grounded Survival pillar fixture", ct);
            object[]? lastOwnedEntities = null;
            bool entityClearancePassed = false;
            try
            {
                await WaitAsync(client, c =>
                {
                    lastOwnedEntities = SnapshotPillarEntities(c);
                    return lastOwnedEntities.Length == 0;
                }, "native pillar entity death/removal delivery", ct);
                lastOwnedEntities = await client.InvokeAsync(SnapshotPillarEntities, ct);
                Assert.Empty(lastOwnedEntities);
                entityClearancePassed = true;
            }
            finally
            {
                await server.WriteAsync("pillar-entity-clearance.json", new
                {
                    mobLootRule,
                    entityDropsRule,
                    entityDropsQuery,
                    entityCleanupCommand,
                    itemCleanupCommand,
                    entityCleanupMark,
                    entityClearancePassed,
                    entitiesBeforeCleanup,
                    entitiesAfterCleanup = lastOwnedEntities,
                    lastOwnedPollSnapshot = lastOwnedEntities is not null,
                    removalFrames = capture.Since(entityCleanupMark).Where(frame => frame.Packet is ClientboundRemoveEntitiesPacket or ClientboundEntityEventPacket)
                        .Select(frame => new { frame.Milliseconds, packet = frame.Packet!.GetType().Name, frame.WireId, frame.Payload }).ToArray(),
                    spawnAndDropFrames = capture.Since(entityCleanupMark).Where(frame => frame.Packet is ClientboundAddEntityPacket or ClientboundAddMobPacket)
                        .Select(frame => new { frame.Milliseconds, packet = frame.Packet!.GetType().Name, frame.WireId, frame.Payload }).ToArray(),
                });
            }
            await AssertNativeSurvival();
            Assert.Contains(capture.Frames, frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == (x >> 4) && chunk.ChunkZ == (z >> 4)
                || frame.Packet is ClientboundMapChunkBulkPacket bulk && bulk.Columns.Any(column => column.Position.X == (x >> 4) && column.Position.Z == (z >> 4)));
            foreach (Direction face in new[] { Direction.Down, Direction.Up, Direction.North, Direction.South, Direction.West, Direction.East })
                await Place(face);
            Assert.Equal(6, scenes.Count);
            Assert.False(capture.Overflowed);
            Assert.True(capture.PayloadBytes <= 20 * 1024 * 1024);
            int actionReplay = capture.AssertRoundTrips(client.State.Registries!);
            int inventoryReplay = ReplayAdventureFrames(capture.Frames, [], version, client.State.Registries!);
            await client.DisconnectAsync(ct);
            string nativeDisconnect = protocol == 47
                ? Username + " lost connection: TextComponent{text='Disconnected',"
                : Username + " lost connection: Disconnected";
            await WaitAsync(() => server.ServerLog.Contains(nativeDisconnect, StringComparison.Ordinal), "native pillar peer EOF", ct);
            Assert.Equal(ClientStatus.Disconnected, client.Status);
            await server.WriteAsync("pillar-acceptance.json", new { release, protocol, scenes, physics = true, explicitCoordinateCaller = true, nativeAxisAndStacks = true, actionReplay, inventoryReplay, peerObservedDisconnect = true, capture.PayloadBytes, recordingCap = 20 * 1024 * 1024, postActionTailSeconds = 2.1, protocol47Profile = protocol == 47 ? "1.8.9" : null, nativeMinecraftClientInputOrRenderingExecuted = false, protocol777LocalSwing = "Production owner Local intent, one actual use packet and no wire swing; no graphical animation claim." });
            passed = true;

            async Task ClearCells()
                => await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 104 {z + 4} minecraft:air", ct);

            async Task AssertNativeSurvival()
            {
                string response = await server.CommandAsync(protocol == 47
                    ? $"testfor @a[name={Username},m=0] {{OnGround:1b}}"
                    : $"execute if entity @a[name={Username},gamemode=survival,nbt={{OnGround:1b}}]", ct);
                string expected = protocol switch
                {
                    47 => "Found " + Username,
                    393 => "Test passed, count: 1",
                    777 => "Test passed. Count: 1",
                    _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
                };
                Assert.Equal(expected, response);
            }

            async Task AssertNativeCount(int count)
            {
                string response = await server.CommandAsync(protocol == 47 ? $"clear {Username} minecraft:{item} 0 0" : $"clear {Username} minecraft:{item} 0", ct);
                Assert.Contains(response, protocol == 47
                    ? [$"{Username} has {count} items that match the criteria"]
                    : new[] { $"Found {count} matching items on player {Username}", $"Found {count} matching item(s) on player {Username}" });
            }

            async Task Place(Direction face)
            {
                await ClearCells();
                BlockPos clicked = destination.Offset(face.Opposite());
                await server.ConsoleAsync($"setblock {clicked.X} {clicked.Y} {clicked.Z} minecraft:stone", ct);
                await WaitAsync(client, c => c.State.World.GetBlock(destination).IsAir && c.State.World.GetBlock(clicked).Block.Id == Identifier.Minecraft("stone"), "native cleared destination and actual clicked support", ct);
                await AssertBlockAsync(server, protocol, destination, "air", ct);
                await AssertBlockAsync(server, protocol, clicked, "stone", ct);
                await server.ConsoleAsync($"clear {Username}", ct);
                await server.ConsoleAsync($"give {Username} minecraft:{item} 32", ct);
                await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft(item) && c.State.Inventory.GetSlot(36).Count == 32
                    && c.State.Inventory.GetSlot(36).Damage == 0 && c.State.Inventory.GetSlot(36).Components.HasNoPatch, "native default log stack", ct);
                await AssertNativeCount(32);
                await WaitAsync(client, c => c.State.Self.OnGround && PillarNear(c.State.Self.Position, self), "retained actual physics location", ct);
                await AssertNativeSurvival();
                Vec3d cursor = face switch
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
                var snapshot = await client.Snapshots.SelfAsync(ct);
                Assert.True(snapshot.EyePosition.Subtract(hit).LengthSqr() < 4.5 * 4.5);
                Assert.False((await client.InvokeAsync(ReadSlabPhysics, ct)).Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)));
                var observed = await client.InvokeAsync(c =>
                {
                    object inputs = ReadPillarOwnedInputs(c, protocol, clicked, destination);
                    var prediction = ReadSlabPrediction(c, clicked, face, cursor);
                    return new { inputs, prediction, afterPrediction = ReadPillarOwnedInputs(c, protocol, clicked, destination) };
                }, ct);
                await server.WriteAsync("pillar-" + face + "-inputs.json", observed);
                var prediction = observed.prediction;
                Assert.True(prediction.SendPacket);
                Assert.Equal("Success", prediction.Result);
                Assert.Equal(protocol == 777 ? "Local" : "Packet", prediction.Swing);
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(clicked, face, cursor, Hand.Main, insideBlock: false, ct: ct);
                string axis = face is Direction.Down or Direction.Up ? "y" : face is Direction.North or Direction.South ? "z" : "x";
                int metadata = axis == "y" ? 0 : axis == "z" ? 8 : 4;
                await WaitAsync(client, c => PillarState(c.State.World.GetBlock(destination), protocol, item, axis, metadata)
                    && c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft(item) && c.State.Inventory.GetSlot(36).Count == 31, "fresh server log axis and consumed stack", ct);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                await AssertBlockAsync(server, protocol, destination, protocol == 47 ? item : item + "[axis=" + axis + "]", ct, protocol == 47 ? metadata : null);
                await AssertNativeCount(31);
                CapturedFrame[] segment = capture.Since(mark);
                CapturedFrame[] actions = segment.Where(IsUseOrAnimation).ToArray();
                Assert.Equal(protocol == 777 ? 1 : 2, actions.Length);
                if (protocol == 47)
                {
                    var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(actions[0].Packet);
                    Assert.Equal((clicked, (int)face), (use.Position, use.Face));
                    Assert.Equal((Identifier.Minecraft(item), 32, 0), (use.HeldItem.Item.Id, use.HeldItem.Count, use.HeldItem.Damage));
                    Assert.Equal(((byte)(cursor.X * 16), (byte)(cursor.Y * 16), (byte)(cursor.Z * 16)), (use.CursorX, use.CursorY, use.CursorZ));
                }
                else
                {
                    var use = Assert.IsType<ServerboundUseItemOnPacket>(actions[0].Packet);
                    Assert.Equal((clicked, (int)face, 0), (use.Position, use.Face, use.Hand));
                    Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
                    Assert.False(use.Inside || use.WorldBorderHit);
                    if (protocol >= 759)
                        Assert.Single(segment, frame => frame.Packet is ClientboundBlockChangedAckPacket ack && ack.Sequence == use.Sequence);
                }
                if (protocol != 777)
                    Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                Assert.DoesNotContain(actions, frame => frame.Packet is ServerboundPunchPacket);
                Assert.True(segment[^1].Milliseconds - actions[0].Milliseconds >= 900);
                Assert.False(capture.Overflowed);
                scenes.Add(new { face, axis, legacyMetadata = protocol == 47 ? metadata : (int?)null, clicked, destination, cursor, prediction, observedCount = 31, actionPackets = actions.Select(frame => new { frame.Flow, frame.WireId, packet = frame.Packet!.GetType().Name, frame.Payload }).ToArray() });
                await server.WriteAsync("pillar-scenarios.json", scenes);
            }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "default log six-face placement", completedCases = scenes.Count, capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnectDeadline.Token);
            }
        }
    }

    private static object[] SnapshotPillarEntities(UmpkClient client)
        => client.State.Entities.All.Select(entity => (object)new { entity.Id, Type = entity.Type.Id.ToString(), entity.Position }).ToArray();

    private static object ReadPillarOwnedInputs(UmpkClient client, int protocol, BlockPos clicked, BlockPos destination)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        ClientState state = client.State;
        object membership = typeof(ClientState).GetProperty("EntityMembership", flags)!.GetValue(state)!;
        object motion = typeof(ClientState).GetProperty("EntityMotion", flags)!.GetValue(state)!;
        object interaction = client.Actions.Interaction;
        object? physicsOwner = typeof(UmpkClient).GetField("_physics", flags)!.GetValue(client);
        PhysicsState? physics = physicsOwner is null ? null
            : (PhysicsState?)physicsOwner.GetType().GetProperty("EngineState", flags)!.GetValue(physicsOwner);
        var held = state.Inventory.GetSlot(36 + state.Self.HeldSlot);
        var data = JavaGameData.BlockInteractionData(protocol);
        BlockState clickedState = state.World.GetBlock(clicked);
        bool pillarSupported = clickedState.IsValid && data.TryGetItem(held.Item.Id, out var use)
            && (bool)typeof(UmpkClient).Assembly.GetType("Umpk.Client.Actions.VanillaPillarPlacement", throwOnError: true)!
                .GetMethod("Supports", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(null, [protocol, held.Item.Id, use, clickedState.Source, JavaGameData.BlockShapes(protocol)])!;
        return new
        {
            diagnosticOnly = true,
            ownerInputs = VanillaInteractionAcceptanceDiagnostics.Read(client, protocol, clicked, destination),
            mainAvailable = state.Inventory.GetType().GetMethod("IsPlayerSlotAvailable", flags)!.Invoke(state.Inventory, [36 + state.Self.HeldSlot]),
            offhandAvailable = state.Inventory.GetType().GetMethod("IsPlayerSlotAvailable", flags)!.Invoke(state.Inventory, [45]),
            state.Inventory.HasOpenContainer,
            hasPendingPrediction = state.Inventory.GetType().GetProperty("HasPendingPrediction", flags)!.GetValue(state.Inventory),
            itemEnabled = interaction.GetType().GetMethod("IsBlockUseItemEnabled", flags)!.Invoke(interaction, [Hand.Main]),
            localMountUnresolved = motion.GetType().GetProperty("IsLocalMountUnresolved", flags)!.GetValue(motion),
            physicsKnown = physics is not null,
            physics = physics is { } body ? new { body.Position, body.Velocity, body.OnGround, body.BoundingBox } : null,
            client.Status,
            inventoryFeature = state.Features.Inventory,
            entitiesFeature = state.Features.Entities,
            pillarSupported,
            cells = new[] { ReadCell(clicked), ReadCell(clicked.Below()), ReadCell(destination), ReadCell(destination.Below()) },
        };

        object ReadCell(BlockPos position)
        {
            BlockState block = state.World.GetBlock(position);
            BlockState lower = state.World.GetBlock(position.Below());
            return new
            {
                position,
                packetColumnMember = membership.GetType().GetMethod("HasColumn", flags)!.Invoke(membership, [position]),
                storageColumnExists = state.World.GetColumn(position) is not null,
                block.IsValid,
                block.StateId,
                id = block.IsValid ? block.Block.Id.ToString() : null,
                lowerValid = lower.IsValid,
                lowerStateId = lower.StateId,
                replacement = block.IsValid && data.TryGetCubeReplacement(block.StateId, lower.IsValid ? lower.StateId : null, out var replacement) ? replacement.ToString() : null,
            };
        }
    }

    private static bool PillarState(BlockState state, int protocol, string item, string axis, int metadata)
        => state.IsValid && state.Block.Id == Identifier.Minecraft(item) && (protocol == 47 ? state.LegacyMeta == metadata : state.TryGetProperty("axis", out string value) && value == axis);

    private static bool PillarNear(Vec3d actual, Vec3d expected)
        => Math.Abs(actual.X - expected.X) < .01 && Math.Abs(actual.Y - expected.Y) < .01 && Math.Abs(actual.Z - expected.Z) < .01;
}
