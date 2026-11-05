using System.Globalization;
using System.Reflection;
using Umpk.Client;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    [NightlyFact]
    public async Task Protocol393FallingBoundsDrivePlacementAndRemoval()
    {
        const string release = "1.13";
        const int protocol = 393;
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        Assert.Equal(protocol, version.Version.Protocol);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct);
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        client.PacketFrameObserved += capture.Observe;
        string actorTag = "umpk_falling393_" + Guid.NewGuid().ToString("N");
        var scenes = new List<object>();
        Entity? actor = null;
        bool passed = false;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded, "falling393 received native terrain", ct);
            Assert.True(client.State.Features.Physics && client.State.Features.Terrain && client.State.Features.Entities);
            Vec3d initial = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(initial.X) >> 4) * 16 + 8;
            int z = ((int)Math.Floor(initial.Z) >> 4) * 16 + 8;
            Vec3d self = new(x + .5, 100, z + .5);
            var destination = new BlockPos(x + 2, 100, z + 1);
            BlockPos clicked = destination.Below();
            Vec3d far = new(destination.X + .5, destination.Y + .125, destination.Z + 3.5);
            Vec3d near = new(destination.X + .5, destination.Y + .125, destination.Z + .5);
            await server.ConsoleAsync("difficulty peaceful", ct);
            foreach (string rule in new[] { "doMobSpawning", "doMobLoot", "doTileDrops", "doEntityDrops" })
                await server.ConsoleAsync($"gamerule {rule} false", ct);
            Assert.Equal("Gamerule doEntityDrops is currently set to: false", await server.CommandAsync("gamerule doEntityDrops", ct));
            await server.ConsoleAsync($"fill {x - 4} 99 {z - 4} {x + 4} 99 {z + 4} minecraft:stone", ct);
            await server.ConsoleAsync($"fill {x - 4} 100 {z - 4} {x + 4} 130 {z + 4} minecraft:air", ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} {self.Y:R} {self.Z:R} 0 0"), ct);
            await server.ConsoleAsync("gamemode survival " + Username, ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp @e[type=!minecraft:player] {x + .5:R} 100 {z - 2.5:R}"), ct);
            await server.ConsoleAsync("kill @e[type=!minecraft:player]", ct);
            await server.ConsoleAsync("kill @e[type=minecraft:item]", ct);
            await WaitAsync(client, c => !c.State.Entities.All.Any() && c.State.Self.GameMode == GameMode.Survival && !c.State.Self.InstantBuild && c.State.Self.OnGround && PillarNear(c.State.Self.Position, self)
                && c.State.World.GetColumn(destination) is not null && c.State.World.GetBlock(clicked).Block.Id == Identifier.Minecraft("stone"), "falling393 grounded body, terrain and ambient removal", ct);
            Assert.Contains(capture.Frames, frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == (x >> 4) && chunk.ChunkZ == (z >> 4));
            Assert.Equal("Test passed", await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct));
            Assert.Equal("Test passed, count: 1", await server.CommandAsync($"execute if entity @a[name={Username},gamemode=survival,nbt={{OnGround:1b}}]", ct));
            await Spawn(far);
            await Place("far-clear", true, far);
            await Move(near);
            await Place("near-obstructed", false, near);
            Entity first = actor!;
            await Remove();
            await Place("removed-clear", true, null);
            await Spawn(near);
            Assert.NotSame(first, actor);
            Assert.NotEqual(first.Id, actor!.Id);
            Assert.False(await client.InvokeAsync(c => HasFalling393Geometry(c, first), ct));
            await Place("replacement-near-obstructed", false, near);
            await Move(far);
            await Place("replacement-far-clear", true, far);
            await Remove();
            Assert.Equal(5, scenes.Count);
            Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.ToArray(), ct));
            int actionReplay = capture.AssertRoundTrips(client.State.Registries!);
            int contextReplay = AssertMotionRoundTrips(version, capture.Frames, client.State.Registries!);
            int inventoryReplay = ReplayAdventureFrames(capture.Frames, [], version, client.State.Registries!);
            await client.DisconnectAsync(ct);
            await WaitAsync(() => QuartzNativeDisconnected(server.ServerLog, protocol), "falling393 native peer EOF", ct);
            Assert.Equal(ClientStatus.Disconnected, client.Status);
            await server.WriteAsync("falling393-acceptance.json", new { release, protocol, scenes, physics = true, productionCurrentBoundsConsumed = true, explicitCoordinateCaller = true, nativeActorTimeStartsAtOne = true, noGravityMetadataIndex = 5, distinctNativeReplacementIds = true, actionReplay, contextReplay, inventoryReplay, peerObservedDisconnect = true, capture.PayloadBytes, recordingCap = 20 * 1024 * 1024, postActionTailSeconds = 2.1, nativeGraphicalClientExecuted = false, serverOwnedEffectsSimulated = false });
            passed = true;

            async Task Spawn(Vec3d position)
            {
                int mark = capture.Mark();
                var sourceCell = new BlockPos(x - 2, 128, z - 2);
                await AssertBlockAsync(server, protocol, sourceCell, "air", ct);
                await AssertBlockAsync(server, protocol, sourceCell.Below(), "air", ct);
                await server.ConsoleAsync($"setblock {sourceCell.X} {sourceCell.Y} {sourceCell.Z} minecraft:sand", ct);
                await WaitAsync(client, c => c.State.Entities.All.Count() == 1 && c.State.Entities.All.Single().Type.Id == Identifier.Minecraft("falling_block"), "falling393 natural native spawn from unsupported sand", ct);
                Entity current = await client.InvokeAsync(c => Assert.Single(c.State.Entities.All), ct);
                actor = current;
                CapturedFrame spawnFrame = Assert.Single(capture.Since(mark), frame => frame.Packet is ClientboundAddEntityPacket observedSpawn && observedSpawn.EntityId == current.Id);
                var spawn = Assert.IsType<ClientboundAddEntityPacket>(spawnFrame.Packet);
                double spawnY = (double)sourceCell.Y + (1.0F - .98F) / 2.0F;
                Assert.Equal((sourceCell.X + .5, spawnY, sourceCell.Z + .5, 66), (spawn.X, spawn.Y, spawn.Z, spawn.Data));
                Assert.Equal((0, 0, 0), ((int)spawn.VelocityX, (int)spawn.VelocityY, (int)spawn.VelocityZ));
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundSetEntityMotionPacket motion && motion.EntityId == current.Id && motion.VelocityY < 0), "falling393 genuine native gravity velocity before freezing", ct);
                string sourceSelector = $"@e[type=minecraft:falling_block,x={sourceCell.X},y=100,z={sourceCell.Z},dx=1,dy=30,dz=1]";
                Assert.Equal("Test passed, count: 1", await server.CommandAsync("execute if entity " + sourceSelector, ct));
                int freezeMark = capture.Mark();
                await server.ConsoleAsync($"data merge entity {sourceSelector[..^1]},limit=1] {{Tags:[\"{actorTag}\"],Time:1,NoGravity:1b,Motion:[0d,0d,0d],DropItem:0b,Silent:1b}}", ct);
                await WaitAsync(() => capture.Since(freezeMark).Any(frame => SlabMetadata(frame.Packet, current.Id, 5, true))
                    && capture.Since(freezeMark).Any(frame => frame.Packet is ClientboundSetEntityMotionPacket motion && motion.EntityId == current.Id && motion.VelocityX == 0 && motion.VelocityY == 0 && motion.VelocityZ == 0), "falling393 native NoGravity5 and zero-velocity transition", ct);
                await WaitAsync(client, c => c.State.Entities.Get(current.Id) is Entity observed && observed.Velocity == Vec3d.Zero, "falling393 native frozen velocity applied", ct);
                await AssertBlockAsync(server, protocol, sourceCell, "air", ct);
                await Move(position);
            }

            async Task Move(Vec3d position)
            {
                Entity current = actor ?? throw new InvalidOperationException("The native falling actor must exist before moving it.");
                int mark = capture.Mark();
                await server.ConsoleAsync(FormattableString.Invariant($"tp @e[type=minecraft:falling_block,tag={actorTag},limit=1] {position.X:R} {position.Y:R} {position.Z:R}"), ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundTeleportEntityPacket move && move.EntityId == current.Id
                    || frame.Packet is ClientboundMoveEntityPosPacket relative && relative.EntityId == current.Id
                    || frame.Packet is ClientboundMoveEntityPosRotPacket rotated && rotated.EntityId == current.Id), "falling393 native position transition", ct);
                await WaitAsync(client, c => PillarNear(c.State.Entities.Get(current.Id)!.Position, position), "falling393 applied native position transition", ct);
                await NativeActor(position);
            }

            async Task Remove()
            {
                Entity removed = actor!;
                int mark = capture.Mark();
                await server.ConsoleAsync($"kill @e[type=minecraft:falling_block,tag={actorTag}]", ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundRemoveEntitiesPacket packet && packet.EntityIds.Contains(removed.Id)), "falling393 native remove frame", ct);
                await WaitAsync(client, c => c.State.Entities.Get(removed.Id) is null, "falling393 removal applied", ct);
                Assert.False(await client.InvokeAsync(c => HasFalling393Geometry(c, removed), ct));
                Assert.Equal("Test passed", await server.CommandAsync($"execute unless entity @e[tag={actorTag}]", ct));
                actor = null;
            }

            async Task NativeActor(Vec3d position)
            {
                string predicate = FormattableString.Invariant($"execute if entity @e[type=minecraft:falling_block,tag={actorTag},limit=1,nbt={{Pos:[{position.X:R}d,{position.Y:R}d,{position.Z:R}d],Motion:[0d,0d,0d],NoGravity:1b,BlockState:{{Name:\"minecraft:sand\"}}}}]");
                Assert.Equal("Test passed, count: 1", await server.CommandAsync(predicate, ct));
                string age = await server.CommandAsync($"data get entity @e[type=minecraft:falling_block,tag={actorTag},limit=1] Time", ct);
                const string prefix = "Falling Block has the following entity data: ";
                Assert.StartsWith(prefix, age, StringComparison.Ordinal);
                Assert.True(int.TryParse(age[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int time));
                Assert.InRange(time, 1, 600);
            }

            async Task Place(string label, bool success, Vec3d? nativePosition)
            {
                if (actor is not null)
                    await server.ConsoleAsync($"data merge entity @e[type=minecraft:falling_block,tag={actorTag},limit=1] {{Time:1}}", ct);
                await server.ConsoleAsync($"setblock {destination.X} {destination.Y} {destination.Z} minecraft:air", ct);
                await server.ConsoleAsync("clear " + Username, ct);
                await server.ConsoleAsync($"give {Username} minecraft:stone 32", ct);
                await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft("stone") && c.State.Inventory.GetSlot(36).Count == 32
                    && c.State.World.GetBlock(destination).IsAir && c.State.World.GetBlock(clicked).Block.Id == Identifier.Minecraft("stone")
                    && c.State.Self.OnGround && PillarNear(c.State.Self.Position, self), "falling393 fresh terrain, held stack and grounded self", ct);
                if (nativePosition is Vec3d expected)
                    await NativeActor(expected);
                else
                    Assert.Equal("Test passed", await server.CommandAsync("execute unless entity @e[type=!minecraft:player]", ct));
                Vec3d cursor = new(.5, 1, .5);
                await client.Actions.Movement.LookAtAsync(new(clicked.X + .5, clicked.Y + 1, clicked.Z + .5), ct);
                Assert.True((await client.Snapshots.SelfAsync(ct)).EyePosition.Subtract(new(clicked.X + .5, clicked.Y + 1, clicked.Z + .5)).LengthSqr() < 4.5 * 4.5);
                Assert.False((await client.InvokeAsync(ReadSlabPhysics, ct)).Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)));
                var before = await client.InvokeAsync(c => new { inputs = ReadPillarOwnedInputs(c, protocol, clicked, destination), prediction = ReadSlabPrediction(c, clicked, Direction.Up, cursor), body = actor is null ? null : ReadSlabBody(c, actor.Id, protocol), actorPosition = actor?.Position, actorVelocity = actor?.Velocity }, ct);
                await server.WriteAsync("falling393-" + label + "-before.json", before);
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(clicked, Direction.Up, cursor, Hand.Main, insideBlock: false, ct: ct);
                int count = success ? 31 : 32;
                await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Count == count && (success ? c.State.World.GetBlock(destination).Block.Id == Identifier.Minecraft("stone") : c.State.World.GetBlock(destination).IsAir), "falling393 native placement and stack outcome", ct);
                await Task.Delay(TimeSpan.FromSeconds(2.1), ct);
                await AssertBlockAsync(server, protocol, destination, success ? "stone" : "air", ct);
                Assert.Equal($"Found {count} matching items on player {Username}", await server.CommandAsync($"clear {Username} minecraft:stone 0", ct));
                if (nativePosition is Vec3d after)
                    await NativeActor(after);
                CapturedFrame[] segment = capture.Since(mark);
                CapturedFrame[] actions = segment.Where(IsUseOrAnimation).ToArray();
                int replay = ReplayQuartzActions(actions, version, client.State.Registries!);
                scenes.Add(new { label, success, expectedCount = count, actualActions = actions.Select(frame => new { frame.Flow, frame.WireId, frame.Payload, packet = frame.Packet!.GetType().Name }).ToArray(), before, nativePosition, actorId = actor?.Id, replay, independentNativeBlockStackAndEntity = true });
                await server.WriteAsync("falling393-scenes.json", scenes);
                Assert.Equal(success ? 2 : 1, actions.Length);
                AssertUsePacket(actions[0], protocol, clicked);
                var use = Assert.IsType<ServerboundUseItemOnPacket>(actions[0].Packet);
                Assert.False(use.Inside || use.WorldBorderHit);
                if (success)
                    Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(actions[1].Packet).Hand);
                Assert.DoesNotContain(actions, frame => frame.Packet is ServerboundPunchPacket);
                Assert.True(segment[^1].Milliseconds - actions[0].Milliseconds >= 900);
                Assert.True(before.prediction.SendPacket);
                Assert.Equal(success ? "Success" : "Fail", before.prediction.Result);
                Assert.Equal(success ? "Packet" : "None", before.prediction.Swing);
                if (before.body is { } body)
                {
                    Assert.True(body.Member && body.Known && body.BlocksBuilding);
                    Assert.Equal(!success, body.Box.Intersects(Aabb.BlockAt(destination.X, destination.Y, destination.Z)));
                    Assert.Equal(Vec3d.Zero, before.actorVelocity);
                }
                Assert.False(capture.Overflowed);
            }
        }
        finally
        {
            capture.Stop();
            await server.WriteAsync("capture.json", capture.Frames);
            await server.WriteAsync("result.json", new { passed, release, protocol, scenario = "bounded falling393 placement context", completedCases = scenes.Count, capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var disconnect = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(disconnect.Token);
            }
        }
    }

    private static bool HasFalling393Geometry(UmpkClient client, Entity entity)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object geometry = typeof(ClientState).GetProperty("EntityGeometry", flags)!.GetValue(client.State)!;
        return (bool)geometry.GetType().GetMethod("TryGetFallingBlock", flags)!.Invoke(geometry, [entity, default(Aabb)])!;
    }
}
