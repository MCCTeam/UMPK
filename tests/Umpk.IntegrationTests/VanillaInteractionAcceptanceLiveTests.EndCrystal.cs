using System.Security.Cryptography;
using System.Text.Json;
using Umpk.Client;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.IntegrationTests;

public sealed partial class VanillaInteractionAcceptanceLiveTests
{
    public static IEnumerable<object[]> EndCrystalReleases()
    {
        string[] names = (Environment.GetEnvironmentVariable("UMPK_CRYSTAL_RELEASES") ?? "1.21.4,26.3").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        foreach (string name in names)
        {
            Assert.Contains(name, new[] { "1.21.4", "26.3" });
            Assert.True(JavaVersions.TryGetByName(name, out JavaVersion version));
            yield return [name, version.Version.Protocol];
        }
    }

    [NightlyTheory]
    [MemberData(nameof(EndCrystalReleases))]
    public async Task EndCrystalGeometryControlsViewMiningAndPlacement(string release, int protocol)
    {
        string prerequisite = Environment.GetEnvironmentVariable("UMPK_CRYSTAL_PREREQUISITE_RECEIPT") ?? throw new InvalidOperationException("Supply the final merged-producer prerequisite receipt before native execution.");
        byte[] receipt = await File.ReadAllBytesAsync(prerequisite);
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_CRYSTAL_PREREQUISITE_SHA256"), Convert.ToHexStringLower(SHA256.HashData(receipt)));
        using JsonDocument ready = JsonDocument.Parse(receipt);
        Assert.True(ready.RootElement.GetProperty("native_ready").GetBoolean());
        Assert.False(ready.RootElement.GetProperty("offline_compatibility_only").GetBoolean());
        Assert.Equal(Environment.GetEnvironmentVariable("UMPK_INTERACTION_PRODUCER"), ready.RootElement.GetProperty("producer_commit").GetString());
        Assert.Equal(25, ready.RootElement.GetProperty("passed_repository_gates").GetInt32());
        Assert.Equal(115, ready.RootElement.GetProperty("executed_EndCrystal_cases").GetInt32());
        Assert.Equal(33, ready.RootElement.GetProperty("consumer_DLL_comparisons").GetInt32());
        Assert.Equal(1, ready.RootElement.GetProperty("executed_passing_AOT_smokes").GetInt32());
        Assert.True(ready.RootElement.GetProperty("aot_bytes").GetInt32() < 25_165_824);
        foreach (JsonProperty binary in ready.RootElement.GetProperty("integration_binaries").EnumerateObject())
            Assert.Equal(binary.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, binary.Name)))));
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion version));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        CancellationToken ct = deadline.Token;
        string settingsFile = Environment.GetEnvironmentVariable("UMPK_CRYSTAL_GENERATOR_SETTINGS_FILE") ?? throw new InvalidOperationException("Supply pinned original flat settings.");
        byte[] settings = await File.ReadAllBytesAsync(settingsFile, ct);
        Assert.Equal("65c9e53a491c688c58eac7422eb4f66b8ca01cfb16be4d309fd3c2a31cb5a898", Convert.ToHexStringLower(SHA256.HashData(settings)));
        string source = Path.Combine(FixturePaths.RepoRoot(), "tests/Umpk.IntegrationTests/VanillaInteractionAcceptanceLiveTests.EndCrystal.cs");
        string sourceHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(source, ct)));
        Assert.Equal(ready.RootElement.GetProperty("fixture_source_sha256").GetString(), sourceHash);
        await using var server = await VanillaInteractionAcceptanceHarness.StartAsync(release, protocol, ct, flatWorldSettings: System.Text.Encoding.UTF8.GetString(settings));
        output.WriteLine($"Evidence: {server.DirectoryPath}");
        await server.WriteAsync("crystal-source.json", new { sha256 = sourceHash });
        await using UmpkClient client = new UmpkClientBuilder().UseVersion(version).UseProfile(new GameProfile(Guid.NewGuid(), Username)).UseStaticRegistries(JavaGameData.Registries(protocol)).Build();
        var capture = new Capture(version);
        client.PacketFrameObserved += capture.Observe;
        var markers = new List<string>();
        var scenarios = new List<object>();
        var actors = new List<object>();
        int? crystalId = null;
        Vec3d crystalPosition = default;
        int retainedDestroyDelay = 0;
        bool passed = false;
        capture.Start();
        try
        {
            await client.ConnectAsync(new ServerEndpoint("127.0.0.1", (ushort)server.Port), ct);
            Assert.True(await client.Spawned.WaitAsync(ct));
            await WaitAsync(client, c => c.State.IsLocalChunkLoaded && c.State.Self.OnGround, "native dry flat spawn", ct);
            Assert.True(client.State.Features.Physics);
            Vec3d initial = await client.InvokeAsync(c => c.State.Self.Position, ct);
            int x = ((int)Math.Floor(initial.X) >> 4) * 16 + 8, z = ((int)Math.Floor(initial.Z) >> 4) * 16 + 8;
            Vec3d self = new(x + .5, 100, z + .5);
            await VerifyCrystalFlatGenerator(server, client, settingsFile, x, z, ct);
            await server.ConsoleAsync("difficulty peaceful", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doMobSpawning false" : "gamerule minecraft:spawn_mobs false", ct);
            await server.ConsoleAsync(protocol < 774 ? "gamerule doTileDrops false" : "gamerule minecraft:block_drops false", ct);
            await server.ConsoleAsync(FormattableString.Invariant($"tp {Username} {self.X:R} 100 {self.Z:R} 0 0"), ct);
            await server.ConsoleAsync($"gamemode survival {Username}", ct);
            await client.Actions.Inventory.SelectHeldSlotAsync(0, ct);
            await WaitAsync(client, c => c.State.Self.GameMode == GameMode.Survival && !c.State.Self.InstantBuild && c.State.Self.OnGround && Near(c.State.Self.Position, self), "grounded Survival owner", ct);
            Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.ToArray(), ct));
            await Marker("execute unless entity @e[type=!minecraft:player]", "zero-ambient");
            await ClearTerrain();

            // A native tp is not a tracking witness on769 until dirty entity data runs the original ServerEntity publication branch.
            await Spawn(new(x + 8.5, 100, z + 2.5));
            int publication = capture.Mark();
            await MoveCrystal(new(x + 7.5, 100, z + 2.5), publication);
            await server.WriteAsync("crystal-publication-precondition.json", new { protocol, harmlessShowBottomToggle = protocol == 769, crystalId, crystalPosition, frames = capture.Since(publication).Where(frame => IsPositionFrame(frame, crystalId!.Value) || frame.Packet is ClientboundSetEntityDataPacket metadata && metadata.EntityId == crystalId).Select(frame => new { frame.Flow, frame.WireId, frame.Payload, packet = frame.Packet!.GetType().Name }).ToArray(), nativeCoordinatesMatched = true, actualPositionPacketRequired = true });
            await Remove();

            await Rejected("initial-near-view-interception", new(x, 101, z + 4), unknown: false);
            await Rejected("initial-unknown-body-context", new(x - 1, 101, z + 3), unknown: true);
            await Natural("far-view-mining", new(x, 101, z + 3), "far", explicitCoordinates: false);
            await Natural("block-before-crystal", new(x + 1, 101, z + 3), "behind", explicitCoordinates: false);
            await Natural("explicit-coordinate-contract", new(x - 1, 101, z + 4), "near", explicitCoordinates: true);
            await Held("held-view-interception", new(x + 1, 101, z + 4), unknown: false);
            await Held("held-unknown-body-context", new(x + 2, 101, z + 3), unknown: true);
            await Place(near: true);
            await Place(near: false);
            await Remove();
            Assert.Empty(await client.InvokeAsync(c => c.State.Entities.All.ToArray(), ct));

            Assert.False(capture.Overflowed);
            Assert.True(capture.PayloadBytes <= 20 * 1024 * 1024);
            int ordinary = capture.AssertRoundTrips(client.State.Registries!);
            int entities = AssertMotionRoundTrips(version, capture.Frames, client.State.Registries!);
            int actions = ReplayAdventureFrames(capture.Frames, [], version, client.State.Registries!);
            var dispositions = new List<object>();
            int causal = ReplayAdventureCausalFrames(capture.Frames, version, client.State.Registries!, markers, dispositions);
            AssertNoCrystalEntityAttack(capture.Frames);
            await server.WriteAsync("crystal-causal-replay.json", new { markers, byteIdenticalFrames = causal, frames = dispositions });
            await client.DisconnectAsync(ct);
            await WaitAsync(() => server.ServerLog.Contains(Username + " lost connection", StringComparison.Ordinal), "native peer disconnect", ct);
            Assert.Equal(9, scenarios.Count);
            await server.WriteAsync("crystal-acceptance.json", new { release, protocol, physics = true, scenarios, actors, ordinary, entities, actions, causal, exactMarkers = markers.Count, peerObservedDisconnect = true, originalMinecraftInputOrNetworkExecuted = false, EndCrystalItemUseOnExercised = false, noPistonOrShulkerFixture = true, recordingCap = 20 * 1024 * 1024, capture.PayloadBytes });
            passed = true;

            async Task Marker(string condition, string label)
            {
                string marker = "crystal-" + label + "-" + Guid.NewGuid().ToString("N");
                Assert.DoesNotContain(marker, markers);
                markers.Add(marker);
                await server.CommandAsync(condition + " run say " + marker, ct);
                await WaitAsync(() => server.ServerLog.Split('\n').Any(line => line.TrimEnd('\r').EndsWith("[Rcon] " + marker, StringComparison.Ordinal)) && capture.Frames.Any(frame => AdventureMarkerMatches(frame.Packet, marker)), "exact native causal marker " + label, ct);
                await client.InvokeAsync(_ => true, ct);
            }

            async Task ClearTerrain()
            {
                await server.ConsoleAsync($"fill {x - 5} 99 {z - 5} {x + 10} 99 {z + 10} minecraft:stone", ct);
                await server.ConsoleAsync($"fill {x - 5} 100 {z - 5} {x + 10} 104 {z + 10} minecraft:air", ct);
                await WaitAsync(client, c => c.State.Self.OnGround && Near(c.State.Self.Position, self), "unchanged native self", ct);
                await ServerPositionNearAsync(server, Username, self, .01, ct);
            }

            async Task Remove()
            {
                if (crystalId is not int id)
                    return;
                int mark = capture.Mark();
                await server.ConsoleAsync("kill @e[type=minecraft:end_crystal,tag=umpk-crystal]", ct);
                await WaitAsync(client, c => c.State.Entities.Get(id) is null, "owned native removal applied", ct);
                Assert.Contains(capture.Since(mark), frame => frame.Packet is ClientboundRemoveEntitiesPacket removed && removed.EntityIds.Contains(id));
                Assert.DoesNotContain(capture.Since(mark), frame => frame.Packet is ClientboundExplodePacket);
                AssertMotionRoundTrips(version, capture.Frames, client.State.Registries!);
                crystalId = null;
                await Marker("execute unless entity @e[type=!minecraft:player]", "removed");
            }

            async Task Spawn(Vec3d position)
            {
                Assert.Null(crystalId);
                int mark = capture.Mark();
                await server.ConsoleAsync(FormattableString.Invariant($"summon minecraft:end_crystal {CrystalNativeCommandPosition(position)} {{Tags:[\"umpk-crystal\"],ShowBottom:1b}}"), ct);
                await WaitAsync(client, c => c.State.Entities.All.Count() == 1, "exact owned crystal", ct);
                Entity entity = Assert.Single(await client.InvokeAsync(c => c.State.Entities.All.ToArray(), ct));
                Assert.Equal(Identifier.Minecraft("end_crystal"), entity.Type.Id);
                crystalId = entity.Id;
                crystalPosition = position;
                var spawn = Assert.IsType<ClientboundAddEntityPacket>(Assert.Single(capture.Since(mark), frame => frame.Packet is ClientboundAddEntityPacket added && added.EntityId == entity.Id).Packet);
                Assert.Equal(entity.Uuid, spawn.Uuid);
                Assert.Equal(position, new Vec3d(spawn.X, spawn.Y, spawn.Z));
                Assert.Equal(position, await client.InvokeAsync(c => c.State.Entities.Get(entity.Id)!.Position, ct));
                AssertMotionRoundTrips(version, capture.Frames, client.State.Registries!);
                AssertCrystalOneEntityResponse(await server.CommandAsync("execute if entity @e[type=!minecraft:player]", ct), protocol);
                await ServerPositionNearAsync(server, "@e[tag=umpk-crystal,limit=1]", position, 1e-9, ct);
                await RequireTerrain(position, allowNonAir: false);
                await Marker("execute if entity @e[type=minecraft:end_crystal,tag=umpk-crystal]", "owned");
                actors.Add(new { id = entity.Id, uuid = entity.Uuid, type = entity.Type.Id.ToString(), position, nativePositionMatched = true, originalSpawnFrame = capture.Mark() });
            }

            async Task MoveCrystal(Vec3d position, int mark)
            {
                int id = crystalId!.Value;
                await server.ConsoleAsync(FormattableString.Invariant($"tp @e[type=minecraft:end_crystal,tag=umpk-crystal,limit=1] {CrystalNativeCommandPosition(position)}"), ct);
                if (protocol == 769)
                    await server.ConsoleAsync("data merge entity @e[type=minecraft:end_crystal,tag=umpk-crystal,limit=1] {ShowBottom:0b}", ct);
                await WaitAsync(client, c => c.State.Entities.Get(id)?.Position == position && capture.Since(mark).Any(frame => IsPositionFrame(frame, id)), "actual native crystal position publication", ct);
                if (protocol == 769)
                    await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ClientboundSetEntityDataPacket data && data.EntityId == id && data.Metadata.Entries.Any(entry => entry.Index == 9 && entry.Value.Equals(MetadataValue.Boolean(false)))), "actual native ShowBottom publication", ct);
                AssertMotionRoundTrips(version, capture.Frames, client.State.Registries!);
                await ServerPositionNearAsync(server, "@e[tag=umpk-crystal,limit=1]", position, 1e-9, ct);
                crystalPosition = position;
            }

            async Task RequireTerrain(Vec3d position, bool allowNonAir)
            {
                for (int bx = (int)Math.Floor(position.X - 1); bx <= (int)Math.Floor(position.X + 1); bx++)
                    for (int bz = (int)Math.Floor(position.Z - 1); bz <= (int)Math.Floor(position.Z + 1); bz++)
                    {
                        int columnX = bx >> 4, columnZ = bz >> 4;
                        await WaitAsync(() => capture.Frames.Any(frame => frame.Packet is ClientboundLevelChunkPacket { FullChunk: true } chunk && chunk.ChunkX == columnX && chunk.ChunkZ == columnZ), "native full body column", ct);
                        for (int by = 100; by <= 102; by++)
                        {
                            var cell = new BlockPos(bx, by, bz);
                            Assert.True(await client.InvokeAsync(c => c.State.World.GetColumn(cell) is not null && c.State.World.GetBlock(cell).IsValid && (allowNonAir || c.State.World.GetBlock(cell).IsAir), ct));
                        }
                    }
            }

            async Task Prepare(BlockPos target, string where)
            {
                await Remove();
                await ClearTerrain();
                await server.ConsoleAsync($"setblock {target.X} {target.Y} {target.Z} minecraft:stone", ct);
                await WaitAsync(client, c => c.State.World.GetBlock(target).Block.Id == Identifier.Minecraft("stone"), "real stone target", ct);
                Vec3d position = where switch
                {
                    "near" => NearCrystal(target),
                    "behind" => new(target.X + .5, 100, target.Z + 2),
                    _ => new(x + 7.5, 100, z + 7.5),
                };
                await Spawn(position);
                await client.Actions.Movement.LookAtAsync(target.Center, ct);
                await WaitAsync(async () => (await client.Snapshots.RaycastViewAsync(ct: ct))?.BlockPos == target, "public block-only raycast", ct);
                await AssertBlockAsync(server, protocol, target, "stone", ct);
            }

            Vec3d NearCrystal(BlockPos target) => new(self.X + (target.X + .5 - self.X) * .375, 100, z + 2);

            async Task HoldItem(string? item, int count = 1)
            {
                await server.ConsoleAsync($"clear {Username}", ct);
                await WaitAsync(client, c => c.State.Inventory.GetSlot(36).IsEmpty, "native cleared hand", ct);
                await Marker($"execute unless items entity {Username} weapon.mainhand *", "empty-hand");
                if (item is not null)
                {
                    await server.ConsoleAsync($"give {Username} minecraft:{item} {count}", ct);
                    await WaitAsync(client, c => c.State.Inventory.GetSlot(36).Item.Id == Identifier.Minecraft(item) && c.State.Inventory.GetSlot(36).Count == count, "native held item applied", ct);
                    await Marker($"execute if items entity {Username} weapon.mainhand minecraft:{item}[count={count}]", "held");
                }
            }

            async Task Tail(int mark, BlockPos target, string expected)
            {
                await Task.Delay(1100, ct);
                await Marker($"execute if block {target.X} {target.Y} {target.Z} minecraft:{expected}", "post-action");
                CapturedFrame[] segment = capture.Since(mark);
                AssertNoCrystalEntityAttack(segment);
                CapturedFrame? last = segment.LastOrDefault(IsMiningOrAnimation);
                if (last is not null)
                    Assert.True(segment[^1].Milliseconds - last.Milliseconds >= 900);
                await ServerPositionNearAsync(server, "@e[tag=umpk-crystal,limit=1]", crystalPosition, 1e-9, ct);
            }

            async Task MakeUnknown()
            {
                var bodyCell = BlockPos.Containing(crystalPosition);
                int mark = capture.Mark();
                await server.ConsoleAsync($"setblock {bodyCell.X} {bodyCell.Y} {bodyCell.Z} minecraft:stone", ct);
                await WaitAsync(client, c => c.State.World.GetBlock(bodyCell).Block.Id == Identifier.Minecraft("stone"), "actual non-air crystal body context", ct);
                Assert.Contains(capture.Since(mark), frame => frame.Packet is ClientboundBlockUpdatePacket block && block.Position == bodyCell);
                await RequireTerrain(crystalPosition, allowNonAir: true);
                await Marker($"execute if block {bodyCell.X} {bodyCell.Y} {bodyCell.Z} minecraft:stone", "unknown-body");
            }

            async Task Rejected(string label, BlockPos target, bool unknown)
            {
                await Prepare(target, unknown ? "far" : "near");
                await HoldItem(null);
                if (unknown)
                    await MakeUnknown();
                int mark = capture.Mark();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Actions.Interaction.DigBlockInViewAsync(target, ct: ct));
                await Tail(mark, target, "stone");
                Assert.DoesNotContain(capture.Since(mark), IsMiningOrAnimation);
                scenarios.Add(new { label, target, crystalPosition, noAutomaticEntityAttack = true, conservativeUnknown = unknown, nativeBlockAfter = "stone", publicBlockRaycastContract = "unchanged" });
            }

            async Task Natural(string label, BlockPos target, string where, bool explicitCoordinates)
            {
                await Prepare(target, where);
                await HoldItem("diamond_pickaxe");
                Direction face = explicitCoordinates ? Direction.Up : (await client.Snapshots.RaycastViewAsync(ct: ct))!.Value.Face;
                int mark = capture.Mark();
                if (explicitCoordinates)
                    await client.Actions.Interaction.DigBlockAsync(target, ct: ct);
                else
                    await client.Actions.Interaction.DigBlockInViewAsync(target, ct: ct);
                await WaitAsync(client, c => c.State.World.GetBlock(target).IsAir, "native completed mining", ct);
                await AssertBlockAsync(server, protocol, target, "air", ct);
                await Tail(mark, target, "air");
                CapturedFrame[] actions = capture.Since(mark).Where(IsMiningOrAnimation).ToArray();
                AssertCrystalCompletedActionFields(actions, protocol, target, face, retainedDestroyDelay);
                scenarios.Add(new { label, target, crystalPosition, explicitCoordinates, retainedDestroyDelay, nativeBlockAfter = "air", packetOrder = actions.Select(CrystalPacketName).ToArray() });
                retainedDestroyDelay = 5;
            }

            async Task Held(string label, BlockPos target, bool unknown)
            {
                await Prepare(target, "far");
                await HoldItem(null);
                int mark = capture.Mark();
                Task mining = client.Actions.Interaction.DigBlockInViewAsync(target, ct: ct);
                await WaitAsync(() => capture.Since(mark).Any(frame => frame.Packet is ServerboundPlayerActionPacket { Action: 0 }) && capture.Since(mark).Count(frame => frame.Packet is ServerboundSwingPacket or ServerboundPunchPacket) >= 8, "real active held input before interception", ct);
                Assert.False(mining.IsCompleted);
                int change = capture.Mark();
                if (unknown)
                    await MakeUnknown();
                else
                    await MoveCrystal(NearCrystal(target), change);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5), ct));
                await Tail(mark, target, "stone");
                CapturedFrame[] frames = capture.Since(mark);
                CapturedFrame[] actions = frames.Where(IsMiningOrAnimation).ToArray();
                AssertCrystalHeldActionFields(actions, protocol, target);
                int abortIndex = Array.FindLastIndex(frames, frame => frame.Packet is ServerboundPlayerActionPacket action && action.Action == CrystalAbortAction(protocol));
                Assert.Contains(frames.Take(abortIndex), frame => unknown ? frame.Packet is ClientboundBlockUpdatePacket block && block.Position == BlockPos.Containing(crystalPosition) : IsPositionFrame(frame, crystalId!.Value));
                scenarios.Add(new { label, target, crystalPosition, noAutomaticEntityAttack = true, conservativeUnknown = unknown, nativeBlockAfter = "stone", packetOrder = actions.Select(CrystalPacketName).ToArray() });
                retainedDestroyDelay = 0;
            }

            async Task Place(bool near)
            {
                await Remove();
                await ClearTerrain();
                var support = new BlockPos(x + 2, 99, z + 2);
                BlockPos destination = support.Above();
                await Spawn(near ? new(destination.X + .5, 100, destination.Z + .5) : new(x + 7.5, 100, z + 7.5));
                await HoldItem("stone", 32);
                int mark = capture.Mark();
                await client.Actions.Interaction.PlaceBlockAsync(support, Direction.Up, new(.5, 1, .5), ct: ct);
                await Tail(mark, destination, near ? "air" : "stone");
                await AssertBlockAsync(server, protocol, destination, near ? "air" : "stone", ct);
                await AssertCountAsync(server, protocol, near ? 32 : 31, ct);
                CapturedFrame[] uses = capture.Since(mark).Where(IsUseOrAnimation).ToArray();
                AssertUsePacket(Assert.Single(uses, frame => frame.Packet is ServerboundUseItemOnPacket), protocol, support);
                Assert.Equal(near || protocol == 777 ? 1 : 2, uses.Length);
                if (!near && protocol != 777)
                    AssertCrystalSwing(uses[1].Packet, protocol);
                scenarios.Add(new { label = near ? "near-placement-compatibility" : "far-placement-compatibility", destination, crystalPosition, nativeBlockAfter = near ? "air" : "stone", nativeCountAfter = near ? 32 : 31, packetSwingObservable = protocol != 777, usePackets = 1, animations = uses.Count(frame => frame.Packet is ServerboundSwingPacket or ServerboundPunchPacket) });
            }
        }
        finally
        {
            capture.Stop();
            client.PacketFrameObserved -= capture.Observe;
            await server.WriteAsync("crystal-capture.json", capture.Frames);
            await server.WriteAsync("crystal-markers.json", markers);
            await server.WriteAsync("crystal-result.json", new { passed, release, protocol, capture.PayloadBytes, capture.Overflowed });
            if (client.Status != ClientStatus.Disconnected)
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.DisconnectAsync(stop.Token);
            }
        }
    }

    private static void AssertCrystalOneEntityResponse(string response, int protocol)
    {
        Assert.Contains(protocol, new[] { 769, 777 });
        Assert.Equal(protocol == 777 ? "Test passed. Count: 1" : "Test passed, count: 1", response);
    }

    private static string CrystalNativeCommandPosition(Vec3d position) => $"{CrystalNativeCommandCoordinate(position.X, horizontal: true)} {CrystalNativeCommandCoordinate(position.Y, horizontal: false)} {CrystalNativeCommandCoordinate(position.Z, horizontal: true)}";

    private static string CrystalNativeCommandCoordinate(double value, bool horizontal)
    {
        Assert.True(double.IsFinite(value));
        Assert.InRange(value, -30_000_000d, 30_000_000d);
        Assert.True(value == 0 || Math.Abs(value) >= .0001, "The bounded fixture requires decimal command coordinates without scientific notation.");
        string token = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        Assert.DoesNotContain("E", token, StringComparison.OrdinalIgnoreCase);
        return horizontal && !token.Contains('.') ? token + ".0" : token;
    }

    private static int CrystalAbortAction(int protocol) => protocol == 777 ? 2 : 1;

    private static int CrystalStopAction(int protocol) => protocol == 777 ? 3 : 2;

    private static void AssertCrystalCompletedActionFields(CapturedFrame[] actions, int protocol, BlockPos target, Direction face, int retainedDestroyDelay)
    {
        Assert.Equal(9 + retainedDestroyDelay, actions.Length);
        Assert.Equal(7 + retainedDestroyDelay, actions.Count(frame => frame.Packet is ServerboundSwingPacket or ServerboundPunchPacket));
        var start = Assert.IsType<ServerboundPlayerActionPacket>(actions[0].Packet);
        var stop = Assert.IsType<ServerboundPlayerActionPacket>(actions[^2].Packet);
        Assert.Equal(0, start.Action);
        Assert.Equal(CrystalStopAction(protocol), stop.Action);
        Assert.Equal((target, (byte)face), (start.Position, start.Direction));
        Assert.Equal((target, (byte)face), (stop.Position, stop.Direction));
        Assert.True(start.Sequence > 0 && stop.Sequence > start.Sequence);
        Assert.All(actions.Skip(1).SkipLast(2).Append(actions[^1]), frame => AssertCrystalSwing(frame.Packet, protocol));
    }

    private static void AssertCrystalHeldActionFields(CapturedFrame[] actions, int protocol, BlockPos target)
    {
        Assert.Equal(new[] { 0, CrystalAbortAction(protocol) }, actions.Select(frame => frame.Packet).OfType<ServerboundPlayerActionPacket>().Select(packet => packet.Action));
        var abort = Assert.IsType<ServerboundPlayerActionPacket>(actions[^1].Packet);
        Assert.Equal(0, abort.Sequence);
        Assert.Equal(target, abort.Position);
        Assert.All(actions.Skip(1).SkipLast(1), frame => AssertCrystalSwing(frame.Packet, protocol));
    }

    private static void AssertNoCrystalEntityAttack(CapturedFrame[] frames) => Assert.DoesNotContain(frames, frame => frame.Packet is ServerboundInteractPacket or ServerboundAttackPacket);

    private static void AssertCrystalSwing(object? packet, int protocol)
    {
        if (protocol == 777)
            Assert.IsType<ServerboundPunchPacket>(packet);
        else
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packet).Hand);
    }

    private static string CrystalPacketName(CapturedFrame frame) => frame.Packet switch
    {
        ServerboundPlayerActionPacket action => $"action{action.Action}/sequence{action.Sequence}/face{action.Direction}",
        ServerboundSwingPacket swing => $"swing/hand{swing.Hand}/hasHand{swing.HasHand}",
        ServerboundPunchPacket => "punch",
        _ => throw new InvalidOperationException("Unexpected measured mining packet."),
    };

    private static async Task VerifyCrystalFlatGenerator(VanillaInteractionAcceptanceHarness server, UmpkClient client, string settingsFile, int x, int z, CancellationToken ct)
    {
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(server.DirectoryPath, "server.properties"), ct);
        var properties = lines.Where(line => line.StartsWith("level-type=", StringComparison.Ordinal) || line.StartsWith("generator-settings=", StringComparison.Ordinal)).ToDictionary(line => line[..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..].Replace("\\:", ":", StringComparison.Ordinal).Replace("\\=", "=", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal));
        Assert.Equal("minecraft:flat", properties["level-type"]);
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllBytesAsync(settingsFile, ct));
        using JsonDocument actual = JsonDocument.Parse(properties["generator-settings"]);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.Equal(-64, await client.InvokeAsync(c => c.State.World.Dimension.MinY, ct));
        foreach (string condition in new[] { $"execute if biome {x} 100 {z} minecraft:the_void", $"execute if block {x} -64 {z} minecraft:stone", $"execute if block {x} 99 {z} minecraft:stone", $"execute if block {x} 100 {z} minecraft:air" })
            Assert.Equal("Test passed", await server.CommandAsync(condition, ct));
        await server.WriteAsync("crystal-generator-preconditions.json", new { properties, minY = -64, floorY = 99, preScenarioFill = true, settingsSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(settingsFile, ct))) });
    }
}
